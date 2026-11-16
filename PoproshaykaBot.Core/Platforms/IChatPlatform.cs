using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Platforms;

public interface IChatPlatform
{
    StreamingPlatform Id { get; }

    string DisplayName { get; }

    PlatformCapabilities Capabilities { get; }

    bool IsEnabled { get; }

    bool IsConnected { get; }

    IStreamStatus StreamStatus { get; }

    IPlatformMessenger Messenger { get; }

    IChatDecorations Decorations => NullChatDecorations.Instance;

    PlatformBotIdentity? BotIdentity => null;

    string? ConfiguredChannel => null;

    string? BuildStreamUrl(StreamInfo? stream = null) => null;
}
