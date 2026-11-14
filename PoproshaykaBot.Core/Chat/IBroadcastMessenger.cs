using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Chat;

public readonly record struct BroadcastSendResult(
    IReadOnlyDictionary<StreamingPlatform, ChatEnqueueResult> PerPlatform)
{
    public bool AnyQueued => PerPlatform.Values.Any(x => x == ChatEnqueueResult.Queued);

    public ChatEnqueueResult FirstRefusalOr(ChatEnqueueResult fallback = ChatEnqueueResult.NotConnected)
    {
        foreach (var result in PerPlatform.Values)
        {
            if (result != ChatEnqueueResult.Queued)
            {
                return result;
            }
        }

        return PerPlatform.Count > 0 ? ChatEnqueueResult.Queued : fallback;
    }
}

public interface IBroadcastMessenger
{
    ChatEnqueueResult Send(StreamingPlatform platform, string text);

    BroadcastSendResult Send(string text, IReadOnlySet<StreamingPlatform> platforms);

    BroadcastSendResult SendToConnected(string text);

    BroadcastSendResult Send(
        Func<IChatPlatform, string?> messageFactory,
        IReadOnlySet<StreamingPlatform>? platforms = null);
}
