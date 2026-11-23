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
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Core.Twitch.Auth;
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
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly IEmbeddedYouTubeChatLoginDialog? _youtubeChatLogin;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly List<IDisposable> _subscriptions = [];

    private readonly ToolbarItemViewModel _reloadAction;
    private readonly ToolbarItemViewModel _resetZoomAction;
    private readonly ToolbarItemViewModel _resetSessionAction;
    private readonly ToolbarItemViewModel _blockersAction;

    private readonly ToolbarItemViewModel _openInBrowserAction;

    [ObservableProperty]
    private ChatDisplayMode _selectedMode;

    private PlatformAuthRole _chatDisplayAccount;

    [ObservableProperty]
    private double _zoomFactor;

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
        IEmbeddedYouTubeChatLoginDialog? youtubeChatLogin = null,
        YouTubeAccountsStore? youtubeAccountsStore = null)
        : base("twitch-chat", "Чат трансляции", minWidth: 280, minHeight: 234)
    {
        _targetChannelProvider = targetChannelProvider;
        _uiSettings = uiSettings;
        TwitchChat = new(shellLauncher, platformRegistry, uiSettings);
        PlatformChat = new(shellLauncher, platformRegistry, uiSettings);
        TwitchChat.ChatAccountChanged += () => OpenRequested?.Invoke(TwitchChat, false);
        PlatformChat.ChatAccountChanged += () => OpenRequested?.Invoke(PlatformChat, false);
        _dialogService = dialogService;
        _store = store;
        _platformRegistry = platformRegistry;
        _accountDirectories = (accountDirectories ?? []).ToDictionary(static directory => directory.Platform);
        _youtubeSettingsStore = youtubeSettingsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _youtubeChatLogin = youtubeChatLogin;
        _uiDispatcher = uiDispatcher;
        Logger = logger;

        if (_youtubeChatLogin is not null && _youtubeAccountsStore is not null)
        {
            PlatformChat.SetBrowserSessionHandlers(
                SignInYouTubeBrowserAsync,
                (accountId, accountLabel) => ResetPlatformSessionRequested?.Invoke(accountId, accountLabel));
        }

        var twitch = settings.Current.Twitch;
        _chatDisplayAccount = twitch.ChatDisplayAccount;
        var modes = new List<ChatDisplayMode>();
        if (platformRegistry is not null)
        {
            modes.AddRange(platformRegistry.All.Select(platform =>
                new ChatDisplayMode(platform.Id, ChatDisplayLayout.Single, platform.DisplayName)));
        }

        if (modes.Count == 0)
        {
            modes.Add(new(StreamingPlatform.Twitch, ChatDisplayLayout.Single, "Twitch"));
        }

        if (modes.Any(mode => mode.Platform == StreamingPlatform.Twitch))
        {
            foreach (var other in modes.Where(mode => mode.Platform != StreamingPlatform.Twitch).ToArray())
            {
                modes.Add(new(other.Platform, ChatDisplayLayout.Columns, $"Twitch и {other.Label} в две колонки"));
                modes.Add(new(other.Platform, ChatDisplayLayout.Rows, $"Twitch и {other.Label} в две строки"));
            }
        }

        Modes = modes;
        var savedMode = _uiSettings?.GetStringValue(SettingsKeys.ChatTileDisplayMode);
        _selectedMode = modes.Find(mode => mode.SettingsValue == savedMode) ?? modes[0];

        _subscriptions.Add(bus.SubscribeOnUi<ChatDisplaySettingsChanged>(OnChatDisplaySettingsChanged));
        _subscriptions.Add(bus.SubscribeOnUi<ActiveChannelChanged>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<StreamWentOnline>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<StreamWentOffline>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<StreamMetadataResolved>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<YouTubeActiveBroadcastChanged>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<PlatformAuthorizationRefreshed>(change =>
        {
            if (change.Platform == SelectedMode.Platform || (SelectedMode.IsSplit && change.Platform == StreamingPlatform.Twitch))
            {
                RefreshChat();
            }
        }));

        _zoomFactor = _store.LoadZoom();

        _reloadAction = new(PackIconLucideKind.RefreshCw, ReloadCommand, "Перезагрузить страницу чата");
        _resetZoomAction = new(PackIconLucideKind.Scaling, ResetZoomCommand, "Сбросить масштаб к значению по умолчанию");
        _resetSessionAction = new(PackIconLucideKind.Trash2, ResetSessionCommand, "Очистить куки и кэш активного аккаунта (бот или стример)");
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
    }

    public event Action<ChatDisplayPaneViewModel, bool>? OpenRequested;

    public event Action? ResetSessionRequested;

    public event Action<string, string>? ResetPlatformSessionRequested;

    public event Action? ClutterScriptChanged;

    public override PackIconLucideKind Icon => PackIconLucideKind.MessageSquare;

    public override bool FillsAvailableSpace => true;

    public override bool ContentFillsTile => true;

    public ILogger Logger { get; }

    public string UserDataFolder => WebView2UserDataFolders.Resolve(_chatDisplayAccount);

    public IReadOnlyList<ChatDisplayMode> Modes { get; }

    public ChatDisplayPaneViewModel TwitchChat { get; }

    public ChatDisplayPaneViewModel PlatformChat { get; }

    public bool HasMultiplePlatforms => Modes.Count > 1;

    public void Dispose()
    {
        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed -= OnYouTubeSettingsChanged;
        }

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private void OnYouTubeSettingsChanged()
    {
        if (_uiDispatcher is null)
        {
            RefreshChat();
        }
        else
        {
            _uiDispatcher.Invoke(() => RefreshChat());
        }
    }

    public string BuildHideClutterScript()
    {
        return _store.BuildHideClutterScript();
    }

    partial void OnZoomFactorChanged(double value)
    {
        _store.SaveZoom(value);
    }

    partial void OnSelectedModeChanged(ChatDisplayMode value)
    {
        RefreshChat();
        _uiSettings?.SetValue(SettingsKeys.ChatTileDisplayMode, value.SettingsValue);
    }

    private static Uri? BuildChannelUri(string? channel)
    {
        var trimmed = channel?.Trim();

        return string.IsNullOrEmpty(trimmed)
            ? null
            : new Uri($"https://www.twitch.tv/popout/{Uri.EscapeDataString(trimmed)}/chat?popout=");
    }

    private static string ResolveAccountLabel(PlatformAuthRole role)
    {
        return role == PlatformAuthRole.Broadcaster ? "стримера" : "бота";
    }

    private void OnChatDisplaySettingsChanged(ChatDisplaySettingsChanged @event)
    {
        var legacyProfileChanged = @event.ChatDisplayAccount != _chatDisplayAccount;
        _chatDisplayAccount = @event.ChatDisplayAccount;
        RefreshChat();
        if (legacyProfileChanged && TwitchChat.IsShown && TwitchChat.SelectedAccount?.BrowserProfile is null)
        {
            OpenRequested?.Invoke(TwitchChat, true);
        }
    }

    private void RefreshChat()
    {
        var mode = SelectedMode;
        var twitchShown = mode.Platform == StreamingPlatform.Twitch || mode.IsSplit;
        var twitchUri = BuildChannelUri(_targetChannelProvider.Current.Login);
        var twitchChanged = TwitchChat.SetTarget(StreamingPlatform.Twitch, twitchUri, twitchUri, twitchShown);
        if (twitchShown)
        {
            TwitchChat.SetAccounts(_accountDirectories.GetValueOrDefault(TwitchChat.Platform), null, $"Сеанс {ResolveAccountLabel(_chatDisplayAccount)}");
        }

        var platformShown = mode.Platform != StreamingPlatform.Twitch;
        var platform = platformShown ? _platformRegistry?.Find(mode.Platform) : null;
        var stream = platform?.StreamStatus is { CurrentStatus: StreamStatus.Online } status ? status.CurrentStream : null;
        var uri = stream is not null ? ParseWebUri(platform?.BuildNativeChatUrl(stream)) : null;
        var platformChanged = PlatformChat.SetTarget(mode.Platform, uri, ParseWebUri(platform?.BuildStreamUrl(stream)), platformShown);
        if (platformShown)
        {
            PlatformChat.SetAccounts(_accountDirectories.GetValueOrDefault(PlatformChat.Platform), stream?.Id, "Просмотр чата");
        }

        _resetZoomAction.IsVisible = twitchShown || platformShown;
        _resetSessionAction.IsVisible = twitchShown;
        _blockersAction.IsVisible = twitchShown;
        _openInBrowserAction.IsVisible = !mode.IsSplit;
        UpdateWebViewActions();
        if (twitchChanged)
        {
            OpenRequested?.Invoke(TwitchChat, false);
        }

        if (platformChanged)
        {
            OpenRequested?.Invoke(PlatformChat, false);
        }
    }

    internal void UpdateWebViewActions()
    {
        var twitchReady = TwitchChat is { IsShown: true, RuntimeMissing: false, ChannelUri: not null };
        var platformReady = PlatformChat is { IsShown: true, RuntimeMissing: false, ChannelUri: not null };
        _reloadAction.IsEnabled = twitchReady || platformReady;
        _resetZoomAction.IsEnabled = twitchReady || platformReady;
        _resetSessionAction.IsEnabled = twitchReady;
    }

    private static Uri? ParseWebUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;

    [RelayCommand]
    private void SelectMode(ChatDisplayMode mode) => SelectedMode = mode;

    [RelayCommand]
    private void Reload()
    {
        if (TwitchChat is { IsShown: true, ChannelUri: not null })
        {
            OpenRequested?.Invoke(TwitchChat, true);
        }

        if (PlatformChat is { IsShown: true, ChannelUri: not null })
        {
            OpenRequested?.Invoke(PlatformChat, true);
        }
    }

    [RelayCommand]
    private void ResetZoom()
    {
        ZoomFactor = ChatDisplayStore.DefaultZoom;
    }

    [RelayCommand]
    private void ResetSession()
    {
        ResetSessionRequested?.Invoke();
    }

    private async Task SignInYouTubeBrowserAsync(string accountId, string accountLabel)
    {
        if (_youtubeChatLogin is null || _youtubeAccountsStore is null)
        {
            return;
        }

        var result = await _youtubeChatLogin.SignInAsync(new(accountId, accountLabel));
        if (result.Outcome != EmbeddedYouTubeChatLoginOutcome.Completed
            || string.IsNullOrWhiteSpace(result.BrowserProfileName))
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                PlatformChat.MessageStatus = result.Message;
            }

            return;
        }

        var written = _youtubeAccountsStore.SetBrowserProfile(accountId, result.BrowserProfileName);
        if (!written)
        {
            Logger.LogWarning("Не удалось сохранить веб-профиль чата YouTube для {AccountId} на диск", accountId);
            _dialogService.Error(
                "Ошибка сохранения",
                "Вход выполнен, но не удалось записать веб-профиль YouTube на диск. После перезапуска привязка может сброситься. Подробности – в логах.");
            PlatformChat.MessageStatus = "Не удалось сохранить веб-профиль YouTube на диск";
        }

        RefreshChat();
        OpenRequested?.Invoke(PlatformChat, true);
    }

    internal void CompletePlatformSessionReset(string accountId)
    {
        if (_youtubeAccountsStore is not null && !string.IsNullOrWhiteSpace(accountId))
        {
            var written = _youtubeAccountsStore.SetBrowserProfile(accountId, null);
            if (!written)
            {
                Logger.LogWarning("Не удалось сохранить сброс веб-профиля чата YouTube для {AccountId} на диск", accountId);
                _dialogService.Error(
                    "Ошибка сохранения",
                    "Куки очищены, но не удалось сохранить сброс привязки профиля YouTube на диск. После перезапуска профиль может вернуться. Подробности – в логах.");
                PlatformChat.MessageStatus = "Не удалось сохранить сброс веб-профиля YouTube на диск";
            }
        }

        RefreshChat();
        OpenRequested?.Invoke(PlatformChat, true);
    }

    [RelayCommand]
    private void OpenInBrowser()
    {
        var pane = TwitchChat.IsShown ? TwitchChat : PlatformChat;
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
