using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Polling;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Polls.Handlers;

public sealed class PollChatAnnouncementHandler :
    IEventHandler<PollStarted>,
    IEventHandler<PollProgressed>,
    IEventHandler<PollFinalized>,
    IEventHandler<PollTerminated>,
    IEventHandler<PollArchived>,
    IEventSubscriber,
    IDisposable
{
    private readonly IChatMessenger _messenger;
    private readonly SettingsManager _settingsManager;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<string, DateTime> _lastProgressAnnouncement = new(StringComparer.Ordinal);
    private readonly object _sync = new();
    private readonly List<IDisposable> _subscriptions;

    public PollChatAnnouncementHandler(
        IChatMessenger messenger,
        SettingsManager settingsManager,
        IEventBus eventBus,
        TimeProvider timeProvider)
    {
        _messenger = messenger;
        _settingsManager = settingsManager;
        _timeProvider = timeProvider;
        _subscriptions =
        [
            eventBus.Subscribe<PollStarted>(this),
            eventBus.Subscribe<PollProgressed>(this),
            eventBus.Subscribe<PollFinalized>(this),
            eventBus.Subscribe<PollTerminated>(this),
            eventBus.Subscribe<PollArchived>(this),
        ];
    }

    private PollChatTemplatesSettings Templates => _settingsManager.Current.Twitch.Polls.ChatTemplates;

    public Task HandleAsync(PollStarted @event, CancellationToken cancellationToken)
    {
        var templates = Templates;

        if (!templates.StartEnabled)
        {
            return Task.CompletedTask;
        }

        var message = Render(templates.StartTemplate, @event.Snapshot, null, false);
        SendIfNotEmpty(message);
        return Task.CompletedTask;
    }

    public Task HandleAsync(PollProgressed @event, CancellationToken cancellationToken)
    {
        var templates = Templates;

        if (!templates.ProgressEnabled)
        {
            return Task.CompletedTask;
        }

        if (!ShouldAnnounceProgress(@event.Snapshot.PollId, templates.ProgressAnnounceIntervalSeconds))
        {
            return Task.CompletedTask;
        }

        var message = Render(templates.ProgressTemplate, @event.Snapshot, null, false);
        SendIfNotEmpty(message);
        return Task.CompletedTask;
    }

    public Task HandleAsync(PollFinalized @event, CancellationToken cancellationToken)
    {
        var templates = Templates;

        if (!templates.EndEnabled)
        {
            return Task.CompletedTask;
        }

        var message = Render(templates.EndTemplate, @event.Snapshot, @event.Winner, @event.WinnerIsTie);
        SendIfNotEmpty(message);
        return Task.CompletedTask;
    }

    public Task HandleAsync(PollTerminated @event, CancellationToken cancellationToken)
    {
        var templates = Templates;

        if (!templates.TerminatedEnabled)
        {
            return Task.CompletedTask;
        }

        var message = Render(templates.TerminatedTemplate, @event.Snapshot, null, false);
        SendIfNotEmpty(message);
        return Task.CompletedTask;
    }

    public Task HandleAsync(PollArchived @event, CancellationToken cancellationToken)
    {
        var templates = Templates;

        if (!templates.ArchivedEnabled)
        {
            return Task.CompletedTask;
        }

        var message = Render(templates.ArchivedTemplate, @event.Snapshot, null, false);
        SendIfNotEmpty(message);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private static string FormatChoices(IReadOnlyList<PollChoiceSnapshot> choices)
    {
        return string.Join(" | ", choices.Select((c, i) => $"{i + 1}) {c.Title}"));
    }

    private static string FormatStatus(PollSnapshotStatus status)
    {
        return status switch
        {
            PollSnapshotStatus.Active => "активно",
            PollSnapshotStatus.Completed => "завершено",
            PollSnapshotStatus.Terminated => "завершено досрочно",
            PollSnapshotStatus.Archived => "архивировано",
            PollSnapshotStatus.Moderated => "заблокировано модерацией",
            PollSnapshotStatus.Invalid => "отклонено",
            _ => status.ToString(),
        };
    }

    private bool ShouldAnnounceProgress(string pollId, int intervalSeconds)
    {
        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;

            if (_lastProgressAnnouncement.TryGetValue(pollId, out var lastUtc)
                && (now - lastUtc).TotalSeconds < intervalSeconds)
            {
                return false;
            }

            _lastProgressAnnouncement[pollId] = now;
            return true;
        }
    }

    private void SendIfNotEmpty(string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            _messenger.Send(message);
        }
    }

    private string Render(string template, PollSnapshot snapshot, PollChoiceSnapshot? winner, bool winnerIsTie)
    {
        var leader = snapshot.Leader;
        var winnerTitle = winnerIsTie ? "ничья" : winner?.Title ?? string.Empty;

        return MessageTemplate.For(template)
            .With("title", snapshot.Title)
            .With("choices", FormatChoices(snapshot.Choices))
            .With("leader", leader?.Title ?? string.Empty)
            .With("leaderVotes", (leader?.Votes ?? 0).ToString())
            .With("totalVotes", snapshot.TotalVotes.ToString())
            .With("durationLeft", FormatDurationLeft(snapshot))
            .With("winner", winnerTitle)
            .With("winnerVotes", (winner?.Votes ?? 0).ToString())
            .With("status", FormatStatus(snapshot.Status))
            .Render();
    }

    private string FormatDurationLeft(PollSnapshot snapshot)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var remaining = snapshot.EndsAtUtc - now;

        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        var totalMinutes = (int)Math.Round(remaining.TotalMinutes);

        return totalMinutes switch
        {
            0 => $"{(int)remaining.TotalSeconds} сек",
            1 => "1 мин",
            _ => $"{totalMinutes} мин",
        };
    }
}
