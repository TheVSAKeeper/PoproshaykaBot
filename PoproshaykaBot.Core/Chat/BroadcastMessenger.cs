using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Chat;

public sealed class BroadcastMessenger(
    IChatPlatformRegistry platformRegistry,
    ILogger<BroadcastMessenger> logger) : IBroadcastMessenger
{
    public ChatEnqueueResult Send(StreamingPlatform platform, string text)
    {
        if (!platformRegistry.TryGet(platform, out var adapter) || !adapter.IsConnected)
        {
            logger.LogDebug("Рассылка на площадку {Platform} пропущена: площадка не подключена", platform);
            return ChatEnqueueResult.NotConnected;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return ChatEnqueueResult.EmptyMessage;
        }

        return adapter.Messenger.Send(text);
    }

    public BroadcastSendResult Send(string text, IReadOnlySet<StreamingPlatform> platforms)
    {
        ArgumentNullException.ThrowIfNull(platforms);
        return Send(_ => text, platforms);
    }

    public BroadcastSendResult SendToConnected(string text)
    {
        return Send(_ => text, platforms: null);
    }

    public BroadcastSendResult Send(
        Func<IChatPlatform, string?> messageFactory,
        IReadOnlySet<StreamingPlatform>? platforms = null)
    {
        ArgumentNullException.ThrowIfNull(messageFactory);

        var results = new Dictionary<StreamingPlatform, ChatEnqueueResult>();
        IReadOnlyCollection<IChatPlatform> targets;

        if (platforms is null)
        {
            targets = platformRegistry.GetConnected();
        }
        else
        {
            var resolved = new List<IChatPlatform>();

            foreach (var platformId in platforms)
            {
                if (!platformRegistry.TryGet(platformId, out var adapter) || !adapter.IsConnected)
                {
                    results[platformId] = ChatEnqueueResult.NotConnected;
                    continue;
                }

                resolved.Add(adapter);
            }

            targets = resolved;
        }

        foreach (var adapter in targets)
        {
            var text = messageFactory(adapter);

            if (string.IsNullOrWhiteSpace(text))
            {
                results[adapter.Id] = ChatEnqueueResult.EmptyMessage;
                continue;
            }

            results[adapter.Id] = adapter.Messenger.Send(text);
        }

        return new(results);
    }
}
