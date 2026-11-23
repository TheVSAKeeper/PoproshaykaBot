using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
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

    IChatAccountMessenger? AccountMessenger => null;

    IChatDecorations Decorations => NullChatDecorations.Instance;

    PlatformBotIdentity? BotIdentity => null;

    string? ConfiguredChannel => null;

    bool UsesChannelCatalog => true;

    ChatConnectionStatus? CaptureChatStatus(BotLifecyclePhase phase) => null;

    string? BuildStreamUrl(StreamInfo? stream = null) => null;

    string? BuildNativeChatUrl(StreamInfo stream) => null;
}
