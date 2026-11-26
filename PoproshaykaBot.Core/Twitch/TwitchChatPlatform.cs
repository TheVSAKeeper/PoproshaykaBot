using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
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
    ChatIngestionService? chatIngestion = null,
    [FromKeyedServices(StreamingPlatform.Twitch)] PlatformHost? platformHost = null,
    SettingsManager? settingsManager = null) : IChatPlatform
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

    public bool IsEnabled => targetChannelProvider?.Current.IsDebugSession == true
        || settingsManager?.Current?.Twitch?.Enabled != false;

    public bool IsConnected =>
        !string.IsNullOrWhiteSpace(channels.GetChannel(StreamingPlatform.Twitch))
        && (platformHost?.IsRunning ?? true);

    public bool? HasWhisperPermission
    {
        get
        {
            var bot = authorization is null ? accountsStore.LoadBot() : authorization.GetAccount(PlatformAuthRole.Bot);
            return bot is not null && TwitchScopes.IsGranted(bot, TwitchScopes.UserManageWhispers);
        }
    }

    public bool UsesChannelCatalog => targetChannelProvider?.Current.IsDebugSession != true;

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

    public bool RequiresOnlineStreamForNativeChat => false;

    public bool SupportsChatClutterBlocking => true;

    public string NativeChatUnavailableHeading => "Канал не указан";

    public string NativeChatUnavailableDescription => "Укажите канал в настройках Twitch, чтобы открыть чат.";

    public string? BuildNativeChatUrl(StreamInfo? stream, string? targetChannelLogin, bool isDarkTheme)
    {
        var login = !string.IsNullOrWhiteSpace(stream?.UserLogin)
            ? stream.UserLogin.Trim()
            : !string.IsNullOrWhiteSpace(targetChannelLogin)
                ? targetChannelLogin.Trim()
                : ConfiguredChannel;

        return string.IsNullOrWhiteSpace(login)
            ? null
            : $"https://www.twitch.tv/popout/{Uri.EscapeDataString(login)}/chat?popout=";
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
