using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Twitch;

public sealed class TwitchChatPlatform(
    [FromKeyedServices(StreamingPlatform.Twitch)] IStreamStatus streamStatus,
    TwitchChatMessenger messenger,
    ChatDecorationsProvider decorations,
    PlatformChannelRegistry channels,
    AccountsStore accountsStore,
    ILogger<TwitchChatPlatform>? logger = null,
    ITargetChannelProvider? targetChannelProvider = null,
    TwitchChannelAuthorization? authorization = null,
    ChatIngestionService? chatIngestion = null) : IChatPlatform
{
    private const string BotFallbackDisplayName = "PoproshaykaBot";

    public StreamingPlatform Id => StreamingPlatform.Twitch;

    public string DisplayName => "Twitch";

    public PlatformCapabilities Capabilities =>
        PlatformCapabilities.Reply
        | PlatformCapabilities.Whisper
        | PlatformCapabilities.NativePolls
        | PlatformCapabilities.ChannelMetadataEdit
        | PlatformCapabilities.EmotePositions;

    public bool IsEnabled => true;

    public bool IsConnected => !string.IsNullOrWhiteSpace(channels.GetChannel(StreamingPlatform.Twitch));

    public ChatConnectionStatus? CaptureChatStatus(BotLifecyclePhase phase) =>
        chatIngestion?.CaptureChatStatus(phase, ConfiguredChannel);

    public IStreamStatus StreamStatus => streamStatus;

    public IPlatformMessenger Messenger => messenger;

    public IChatDecorations Decorations => decorations;

    public PlatformBotIdentity? BotIdentity
    {
        get
        {
            var bot = authorization is null ? accountsStore.LoadBot() : authorization.GetAccount(PlatformAuthRole.Bot);

            if (bot is null || (string.IsNullOrWhiteSpace(bot.UserId) && string.IsNullOrWhiteSpace(bot.Login)))
            {
                logger?.LogDebug("Учётная запись бота Twitch ещё не содержит идентификатора и логина");
                return null;
            }

            var displayName = string.IsNullOrWhiteSpace(bot.Login) ? BotFallbackDisplayName : bot.Login;
            return new(bot.UserId, displayName);
        }
    }

    public string? ConfiguredChannel
    {
        get
        {
            var connected = channels.GetChannel(StreamingPlatform.Twitch);
            if (!string.IsNullOrWhiteSpace(connected))
            {
                return connected.Trim();
            }

            var target = targetChannelProvider?.Current.Login;
            return string.IsNullOrWhiteSpace(target) ? null : target.Trim();
        }
    }

    public string? BuildStreamUrl(StreamInfo? stream = null)
    {
        var login = !string.IsNullOrWhiteSpace(stream?.UserLogin)
            ? stream.UserLogin.Trim()
            : ConfiguredChannel;

        return string.IsNullOrWhiteSpace(login)
            ? null
            : $"https://www.twitch.tv/{Uri.EscapeDataString(login)}";
    }
}
