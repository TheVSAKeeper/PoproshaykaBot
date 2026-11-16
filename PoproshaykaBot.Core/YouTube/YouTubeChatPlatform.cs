using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
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
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly ILogger<YouTubeChatPlatform>? _logger;

    internal YouTubeChatPlatform(
        [FromKeyedServices(StreamingPlatform.YouTube)] IStreamStatus streamStatus,
        YouTubeSessionContext sessionContext,
        IYouTubeActiveBroadcastSource activeBroadcastSource,
        [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformMessenger messenger,
        YouTubeSettingsStore settingsStore,
        YouTubeAccountsStore accountsStore,
        ITargetChannelProvider targetChannelProvider,
        ILogger<YouTubeChatPlatform>? logger = null)
    {
        _streamStatus = streamStatus;
        _sessionContext = sessionContext;
        _activeBroadcastSource = activeBroadcastSource;
        _messenger = messenger;
        _settingsStore = settingsStore;
        _accountsStore = accountsStore;
        _targetChannelProvider = targetChannelProvider;
        _logger = logger;
    }

    public StreamingPlatform Id => StreamingPlatform.YouTube;

    public string DisplayName => "YouTube";

    public PlatformCapabilities Capabilities => PlatformCapabilities.ServerChatHistory;

    public bool IsEnabled => _settingsStore.Load().Enabled && !_targetChannelProvider.Current.IsDebugSession;

    public bool IsConnected =>
        _sessionContext.Current is not null
        && !string.IsNullOrWhiteSpace(_activeBroadcastSource.CurrentBroadcast?.LiveChatId);

    public IStreamStatus StreamStatus => _streamStatus;

    public IPlatformMessenger Messenger => _messenger;

    public IChatDecorations Decorations => NullChatDecorations.Instance;

    public PlatformBotIdentity? BotIdentity
    {
        get
        {
            var bot = _accountsStore.Load(PlatformAuthRole.Bot);

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
