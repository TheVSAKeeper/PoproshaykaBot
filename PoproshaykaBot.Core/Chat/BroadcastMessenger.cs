using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Chat;

public sealed class BroadcastMessenger(
    IChatPlatformRegistry platformRegistry,
    ILogger<BroadcastMessenger> logger) : IBroadcastMessenger
{
    public ChatEnqueueResult Send(
        StreamingPlatform platform,
        string text,
        ChatSendCategory category = ChatSendCategory.None)
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

        return adapter.Messenger.Send(text, category: category);
    }

    public BroadcastSendResult Send(
        string text,
        IReadOnlySet<StreamingPlatform> platforms,
        ChatSendCategory category = ChatSendCategory.None)
    {
        ArgumentNullException.ThrowIfNull(platforms);
        return Send(_ => text, platforms, category);
    }

    public BroadcastSendResult SendToConnected(
        string text,
        ChatSendCategory category = ChatSendCategory.None)
    {
        return Send(_ => text, platforms: null, category);
    }

    public BroadcastSendResult Send(
        Func<IChatPlatform, string?> messageFactory,
        IReadOnlySet<StreamingPlatform>? platforms = null,
        ChatSendCategory category = ChatSendCategory.None)
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

            results[adapter.Id] = adapter.Messenger.Send(text, category: category);
        }

        return new(results);
    }
}
