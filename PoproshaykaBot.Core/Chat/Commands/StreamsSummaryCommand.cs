using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Chat.Commands;

public sealed class StreamsSummaryCommand(IChatPlatformRegistry platformRegistry, TimeProvider timeProvider) : IChatCommand
{
    public string Canonical => "стримы";
    public IReadOnlyCollection<string> Aliases => ["multistream", "площадки"];
    public string Description => "сводка по всем включённым площадкам";

    public bool CanExecute(CommandContext context)
    {
        return true;
    }

    public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        var platforms = platformRegistry.GetEnabled();

        if (platforms.Count == 0)
        {
            if (platformRegistry.TryGet(context.Platform, out var callerPlatform))
            {
                platforms = [callerPlatform];
            }
            else
            {
                return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply("Нет включённых площадок", context.MessageId));
            }
        }

        var now = timeProvider.GetUtcNow();
        var parts = new List<string>(platforms.Count + 1);
        var freshSum = 0;
        var freshOnlineCount = 0;
        var anyOnline = false;
        var anyUnknown = false;
        var isTotalIncomplete = false;

        foreach (var platform in platforms)
        {
            var snapshot = platform.StreamStatus.GetSnapshot();

            switch (snapshot.Status)
            {
                case StreamStatus.Offline:
                    parts.Add($"{platform.DisplayName}: офлайн");
                    break;

                case StreamStatus.Unknown:
                    anyUnknown = true;
                    parts.Add($"{platform.DisplayName}: статус неизвестен");
                    break;

                case StreamStatus.Online:
                    anyOnline = true;
                    var sampleState = snapshot.EvaluateViewerSample(now, StreamStatusSnapshot.DefaultViewerFreshnessWindow);

                    switch (sampleState)
                    {
                        case ViewerSampleState.Fresh:
                            var stream = snapshot.Stream!;
                            var duration = now.UtcDateTime - stream.StartedAt;

                            if (duration < TimeSpan.Zero)
                            {
                                duration = TimeSpan.Zero;
                            }

                            var title = string.IsNullOrWhiteSpace(stream.Title) ? "Без названия" : stream.Title;
                            freshSum += stream.ViewerCount;
                            freshOnlineCount++;
                            parts.Add($"{platform.DisplayName}: онлайн ({title} – {stream.ViewerCount} зрит., {(int)duration.TotalHours:0}ч {duration.Minutes:00}м)");
                            break;

                        case ViewerSampleState.Stale:
                            isTotalIncomplete = true;
                            parts.Add($"{platform.DisplayName}: онлайн (зрители ~{snapshot.Stream!.ViewerCount}, замер устарел)");
                            break;

                        default:
                            isTotalIncomplete = true;
                            parts.Add($"{platform.DisplayName}: онлайн (замер зрителей недоступен)");
                            break;
                    }

                    break;
            }
        }

        if (anyOnline && anyUnknown)
        {
            isTotalIncomplete = true;
        }

        if (!anyOnline)
        {
            parts.Add(anyUnknown
                ? "Всего по площадкам: статус не подтверждён"
                : "Всего по площадкам: офлайн");
        }
        else if (freshOnlineCount == 0)
        {
            parts.Add("Всего по площадкам: н/д (неполные данные)");
        }
        else if (isTotalIncomplete)
        {
            parts.Add($"Всего по площадкам: от {freshSum} (неполные данные)");
        }
        else
        {
            parts.Add($"Всего по площадкам: {freshSum}");
        }

        var text = string.Join(" | ", parts);
        return Task.FromResult<OutgoingMessage?>(OutgoingMessage.Reply(text, context.MessageId));
    }
}
