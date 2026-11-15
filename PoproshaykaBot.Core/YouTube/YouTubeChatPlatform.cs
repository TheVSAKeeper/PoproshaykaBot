using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Chat;

namespace PoproshaykaBot.Core.YouTube;

public sealed class YouTubeChatPlatform : IChatPlatform
{
    private readonly IStreamStatus _streamStatus;
    private readonly YouTubeChatIngestionService _ingestionService;
    private readonly IPlatformMessenger _messenger;
    private readonly YouTubeSettingsStore _settingsStore;
    private readonly YouTubeAccountsStore _accountsStore;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly ILogger<YouTubeChatPlatform>? _logger;

    internal YouTubeChatPlatform(
        [FromKeyedServices(StreamingPlatform.YouTube)] IStreamStatus streamStatus,
        YouTubeChatIngestionService ingestionService,
        [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformMessenger messenger,
        YouTubeSettingsStore settingsStore,
        YouTubeAccountsStore accountsStore,
        ITargetChannelProvider targetChannelProvider,
        ILogger<YouTubeChatPlatform>? logger = null)
    {
        _streamStatus = streamStatus;
        _ingestionService = ingestionService;
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

    public bool IsConnected => _ingestionService.IsConnected;

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
}
