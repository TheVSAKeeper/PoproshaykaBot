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
    private readonly IPlatformWebChatLoginDialog? _webChatLogin;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly StreamingPlatform _primaryPlatform;

    private readonly ToolbarItemViewModel _reloadAction;
    private readonly ToolbarItemViewModel _resetZoomAction;
    private readonly ToolbarItemViewModel _resetSessionAction;
    private readonly ToolbarItemViewModel _blockersAction;
    private readonly ToolbarItemViewModel _openInBrowserAction;

    [ObservableProperty]
    private ChatDisplayMode _selectedMode;

    private PlatformAuthRole _chatDisplayAccount;
    private bool _isDarkTheme;

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
        IPlatformWebChatLoginDialog? webChatLogin = null,
        IEnumerable<IPlatformAccountLogin>? accountLogins = null)
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

        _subscriptions.Add(bus.SubscribeOnUi<ChatDisplaySettingsChanged>(OnChatDisplaySettingsChanged));
        _subscriptions.Add(bus.SubscribeOnUi<ActiveChannelChanged>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<StreamWentOnline>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<StreamWentOffline>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<StreamMetadataResolved>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<YouTubeActiveBroadcastChanged>(_ => RefreshChat()));
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
        if (_uiDispatcher is null)
        {
            RefreshChat();
        }
        else
        {
            _uiDispatcher.Invoke(() => RefreshChat());
        }
    }

    public string BuildHideClutterScript() => _store.BuildHideClutterScript();

    partial void OnZoomFactorChanged(double value) => _store.SaveZoom(value);

    partial void OnSelectedModeChanged(ChatDisplayMode value)
    {
        RefreshChat();
        _uiSettings?.SetValue(SettingsKeys.ChatTileDisplayMode, value.SettingsValue);
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

        var heading = isUnconfiguredPlatform
            ? $"{platform!.DisplayName} не подключён"
            : platform?.NativeChatUnavailableHeading ?? "Чат эфира недоступен";
        var description = isUnconfiguredPlatform
            ? $"Подключите {platform!.DisplayName}, чтобы видеть второй встроенный чат рядом с Twitch в одном окне."
            : platform?.NativeChatUnavailableDescription ?? "Чат появится, когда у выбранного канала начнётся эфир с доступным чатом.";
        var setupCommand = isUnconfiguredPlatform
            ? new RelayCommand(() => SetupPlatformRequested?.Invoke(this, platformId))
            : null;
        var setupActionText = isUnconfiguredPlatform ? $"Подключить {platform!.DisplayName}" : null;

        var changed = pane.SetTarget(
            platformId, chatUri, externalUri, shown, supportsClutter, appliesTheme, heading, description,
            setupCommand, setupActionText);
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
