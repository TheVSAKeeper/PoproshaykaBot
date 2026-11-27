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
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.YouTube;

public sealed class YouTubeChatPlatform : IChatPlatform
{
    private readonly IStreamStatus _streamStatus;
    private readonly YouTubeSessionContext _sessionContext;
    private readonly IYouTubeActiveBroadcastSource _activeBroadcastSource;
    private readonly IPlatformMessenger _messenger;
    private readonly YouTubeSettingsStore _settingsStore;
    private readonly YouTubeAccountsStore _accountsStore;
    private readonly ChannelCatalogStore? _channelCatalogStore;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly ILogger<YouTubeChatPlatform>? _logger;
    private readonly IYouTubeChatDiagnostics? _chatDiagnostics;

    internal YouTubeChatPlatform(
        [FromKeyedServices(StreamingPlatform.YouTube)] IStreamStatus streamStatus,
        YouTubeSessionContext sessionContext,
        IYouTubeActiveBroadcastSource activeBroadcastSource,
        [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformMessenger messenger,
        YouTubeSettingsStore settingsStore,
        YouTubeAccountsStore accountsStore,
        ITargetChannelProvider targetChannelProvider,
        ILogger<YouTubeChatPlatform>? logger = null,
        IYouTubeChatDiagnostics? chatDiagnostics = null,
        ChannelCatalogStore? channelCatalogStore = null)
    {
        _streamStatus = streamStatus;
        _sessionContext = sessionContext;
        _activeBroadcastSource = activeBroadcastSource;
        _messenger = messenger;
        _settingsStore = settingsStore;
        _accountsStore = accountsStore;
        _channelCatalogStore = channelCatalogStore;
        _targetChannelProvider = targetChannelProvider;
        _logger = logger;
        _chatDiagnostics = chatDiagnostics;
    }

    public StreamingPlatform Id => StreamingPlatform.YouTube;

    public string DisplayName => "YouTube";

    public PlatformCapabilities Capabilities => PlatformCapabilities.ServerChatHistory;

    public bool IsEnabled => _settingsStore.Load().Enabled && !_targetChannelProvider.Current.IsDebugSession;

    public bool IsBlockedByDebugSession => _settingsStore.Load().Enabled && _targetChannelProvider.Current.IsDebugSession;

    public bool IsConnected =>
        _sessionContext.Current is not null
        && !string.IsNullOrWhiteSpace(_activeBroadcastSource.CurrentBroadcast?.LiveChatId);

    public bool UsesSingleMonitoringRole => true;

    public IStreamStatus StreamStatus => _streamStatus;

    public ChatConnectionStatus? CaptureChatStatus(BotLifecyclePhase phase)
    {
        var chat = _chatDiagnostics?.CaptureDiagnostics().ToChatConnectionStatus();
        if (chat is null || phase == BotLifecyclePhase.Idle)
        {
            return chat;
        }

        return ChatConnectionStatus.InactiveState(phase) is { } state
            ? chat with
            {
                State = state,
                Issue = phase == BotLifecyclePhase.Failed ? chat.Issue : ChatReadIssue.None,
            }
            : chat;
    }

    public IPlatformMessenger Messenger => _messenger;

    public IChatAccountMessenger? AccountMessenger => _messenger as IChatAccountMessenger;

    public IChatDecorations Decorations => NullChatDecorations.Instance;

    public PlatformBotIdentity? BotIdentity
    {
        get
        {
            var contextId = _sessionContext.Current?.BotContextId
                ?? _channelCatalogStore?.GetActiveChannel(Id)?.BotContextId;
            var bot = _accountsStore.Load(PlatformAuthRole.Bot, contextId);

            if (string.IsNullOrWhiteSpace(bot.ChannelId) && string.IsNullOrWhiteSpace(bot.ChannelTitle))
            {
                _logger?.LogDebug("Учётная запись бота YouTube ещё не содержит идентификатора и имени канала");
                return null;
            }

            var displayName = string.IsNullOrWhiteSpace(bot.ChannelTitle) ? bot.ChannelId : bot.ChannelTitle;
            return new(bot.ChannelId, displayName);
        }
    }

    public string? ConfiguredChannel
    {
        get
        {
            if (_channelCatalogStore?.GetActiveChannel(Id) is { } activeEntry)
            {
                if (!string.IsNullOrWhiteSpace(activeEntry.Handle))
                {
                    return activeEntry.Handle.Trim();
                }

                if (!string.IsNullOrWhiteSpace(activeEntry.DisplayName))
                {
                    return activeEntry.DisplayName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(activeEntry.ChannelId))
                {
                    return activeEntry.ChannelId.Trim();
                }
            }

            var channel = _settingsStore.Load().Channel;
            if (!string.IsNullOrWhiteSpace(channel))
            {
                return channel.Trim();
            }

            var activeChannelId = _activeBroadcastSource.CurrentBroadcast?.ChannelId;
            if (!string.IsNullOrWhiteSpace(activeChannelId))
            {
                return activeChannelId.Trim();
            }

            var broadcasterChannelId = _accountsStore.Load(PlatformAuthRole.Broadcaster).ChannelId;
            return string.IsNullOrWhiteSpace(broadcasterChannelId) ? null : broadcasterChannelId.Trim();
        }
    }

    public string? ConfiguredChannelDisplayName
    {
        get
        {
            if (_channelCatalogStore?.GetActiveChannel(Id) is { } activeEntry)
            {
                if (!string.IsNullOrWhiteSpace(activeEntry.DisplayName))
                {
                    return activeEntry.DisplayName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(activeEntry.Handle))
                {
                    return activeEntry.Handle.Trim();
                }

                if (!string.IsNullOrWhiteSpace(activeEntry.ChannelId))
                {
                    return activeEntry.ChannelId.Trim();
                }
            }

            var broadcastTitle = _activeBroadcastSource.CurrentBroadcast?.ChannelTitle;
            if (!string.IsNullOrWhiteSpace(broadcastTitle))
            {
                return broadcastTitle.Trim();
            }

            var channel = _settingsStore.Load().Channel;
            if (!string.IsNullOrWhiteSpace(channel))
            {
                return channel.Trim();
            }

            var broadcaster = _accountsStore.Load(PlatformAuthRole.Broadcaster);
            if (!string.IsNullOrWhiteSpace(broadcaster.ChannelTitle))
            {
                return broadcaster.ChannelTitle.Trim();
            }

            return ConfiguredChannel;
        }
    }

    public bool AppliesPreferredColorScheme => true;

    public string? BuildNativeChatUrl(StreamInfo stream)
    {
        return string.IsNullOrWhiteSpace(stream.Id)
            ? null
            : $"https://www.youtube.com/live_chat?v={Uri.EscapeDataString(stream.Id.Trim())}&is_popout=1";
    }

    public string? BuildNativeChatUrl(StreamInfo? stream, string? targetChannelLogin, bool isDarkTheme)
    {
        if (stream is null)
        {
            return null;
        }

        var url = BuildNativeChatUrl(stream);
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}dark_theme={(isDarkTheme ? "1" : "0")}";
    }

    public string? BuildStreamUrl(StreamInfo? stream = null)
    {
        if (!string.IsNullOrWhiteSpace(stream?.Id))
        {
            return $"https://www.youtube.com/watch?v={Uri.EscapeDataString(stream.Id.Trim())}";
        }

        if (!string.IsNullOrWhiteSpace(stream?.UserId))
        {
            return $"https://www.youtube.com/channel/{Uri.EscapeDataString(stream.UserId.Trim())}";
        }

        var configured = ConfiguredChannel;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        if (Uri.TryCreate(configured, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        if (configured.StartsWith('@'))
        {
            var handle = configured[1..].Trim();
            return string.IsNullOrWhiteSpace(handle)
                ? null
                : $"https://www.youtube.com/@{Uri.EscapeDataString(handle)}";
        }

        if (configured.StartsWith("UC", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://www.youtube.com/channel/{Uri.EscapeDataString(configured)}";
        }

        return $"https://www.youtube.com/{Uri.EscapeDataString(configured)}";
    }
}
