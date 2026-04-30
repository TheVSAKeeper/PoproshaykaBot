using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Polls.Handlers;

public sealed class PollAutoTriggerHandler :
    IEventHandler<StreamWentOnline>,
    IEventHandler<StreamWentOffline>,
    IEventHandler<BroadcastProfileApplied>,
    IEventSubscriber,
    IDisposable
{
    private readonly PollProfilesManager _profilesManager;
    private readonly IPollController _controller;
    private readonly PollSnapshotStore _store;
    private readonly SettingsManager _settingsManager;
    private readonly TimeProvider _timeProvider;
    private readonly Dictionary<Guid, DateTime> _lastAutoStartedAt = new();
    private readonly object _sync = new();
    private readonly List<IDisposable> _subscriptions;
    private readonly ILogger<PollAutoTriggerHandler> _logger;

    public PollAutoTriggerHandler(
        PollProfilesManager profilesManager,
        IPollController controller,
        PollSnapshotStore store,
        SettingsManager settingsManager,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<PollAutoTriggerHandler> logger)
    {
        _profilesManager = profilesManager;
        _controller = controller;
        _store = store;
        _settingsManager = settingsManager;
        _timeProvider = timeProvider;
        _logger = logger;
        _subscriptions =
        [
            eventBus.Subscribe<StreamWentOnline>(this),
            eventBus.Subscribe<StreamWentOffline>(this),
            eventBus.Subscribe<BroadcastProfileApplied>(this),
        ];
    }

    public async Task HandleAsync(StreamWentOnline @event, CancellationToken cancellationToken)
    {
        await TryTriggerAsync(PollAutoTriggerEvent.StreamOnline, null, cancellationToken);
    }

    public Task HandleAsync(StreamWentOffline @event, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _lastAutoStartedAt.Clear();
        }

        return Task.CompletedTask;
    }

    public async Task HandleAsync(BroadcastProfileApplied @event, CancellationToken cancellationToken)
    {
        await TryTriggerAsync(PollAutoTriggerEvent.BroadcastProfileApplied, @event.Profile.Id, cancellationToken);
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private static bool MatchesBroadcastProfile(PollProfile profile, Guid? broadcastProfileId)
    {
        if (profile.AutoTrigger.Event != PollAutoTriggerEvent.BroadcastProfileApplied)
        {
            return true;
        }

        if (profile.AutoTrigger.BroadcastProfileId is null)
        {
            return true;
        }

        return broadcastProfileId is not null && profile.AutoTrigger.BroadcastProfileId == broadcastProfileId;
    }

    private async Task TryTriggerAsync(PollAutoTriggerEvent triggerEvent, Guid? broadcastProfileId, CancellationToken cancellationToken)
    {
        if (IsKillSwitchActive())
        {
            _logger.LogDebug("PollAutoTriggerHandler: kill-switch активен, триггеры отключены");
            return;
        }

        if (_store.Current is { Status: PollSnapshotStatus.Active })
        {
            _logger.LogDebug("PollAutoTriggerHandler: уже идёт голосование, пропускаем");
            return;
        }

        var matching = _profilesManager.GetAll()
            .Where(p => p.AutoTrigger.Event == triggerEvent && MatchesBroadcastProfile(p, broadcastProfileId))
            .ToArray();

        foreach (var profile in matching)
        {
            if (!PassesCooldown(profile))
            {
                continue;
            }

            if (_store.Current is { Status: PollSnapshotStatus.Active })
            {
                return;
            }

            var snapshot = await _controller.StartAsync(profile, cancellationToken);

            if (snapshot is null)
            {
                continue;
            }

            MarkStarted(profile);
            return;
        }
    }

    private bool IsKillSwitchActive()
    {
        var date = _settingsManager.Current.Twitch.Polls.AutoTriggerKillSwitchDateUtc;
        return date is not null && date.Value.Date == _timeProvider.GetUtcNow().UtcDateTime.Date;
    }

    private bool PassesCooldown(PollProfile profile)
    {
        lock (_sync)
        {
            if (profile.AutoTrigger.CooldownMinutes <= 0)
            {
                return true;
            }

            if (!_lastAutoStartedAt.TryGetValue(profile.Id, out var lastUtc))
            {
                return true;
            }

            return (_timeProvider.GetUtcNow().UtcDateTime - lastUtc).TotalMinutes >= profile.AutoTrigger.CooldownMinutes;
        }
    }

    private void MarkStarted(PollProfile profile)
    {
        lock (_sync)
        {
            _lastAutoStartedAt[profile.Id] = _timeProvider.GetUtcNow().UtcDateTime;
        }
    }
}
