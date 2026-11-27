using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat.Display;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class ChatDisplayTileViewModel : DashboardTileViewModel, IDisposable
{
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly ChatDisplayStore _store;
    private readonly ISettingsStore? _uiSettings;
    private readonly IDialogService _dialogService;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly IReadOnlyDictionary<StreamingPlatform, IPlatformAccountDirectory> _accountDirectories;
    private readonly IReadOnlyDictionary<StreamingPlatform, IPlatformAccountLogin> _accountLogins;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly ChannelCatalogStore? _channelCatalogStore;
    private readonly IPlatformWebChatLoginDialog? _webChatLogin;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly HashSet<string> _youtubeLiveSeenKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _youtubeEndedKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly StreamingPlatform _primaryPlatform;

    private readonly ToolbarItemViewModel _reloadAction;
    private readonly ToolbarItemViewModel _resetZoomAction;
    private readonly ToolbarItemViewModel _resetSessionAction;
    private readonly ToolbarItemViewModel _blockersAction;
    private readonly ToolbarItemViewModel _openInBrowserAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsYouTubeCollapsed),
        nameof(YouTubeCollapseButtonText),
        nameof(YouTubeCollapseIcon),
        nameof(YouTubeCollapseToolTip),
        nameof(SplitLayoutButtonText),
        nameof(SplitLayoutToolTip),
        nameof(SplitLayoutIcon))]
    private ChatDisplayMode _selectedMode;

    private PlatformAuthRole _chatDisplayAccount;
    private ChatDisplayLayout _lastSplitLayout = ChatDisplayLayout.Columns;
    private string? _lastYouTubeChannelKey;
    private bool _isDarkTheme;
    private bool _isGalleryPreview;

    [ObservableProperty]
    private double _zoomFactor;

    [ObservableProperty]
    private bool _isNarrowHeader;

    public ChatDisplayTileViewModel(
        SettingsManager settings,
        ITargetChannelProvider targetChannelProvider,
        ILogger<ChatDisplayTileViewModel> logger,
        IShellLauncher shellLauncher,
        IDialogService dialogService,
        IEventBus bus,
        ChatDisplayStore store,
        IChatPlatformRegistry? platformRegistry = null,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        IUiDispatcher? uiDispatcher = null,
        ISettingsStore? uiSettings = null,
        IEnumerable<IPlatformAccountDirectory>? accountDirectories = null,
        IPlatformWebChatLoginDialog? webChatLogin = null,
        IEnumerable<IPlatformAccountLogin>? accountLogins = null,
        ChannelCatalogStore? channelCatalogStore = null)
        : base("twitch-chat", "Чат трансляции", minWidth: 280, minHeight: 234)
    {
        _targetChannelProvider = targetChannelProvider;
        _uiSettings = uiSettings;
        PrimaryChat = new(shellLauncher, platformRegistry, uiSettings);
        SecondaryChat = new(shellLauncher, platformRegistry, uiSettings);
        PrimaryChat.ChatAccountChanged += () => OpenRequested?.Invoke(PrimaryChat, false);
        SecondaryChat.ChatAccountChanged += () => OpenRequested?.Invoke(SecondaryChat, false);
        _dialogService = dialogService;
        _store = store;
        _platformRegistry = platformRegistry;
        _accountDirectories = (accountDirectories ?? []).ToDictionary(static directory => directory.Platform);
        _accountLogins = (accountLogins ?? []).ToDictionary(static login => login.Platform);
        _youtubeSettingsStore = youtubeSettingsStore;
        _channelCatalogStore = channelCatalogStore;
        _webChatLogin = webChatLogin;
        _uiDispatcher = uiDispatcher;
        _isDarkTheme = ThemeManager.Current?.IsDark == true;
        Logger = logger;

        var twitch = settings.Current.Twitch;
        _chatDisplayAccount = twitch.ChatDisplayAccount;
        var modes = new List<ChatDisplayMode>();
        if (platformRegistry is not null)
        {
            modes.AddRange(platformRegistry.All.Select(platform =>
                new ChatDisplayMode(platform.Id, ChatDisplayLayout.Single, platform.DisplayName, platform.Id)));
        }

        if (modes.Count == 0)
        {
            modes.Add(new(StreamingPlatform.Twitch, ChatDisplayLayout.Single, "Twitch", StreamingPlatform.Twitch));
        }

        _primaryPlatform = modes[0].Platform;
        var singleModes = modes.ToArray();
        for (var i = 0; i < singleModes.Length; i++)
        {
            for (var j = i + 1; j < singleModes.Length; j++)
            {
                var first = singleModes[i];
                var second = singleModes[j];
                modes.Add(new(second.Platform, ChatDisplayLayout.Columns, $"{first.Label} и {second.Label} в две колонки", first.Platform));
                modes.Add(new(second.Platform, ChatDisplayLayout.Rows, $"{first.Label} и {second.Label} в две строки", first.Platform));
            }
        }

        Modes = modes;
        var savedMode = _uiSettings?.GetStringValue(SettingsKeys.ChatTileDisplayMode);
        _selectedMode = modes.Find(mode => mode.SettingsValue == savedMode)
            ?? modes.Find(static mode => mode.Layout == ChatDisplayLayout.Columns)
            ?? modes[0];
        if (_selectedMode.IsSplit)
        {
            _lastSplitLayout = _selectedMode.Layout;
        }

        _subscriptions.Add(bus.SubscribeOnUi<ChatDisplaySettingsChanged>(OnChatDisplaySettingsChanged));
        _subscriptions.Add(bus.SubscribeOnUi<ActiveChannelChanged>(OnActiveChannelChanged));
        _subscriptions.Add(bus.SubscribeOnUi<StreamWentOnline>(OnStreamWentOnline));
        _subscriptions.Add(bus.SubscribeOnUi<StreamWentOffline>(OnStreamWentOffline));
        _subscriptions.Add(bus.SubscribeOnUi<StreamMetadataResolved>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<YouTubeActiveBroadcastChanged>(OnYouTubeActiveBroadcastChanged));
        _subscriptions.Add(bus.SubscribeOnUi<PlatformAuthorizationRefreshed>(change =>
        {
            if (change.Platform == SelectedMode.Platform
                || (SelectedMode.IsSplit && change.Platform == SelectedMode.EffectivePrimaryPlatform))
            {
                RefreshChat();
            }
        }));

        _zoomFactor = _store.LoadZoom();

        _reloadAction = new(PackIconLucideKind.RefreshCw, ReloadCommand, "Перезагрузить страницу чата");
        _resetZoomAction = new(PackIconLucideKind.Scaling, ResetZoomCommand, "Сбросить масштаб к значению по умолчанию");
        _resetSessionAction = new(PackIconLucideKind.Trash2, ResetSessionCommand, "Очистить куки и кэш сеанса просмотра");
        _blockersAction = new(PackIconLucideKind.Ban, EditBlockersCommand, "Блокировка баннеров: скрыть лишние элементы чата");
        _openInBrowserAction = new(PackIconLucideKind.ExternalLink, OpenInBrowserCommand, "Открыть текущую страницу в системном браузере");

        HeaderActions.Add(_reloadAction);
        HeaderActions.Add(_resetZoomAction);
        HeaderActions.Add(_openInBrowserAction);
        HeaderActions.Add(_resetSessionAction);
        HeaderActions.Add(_blockersAction);
        RefreshChat();

        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed += OnYouTubeSettingsChanged;
        }

        if (_channelCatalogStore is not null)
        {
            _channelCatalogStore.Changed += OnChannelCatalogChanged;
        }
    }

    public event Action<ChatDisplayPaneViewModel, bool>? OpenRequested;

    public event Action? ResetSessionRequested;

    public event Action<ChatDisplayPaneViewModel, string, string>? ResetPaneSessionRequested;

    public event Action? ClutterScriptChanged;

    public event EventHandler<StreamingPlatform>? SetupPlatformRequested;

    public override PackIconLucideKind Icon => PackIconLucideKind.MessageSquare;

    public override bool FillsAvailableSpace => true;

    public override bool ContentFillsTile => true;

    public ILogger Logger { get; }

    public IReadOnlyList<ChatDisplayMode> Modes { get; }

    public ChatDisplayPaneViewModel PrimaryChat { get; }

    public ChatDisplayPaneViewModel SecondaryChat { get; }

    public bool HasMultiplePlatforms => Modes.Count > 1;

    public bool IsYouTubeCollapsed => SelectedMode.Platform == _primaryPlatform && !SelectedMode.IsSplit;

    public string YouTubeCollapseButtonText => IsYouTubeCollapsed ? "Показать YouTube" : "Свернуть YouTube";

    public PackIconLucideKind YouTubeCollapseIcon => IsYouTubeCollapsed
        ? PackIconLucideKind.Eye
        : PackIconLucideKind.EyeOff;

    public string YouTubeCollapseToolTip => IsYouTubeCollapsed
        ? "Вернуть два чата (Twitch и YouTube)"
        : "Свернуть YouTube и раскрыть Twitch на всю ширину";

    public string SplitLayoutButtonText => SelectedMode.Layout == ChatDisplayLayout.Columns ? "Друг под другом" : "Рядом";

    public string SplitLayoutToolTip => SelectedMode.Layout == ChatDisplayLayout.Columns
        ? "Показать чаты друг под другом (две строки)"
        : "Показать чаты рядом (две колонки)";

    public PackIconLucideKind SplitLayoutIcon => SelectedMode.Layout == ChatDisplayLayout.Columns
        ? PackIconLucideKind.Rows2
        : PackIconLucideKind.Columns2;

    public string GetDefaultUserDataFolder(ChatDisplayPaneViewModel pane) =>
        pane.Platform == StreamingPlatform.Twitch
            ? WebView2UserDataFolders.Resolve(_chatDisplayAccount)
            : AppPaths.Combine("WebView2", $"chat-{pane.Platform.ToString().ToLowerInvariant()}");

    public void Dispose()
    {
        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed -= OnYouTubeSettingsChanged;
        }

        if (_channelCatalogStore is not null)
        {
            _channelCatalogStore.Changed -= OnChannelCatalogChanged;
        }

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    internal void SetDarkTheme(bool isDarkTheme)
    {
        if (_isDarkTheme == isDarkTheme)
        {
            return;
        }

        _isDarkTheme = isDarkTheme;
        RefreshChat();
        if (PrimaryChat is { IsShown: true, AppliesPreferredColorScheme: true })
        {
            OpenRequested?.Invoke(PrimaryChat, true);
        }

        if (SecondaryChat is { IsShown: true, AppliesPreferredColorScheme: true })
        {
            OpenRequested?.Invoke(SecondaryChat, true);
        }
    }

    private void OnYouTubeSettingsChanged()
    {
        InvokeOnUi(RefreshChat);
    }

    private void OnChannelCatalogChanged(object? sender, EventArgs e)
    {
        InvokeOnUi(RefreshChat);
    }

    private void InvokeOnUi(Action action)
    {
        if (_uiDispatcher is null)
        {
            action();
        }
        else
        {
            _uiDispatcher.Invoke(action);
        }
    }

    public string BuildHideClutterScript() => _store.BuildHideClutterScript();

    partial void OnZoomFactorChanged(double value) => _store.SaveZoom(value);

    partial void OnSelectedModeChanged(ChatDisplayMode value)
    {
        if (value.IsSplit)
        {
            _lastSplitLayout = value.Layout;
        }

        RefreshChat();
        _uiSettings?.SetValue(SettingsKeys.ChatTileDisplayMode, value.SettingsValue);
    }

    private void OnActiveChannelChanged(ActiveChannelChanged @event)
    {
        if (@event.Platform == StreamingPlatform.YouTube)
        {
            ClearYouTubeEndedState(@event.NewChannel.ChannelId);
            ClearYouTubeEndedState(@event.NewDisplayName);
            if (!string.IsNullOrWhiteSpace(_lastYouTubeChannelKey))
            {
                _youtubeLiveSeenKeys.Remove(_lastYouTubeChannelKey);
            }
        }

        RefreshChat();
    }

    private void OnStreamWentOnline(StreamWentOnline @event)
    {
        if (@event.Platform == StreamingPlatform.YouTube)
        {
            MarkYouTubeLive(@event.Channel, @event.ChannelId);
        }

        RefreshChat();
    }

    private void OnStreamWentOffline(StreamWentOffline @event)
    {
        if (@event.Platform == StreamingPlatform.YouTube)
        {
            MarkYouTubeEnded(@event.Channel, @event.ChannelId);
        }

        RefreshChat();
    }

    private void OnYouTubeActiveBroadcastChanged(YouTubeActiveBroadcastChanged @event)
    {
        if (@event.ActiveBroadcast is { } broadcast)
        {
            MarkYouTubeLive(broadcast.ChannelTitle, broadcast.ChannelId);
        }
        else
        {
            var currentKey = ResolveYouTubeChannelKey(_platformRegistry?.Find(StreamingPlatform.YouTube));
            if (!string.IsNullOrWhiteSpace(currentKey) && _youtubeLiveSeenKeys.Contains(currentKey))
            {
                MarkYouTubeEnded(currentKey, null);
            }
        }

        RefreshChat();
    }

    private void MarkYouTubeLive(string? channel, string? channelId)
    {
        foreach (var key in EnumerateYouTubeKeys(channel, channelId))
        {
            _youtubeLiveSeenKeys.Add(key);
            _youtubeEndedKeys.Remove(key);
        }
    }

    private void MarkYouTubeEnded(string? channel, string? channelId)
    {
        foreach (var key in EnumerateYouTubeKeys(channel, channelId))
        {
            _youtubeLiveSeenKeys.Remove(key);
            _youtubeEndedKeys.Add(key);
        }
    }

    private void ClearYouTubeEndedState(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            _youtubeEndedKeys.Remove(key.Trim());
        }
    }

    private IEnumerable<string> EnumerateYouTubeKeys(string? channel, string? channelId)
    {
        if (!string.IsNullOrWhiteSpace(channel))
        {
            yield return channel.Trim();
        }

        if (!string.IsNullOrWhiteSpace(channelId))
        {
            yield return channelId.Trim();
        }

        var currentKey = ResolveYouTubeChannelKey(_platformRegistry?.Find(StreamingPlatform.YouTube));
        if (!string.IsNullOrWhiteSpace(currentKey))
        {
            yield return currentKey;
        }
    }

    private static string ResolveYouTubeChannelKey(IChatPlatform? platform)
    {
        if (platform is null)
        {
            return string.Empty;
        }

        return !string.IsNullOrWhiteSpace(platform.ConfiguredChannel)
            ? platform.ConfiguredChannel.Trim()
            : platform.ConfiguredChannelDisplayName?.Trim() ?? string.Empty;
    }

    private static string? ResolveChannelDisplayName(IChatPlatform? platform, StreamInfo? stream)
    {
        if (platform is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(platform.ConfiguredChannelDisplayName))
        {
            return platform.ConfiguredChannelDisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(platform.ConfiguredChannel))
        {
            return platform.ConfiguredChannel.Trim();
        }

        return string.IsNullOrWhiteSpace(stream?.UserName) ? null : stream.UserName.Trim();
    }

    private static string ResolveAccountLabel(PlatformAuthRole role) =>
        role == PlatformAuthRole.Broadcaster ? "стримера" : "бота";

    private void OnChatDisplaySettingsChanged(ChatDisplaySettingsChanged @event)
    {
        var legacyProfileChanged = @event.ChatDisplayAccount != _chatDisplayAccount;
        _chatDisplayAccount = @event.ChatDisplayAccount;
        RefreshChat();
        if (legacyProfileChanged && PrimaryChat.IsShown && PrimaryChat.SelectedAccount?.BrowserProfile is null)
        {
            OpenRequested?.Invoke(PrimaryChat, true);
        }
    }

    private void RefreshChat()
    {
        if (_isGalleryPreview)
        {
            return;
        }
        var mode = SelectedMode;
        var primaryShown = mode.Platform == _primaryPlatform || mode.IsSplit;
        var primaryPlatform = mode.IsSplit ? mode.EffectivePrimaryPlatform : _primaryPlatform;
        var primaryChanged = RefreshPane(PrimaryChat, primaryPlatform, primaryShown);

        var secondaryShown = mode.Platform != _primaryPlatform || mode.IsSplit;
        var secondaryChanged = RefreshPane(SecondaryChat, mode.Platform, secondaryShown);

        var primaryObj = primaryShown ? _platformRegistry?.Find(primaryPlatform) : null;
        var secondaryObj = secondaryShown ? _platformRegistry?.Find(mode.Platform) : null;
        var supportsClutter = (primaryShown && (primaryObj?.SupportsChatClutterBlocking ?? false))
            || (secondaryShown && (secondaryObj?.SupportsChatClutterBlocking ?? false));

        _resetZoomAction.IsVisible = primaryShown || secondaryShown;
        _resetSessionAction.IsVisible = primaryShown || secondaryShown;
        _blockersAction.IsVisible = supportsClutter;
        _openInBrowserAction.IsVisible = !mode.IsSplit;
        UpdateWebViewActions();

        if (primaryChanged)
        {
            OpenRequested?.Invoke(PrimaryChat, false);
        }

        if (secondaryChanged)
        {
            OpenRequested?.Invoke(SecondaryChat, false);
        }
    }

    private bool RefreshPane(ChatDisplayPaneViewModel pane, StreamingPlatform platformId, bool shown)
    {
        var platform = shown ? _platformRegistry?.Find(platformId) : null;
        var stream = platform?.StreamStatus is { CurrentStatus: StreamStatus.Online } status ? status.CurrentStream : null;
        Uri? chatUri = null;
        if (shown && platform is not null)
        {
            var url = platform.RequiresOnlineStreamForNativeChat && stream is null
                ? null
                : platform.BuildNativeChatUrl(stream, _targetChannelProvider.Current.Login, _isDarkTheme);
            chatUri = ParseWebUri(url);
        }

        var externalUri = ParseWebUri(platform?.BuildStreamUrl(stream)) ?? (platform is null ? chatUri : null);
        var supportsClutter = platform?.SupportsChatClutterBlocking ?? false;
        var appliesTheme = platform?.AppliesPreferredColorScheme ?? false;
        var isUnconfiguredPlatform = platform is not null
            && platform.RequiresOnlineStreamForNativeChat
            && (!platform.IsEnabled || (string.IsNullOrWhiteSpace(platform.ConfiguredChannel) && externalUri is null));

        string? channelDisplayName = null;
        var fallbackTone = ChatFallbackTone.Neutral;
        var fallbackIcon = PackIconLucideKind.MessageSquareWarning;
        string heading;
        string description;

        if (platformId == StreamingPlatform.YouTube && platform is not null)
        {
            channelDisplayName = isUnconfiguredPlatform ? null : ResolveChannelDisplayName(platform, stream);
            var channelKey = ResolveYouTubeChannelKey(platform);
            if (!string.Equals(_lastYouTubeChannelKey, channelKey, StringComparison.OrdinalIgnoreCase))
            {
                if (stream is null && !string.IsNullOrWhiteSpace(channelKey))
                {
                    _youtubeEndedKeys.Remove(channelKey);
                }

                _lastYouTubeChannelKey = channelKey;
            }

            if (stream is not null && !string.IsNullOrWhiteSpace(channelKey))
            {
                _youtubeLiveSeenKeys.Add(channelKey);
                _youtubeEndedKeys.Remove(channelKey);
            }
            else if (stream is null && !string.IsNullOrWhiteSpace(channelKey) && _youtubeLiveSeenKeys.Contains(channelKey))
            {
                _youtubeLiveSeenKeys.Remove(channelKey);
                _youtubeEndedKeys.Add(channelKey);
            }

            if (isUnconfiguredPlatform)
            {
                fallbackTone = ChatFallbackTone.Neutral;
                fallbackIcon = PackIconLucideKind.Tv;
                heading = $"{platform.DisplayName} не подключён";
                description = $"Подключите {platform.DisplayName}, чтобы видеть второй встроенный чат рядом с Twitch в одном окне.";
            }
            else if (!string.IsNullOrWhiteSpace(channelKey) && _youtubeEndedKeys.Contains(channelKey))
            {
                fallbackTone = ChatFallbackTone.Ended;
                fallbackIcon = PackIconLucideKind.Radio;
                heading = "Эфир завершён / не в сети";
                description = !string.IsNullOrWhiteSpace(channelDisplayName)
                    ? $"Эфир на канале «{channelDisplayName}» завершён. Переключите канал YouTube перед следующим сегментом или запустите новый эфир в OBS."
                    : "Эфир на текущем канале завершён. Переключите канал YouTube перед следующим сегментом или запустите новый эфир в OBS.";
            }
            else
            {
                fallbackTone = ChatFallbackTone.Waiting;
                fallbackIcon = PackIconLucideKind.Clock;
                heading = !string.IsNullOrWhiteSpace(channelDisplayName)
                    ? $"Ожидание эфира на {channelDisplayName}"
                    : "Ожидание эфира на YouTube";
                description = "Чат подключится автоматически, когда вы запустите эфир в OBS.";
            }
        }
        else
        {
            heading = isUnconfiguredPlatform
                ? $"{platform!.DisplayName} не подключён"
                : platform?.NativeChatUnavailableHeading ?? "Чат эфира недоступен";
            description = isUnconfiguredPlatform
                ? $"Подключите {platform!.DisplayName}, чтобы видеть второй встроенный чат рядом с Twitch в одном окне."
                : platform?.NativeChatUnavailableDescription ?? "Чат появится, когда у выбранного канала начнётся эфир с доступным чатом.";
        }

        var setupCommand = isUnconfiguredPlatform
            ? new RelayCommand(() => SetupPlatformRequested?.Invoke(this, platformId))
            : null;
        var setupActionText = isUnconfiguredPlatform ? $"Подключить {platform!.DisplayName}" : null;

        var changed = pane.SetTarget(
            platformId, chatUri, externalUri, shown, supportsClutter, appliesTheme, heading, description,
            setupCommand, setupActionText, channelDisplayName, fallbackTone, fallbackIcon);
        if (shown)
        {
            var directory = _accountDirectories.GetValueOrDefault(platformId);
            var canSignIn = CanSignInWebChat(platformId, directory);
            pane.SetBrowserSessionHandlers(
                canSignIn,
                (accountId, accountLabel) => SignInBrowserAsync(pane, accountId, accountLabel),
                (accountId, accountLabel) => ResetPaneSessionRequested?.Invoke(pane, accountId, accountLabel));

            var defaultLabel = platform?.RequiresOnlineStreamForNativeChat == false
                ? $"Сеанс {ResolveAccountLabel(_chatDisplayAccount)}"
                : "Просмотр чата";
            pane.SetAccounts(directory, defaultLabel);
        }

        return changed;
    }

    private bool CanSignInWebChat(StreamingPlatform platform, IPlatformAccountDirectory? directory)
    {
        if (directory is not { SupportsChatBrowserProfile: true })
        {
            return false;
        }

        if (_accountLogins.TryGetValue(platform, out var login))
        {
            return login.SupportsWebChatSignIn;
        }

        return _webChatLogin is not null && PlatformWebChatDescriptors.ForPlatform(platform) is not null;
    }

    internal void UpdateWebViewActions()
    {
        var primaryReady = PrimaryChat is { IsShown: true, RuntimeMissing: false, ChannelUri: not null };
        var secondaryReady = SecondaryChat is { IsShown: true, RuntimeMissing: false, ChannelUri: not null };
        _reloadAction.IsEnabled = primaryReady || secondaryReady;
        _resetZoomAction.IsEnabled = primaryReady || secondaryReady;
        _resetSessionAction.IsEnabled = primaryReady || secondaryReady;
    }

    private static Uri? ParseWebUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;

    [RelayCommand]
    private void SelectMode(ChatDisplayMode mode) => SelectedMode = mode;

    [RelayCommand]
    private void ToggleYouTubeCollapsed()
    {
        var target = IsYouTubeCollapsed
            ? Modes.FirstOrDefault(mode => mode.Platform != _primaryPlatform && mode.Layout == _lastSplitLayout)
                ?? Modes.FirstOrDefault(static mode => mode.IsSplit)
            : Modes.FirstOrDefault(mode => mode.Platform == _primaryPlatform && mode.Layout == ChatDisplayLayout.Single);

        if (target is not null)
        {
            SelectedMode = target;
        }
    }

    [RelayCommand]
    private void ToggleSplitLayout()
    {
        var targetLayout = SelectedMode.Layout == ChatDisplayLayout.Columns
            ? ChatDisplayLayout.Rows
            : ChatDisplayLayout.Columns;

        var target = Modes.FirstOrDefault(mode => mode.Platform != _primaryPlatform && mode.Layout == targetLayout)
            ?? Modes.FirstOrDefault(mode => mode.Layout == targetLayout);

        if (target is not null)
        {
            SelectedMode = target;
        }
    }

    internal ChatDisplayMode BeginGalleryPreview(ChatDisplayLayout layout, string? youtubeChannelName, ChatFallbackTone fallbackTone)
    {
        var previousMode = SelectedMode;
        _isGalleryPreview = true;
        try
        {
            var mode = layout == ChatDisplayLayout.Single
                ? Modes.FirstOrDefault(m => m.Platform == _primaryPlatform && m.Layout == ChatDisplayLayout.Single)
                : Modes.FirstOrDefault(m => m.Platform != _primaryPlatform && m.Layout == layout);

            if (mode is not null)
            {
                SelectedMode = mode;
            }

            if (layout != ChatDisplayLayout.Single)
            {
                var channelLabel = string.IsNullOrWhiteSpace(youtubeChannelName) ? "Образовательный канал" : youtubeChannelName.Trim();
                var external = new Uri("https://www.youtube.com/@channel");
                var (heading, description, icon) = fallbackTone == ChatFallbackTone.Ended
                    ? ("Эфир завершён / не в сети",
                        $"Эфир на канале «{channelLabel}» завершён. Переключите канал YouTube перед следующим сегментом или запустите новый эфир в OBS.",
                        PackIconLucideKind.Radio)
                    : ($"Ожидание эфира на {channelLabel}",
                        "Чат подключится автоматически, когда вы запустите эфир в OBS.",
                        PackIconLucideKind.Clock);

                SecondaryChat.SetTarget(
                    StreamingPlatform.YouTube,
                    null,
                    external,
                    shown: true,
                    supportsClutterBlocking: false,
                    appliesPreferredColorScheme: true,
                    unavailableHeading: heading,
                    unavailableDescription: description,
                    channelName: channelLabel,
                    fallbackTone: fallbackTone,
                    fallbackIcon: icon);
            }
        }
        catch
        {
            _isGalleryPreview = false;
            throw;
        }

        return previousMode;
    }

    internal void EndGalleryPreview(ChatDisplayMode previousMode)
    {
        _isGalleryPreview = false;
        SelectedMode = previousMode;
        RefreshChat();
    }

    [RelayCommand]
    private void Reload()
    {
        if (PrimaryChat is { IsShown: true, ChannelUri: not null })
        {
            OpenRequested?.Invoke(PrimaryChat, true);
        }

        if (SecondaryChat is { IsShown: true, ChannelUri: not null })
        {
            OpenRequested?.Invoke(SecondaryChat, true);
        }
    }

    [RelayCommand]
    private void ResetZoom() => ZoomFactor = ChatDisplayStore.DefaultZoom;

    [RelayCommand]
    private void ResetSession() => ResetSessionRequested?.Invoke();

    private async Task SignInBrowserAsync(ChatDisplayPaneViewModel pane, string accountId, string accountLabel)
    {
        var platform = pane.Platform;
        if (_accountLogins.TryGetValue(platform, out var login) && login.SupportsWebChatSignIn)
        {
            var loginResult = await login.SignInWebChatAsync(accountId, accountLabel);
            if (loginResult.Outcome != PlatformWebChatLoginOutcome.Completed
                || string.IsNullOrWhiteSpace(loginResult.BrowserProfileName))
            {
                if (!string.IsNullOrWhiteSpace(loginResult.Message))
                {
                    pane.StatusNotice = loginResult.Message;
                }

                return;
            }

            if (!loginResult.Persisted)
            {
                Logger.LogWarning("Не удалось сохранить веб-профиль чата {Platform} для {AccountId} на диск", pane.DisplayName, accountId);
                _dialogService.Error(
                    "Ошибка сохранения",
                    $"Вход выполнен, но не удалось записать веб-профиль {pane.DisplayName} на диск. После перезапуска привязка может сброситься. Подробности – в логах.");
                pane.StatusNotice = $"Не удалось сохранить веб-профиль {pane.DisplayName} на диск";
            }

            RefreshChat();
            OpenRequested?.Invoke(pane, true);
            return;
        }

        if (_webChatLogin is null
            || PlatformWebChatDescriptors.ForPlatform(platform) is not { } descriptor
            || !_accountDirectories.TryGetValue(platform, out var directory)
            || !directory.SupportsChatBrowserProfile)
        {
            return;
        }

        var result = await _webChatLogin.SignInAsync(new(descriptor, accountId, accountLabel));
        if (result.Outcome != PlatformWebChatLoginOutcome.Completed
            || string.IsNullOrWhiteSpace(result.BrowserProfileName))
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                pane.StatusNotice = result.Message;
            }

            return;
        }

        bool written;
        try
        {
            written = directory.SetChatBrowserProfile(accountId, result.BrowserProfileName);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Ошибка сохранения веб-профиля чата {Platform} для {AccountId}", pane.DisplayName, accountId);
            written = false;
        }

        if (!written)
        {
            Logger.LogWarning("Не удалось сохранить веб-профиль чата {Platform} для {AccountId} на диск", pane.DisplayName, accountId);
            _dialogService.Error(
                "Ошибка сохранения",
                $"Вход выполнен, но не удалось записать веб-профиль {pane.DisplayName} на диск. После перезапуска привязка может сброситься. Подробности – в логах.");
            pane.StatusNotice = $"Не удалось сохранить веб-профиль {pane.DisplayName} на диск";
        }

        RefreshChat();
        OpenRequested?.Invoke(pane, true);
    }

    internal void CompletePaneSessionReset(ChatDisplayPaneViewModel pane, string accountId) =>
        CompletePaneSessionReset(pane, pane.Platform, accountId);

    internal void CompletePaneSessionReset(ChatDisplayPaneViewModel pane, StreamingPlatform targetPlatform, string accountId)
    {
        if (_accountDirectories.TryGetValue(targetPlatform, out var directory)
            && directory.SupportsChatBrowserProfile
            && !string.IsNullOrWhiteSpace(accountId))
        {
            bool written;
            try
            {
                written = directory.SetChatBrowserProfile(accountId, null);
            }
            catch (Exception exception)
            {
                Logger.LogWarning(exception, "Ошибка сброса веб-профиля чата {Platform} для {AccountId}", targetPlatform, accountId);
                written = false;
            }

            if (!written)
            {
                Logger.LogWarning("Не удалось сохранить сброс веб-профиля чата {Platform} для {AccountId} на диск", targetPlatform, accountId);
                _dialogService.Error(
                    "Ошибка сохранения",
                    $"Куки очищены, но не удалось сохранить сброс привязки профиля {pane.DisplayName} на диск. После перезапуска профиль может вернуться. Подробности – в логах.");
                pane.StatusNotice = $"Не удалось сохранить сброс веб-профиля {pane.DisplayName} на диск";
            }
        }

        RefreshChat();
        OpenRequested?.Invoke(pane, true);
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        var pane = PrimaryChat.IsShown ? PrimaryChat : SecondaryChat;
        pane.OpenInBrowserCommand.Execute(null);
    }

    [RelayCommand]
    private async Task EditBlockersAsync()
    {
        if (!_store.TryLoadBlockersText(out var selectors))
        {
            _dialogService.Error("Не удалось открыть правила",
                "Файл с правилами скрытия сейчас не читается: его держит другая программа или к нему нет доступа. Сохранённые правила не тронуты – попробуйте открыть их ещё раз чуть позже. Подробности – в логах.");

            return;
        }

        var dialog = new ChatBlockersDialogViewModel(selectors);

        if (!await _dialogService.ShowAsync(dialog))
        {
            return;
        }

        if (!_store.TrySaveBlockersText(dialog.Selectors))
        {
            _dialogService.Error("Ошибка", "Не удалось сохранить список блокираторов. Подробности – в логах.");
            return;
        }

        ClutterScriptChanged?.Invoke();
    }
}
