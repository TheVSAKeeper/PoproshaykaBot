using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class StreamInfoCommand(IChatPlatformRegistry platformRegistry, TimeProvider timeProvider) : IChatCommand
{
    public string Canonical => "стрим";
    public IReadOnlyCollection<string> Aliases => ["stream", "онлайн", "он"];
    public string Description => "информация о текущем стриме";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        if (!platformRegistry.TryGet(context.Platform, out var platform))
        {
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply("Статус стрима пока неизвестен", context.MessageId));
        }

        var snapshot = platform.StreamStatus.GetSnapshot();

        if (snapshot.Status == StreamStatus.Unknown)
        {
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply("Статус стрима пока неизвестен", context.MessageId));
        }

        if (snapshot.Status != StreamStatus.Online)
        {
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply("Сейчас стрим офлайн", context.MessageId));
        }

        var info = snapshot.Stream;

        if (info == null || (info.IsBare && snapshot.ViewerSampledAtUtc == null))
        {
            return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply("Стрим онлайн, детали загружаются", context.MessageId));
        }

        var now = timeProvider.GetUtcNow();
        var duration = now.UtcDateTime - info.StartedAt;

        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        var hours = (int)duration.TotalHours;
        var minutes = duration.Minutes;

        var title = string.IsNullOrWhiteSpace(info.Title) ? "Без названия" : info.Title;
        var game = string.IsNullOrWhiteSpace(info.GameName) ? "Без категории" : info.GameName;

        var viewerState = snapshot.EvaluateViewerSample(now, StreamStatusSnapshot.DefaultViewerFreshnessWindow);
        var viewers = viewerState switch
        {
            ViewerSampleState.Fresh => info.ViewerCount.ToString(),
            ViewerSampleState.Stale => $"~{info.ViewerCount} (устарел)",
            _ => "н/д",
        };

        var textFull = $"🔴 Стрим: {title} | {game} | 👥 {viewers} | ⏱ {hours:0}ч {minutes:00}м";
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(textFull, context.MessageId));
    }
}
