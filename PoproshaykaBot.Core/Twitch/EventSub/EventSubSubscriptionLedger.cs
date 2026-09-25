using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Twitch.EventSub;

public sealed class EventSubSubscriptionLedger(ITwitchHelixClient helix, ILogger logger)
{
    private readonly object _lock = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _idsByType = new(StringComparer.Ordinal);
    private long _generation;

    public long Generation
    {
        get
        {
            lock (_lock)
            {
                return _generation;
            }
        }
    }

    public async Task RecordAsync(long generation, string type, EventSubSubscribeResult result, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            if (generation == _generation)
            {
                if (result.IsSubscribed)
                {
                    _idsByType[type] = result.SubscriptionIds;
                }
                else
                {
                    _idsByType.Remove(type);
                }

                return;
            }
        }

        if (!result.IsSubscribed)
        {
            return;
        }

        logger.LogInformation("Подписка EventSub {Type} {SubscriptionIds} получена, когда подписчик уже остановлен – удаляем",
            type, result.SubscriptionIds);

        await EventSubSubscriptions.DeleteAsync(helix, type, result.SubscriptionIds, logger, cancellationToken);
    }

    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        KeyValuePair<string, IReadOnlyList<string>>[] owned;

        lock (_lock)
        {
            _generation++;
            owned = _idsByType.ToArray();
            _idsByType.Clear();
        }

        foreach (var (type, ids) in owned)
        {
            await EventSubSubscriptions.DeleteAsync(helix, type, ids, logger, cancellationToken);
        }
    }
}
