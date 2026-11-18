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
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class ChatDisplayTileViewModel : DashboardTileViewModel, IDisposable
{
    private const string WebView2RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly ChatDisplayStore _store;
    private readonly IShellLauncher _shellLauncher;
    private readonly IDialogService _dialogService;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly List<IDisposable> _subscriptions = [];

    private readonly ToolbarItemViewModel _reloadAction;
    private readonly ToolbarItemViewModel _resetZoomAction;
    private readonly ToolbarItemViewModel _resetSessionAction;
    private readonly ToolbarItemViewModel _blockersAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTwitchSelected), nameof(ShowAccountChangeNotice))]
    private StreamingPlatform _selectedPlatform = StreamingPlatform.Twitch;

    private PlatformAuthRole _chatDisplayAccount;

    [ObservableProperty]
    private double _zoomFactor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAccountChangeNotice))]
    private bool _accountChangeRequiresRestart;

    [ObservableProperty]
    private bool _hasFallback;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FallbackActionCommand), nameof(FallbackActionText))]
    private bool _showRuntimeDownload;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FallbackActionCommand), nameof(FallbackActionText))]
    private Uri? _externalUri;

    [ObservableProperty]
    private string _fallbackHeading = string.Empty;

    [ObservableProperty]
    private string _fallbackDescription = string.Empty;

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
        IUiDispatcher? uiDispatcher = null)
        : base("twitch-chat", "Чат трансляции", minWidth: 280, minHeight: 234)
    {
        _targetChannelProvider = targetChannelProvider;
        _shellLauncher = shellLauncher;
        _dialogService = dialogService;
        _store = store;
        _platformRegistry = platformRegistry;
        _youtubeSettingsStore = youtubeSettingsStore;
        _uiDispatcher = uiDispatcher;
        Logger = logger;

        var twitch = settings.Current.Twitch;
        _chatDisplayAccount = twitch.ChatDisplayAccount;
        UserDataFolder = WebView2UserDataFolders.Resolve(twitch.ChatDisplayAccount);
        AccountLabel = ResolveAccountLabel(twitch.ChatDisplayAccount);
        Platforms = platformRegistry?.All.Select(platform => new PlatformFilterOption(platform.Id, platform.DisplayName))
            .ToArray() ?? [new(StreamingPlatform.Twitch, "Twitch")];

        _subscriptions.Add(bus.SubscribeOnUi<ChatDisplaySettingsChanged>(OnChatDisplaySettingsChanged));
        _subscriptions.Add(bus.SubscribeOnUi<ActiveChannelChanged>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<StreamWentOnline>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<StreamWentOffline>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<StreamMetadataResolved>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<YouTubeActiveBroadcastChanged>(_ => RefreshChat()));
        _subscriptions.Add(bus.SubscribeOnUi<PlatformAuthorizationRefreshed>(change =>
        {
            if (change.Platform == SelectedPlatform)
            {
                RefreshChat();
            }
        }));

        _zoomFactor = _store.LoadZoom();

        _reloadAction = new(PackIconLucideKind.RefreshCw, ReloadCommand, "Перезагрузить страницу чата");
        _resetZoomAction = new(PackIconLucideKind.Scaling, ResetZoomCommand, "Сбросить масштаб к значению по умолчанию");
        _resetSessionAction = new(PackIconLucideKind.Trash2, ResetSessionCommand, "Очистить куки и кэш активного аккаунта (бот или стример)");
        _blockersAction = new(PackIconLucideKind.Ban, EditBlockersCommand, "Блокировка баннеров: скрыть лишние элементы чата");

        HeaderActions.Add(_reloadAction);
        HeaderActions.Add(_resetZoomAction);
        HeaderActions.Add(new(PackIconLucideKind.ExternalLink, OpenInBrowserCommand, "Открыть текущую страницу в системном браузере"));
        HeaderActions.Add(_resetSessionAction);
        HeaderActions.Add(_blockersAction);
        RefreshChat(force: true);

        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed += OnYouTubeSettingsChanged;
        }
    }

    public event Action? ReloadRequested;

    public event Action? ResetSessionRequested;

    public event Action? ClutterScriptChanged;

    public event Action? ChannelUriChanged;

    public override PackIconLucideKind Icon => PackIconLucideKind.MessageSquare;

    public override bool FillsAvailableSpace => true;

    public override bool ContentFillsTile => true;

    public ILogger Logger { get; }

    public string UserDataFolder { get; }

    public IReadOnlyList<PlatformFilterOption> Platforms { get; }

    public bool HasMultiplePlatforms => Platforms.Count > 1;

    public bool IsTwitchSelected => SelectedPlatform == StreamingPlatform.Twitch;

    public bool ShowAccountChangeNotice => IsTwitchSelected && AccountChangeRequiresRestart;

    public string AccountLabel { get; private set; }

    public Uri? ChannelUri { get; private set; }

    public ICommand? FallbackActionCommand => ShowRuntimeDownload ? OpenRuntimeDownloadCommand
        : ExternalUri is not null ? OpenInBrowserCommand : null;

    public string? FallbackActionText => ShowRuntimeDownload ? "Скачать WebView2 Runtime"
        : ExternalUri is not null ? "Открыть в браузере" : null;

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

    private void ShowNoChannelFallback()
    {
        HasFallback = true;
        ShowRuntimeDownload = false;
        FallbackHeading = IsTwitchSelected ? "Канал не указан" : "Чат эфира недоступен";
        FallbackDescription = IsTwitchSelected
            ? "Укажите канал в настройках Twitch, чтобы открыть чат."
            : "Чат появится, когда у выбранного канала начнётся эфир с доступным чатом.";
    }

    public void ShowRuntimeMissingFallback()
    {
        HasFallback = true;
        ShowRuntimeDownload = true;
        FallbackHeading = "Требуется Microsoft Edge WebView2 Runtime";
        FallbackDescription = "Установите его, чтобы открыть чат в приложении.";
        DisableWebViewActions();
    }

    public void ShowGenericFallback()
    {
        HasFallback = true;
        ShowRuntimeDownload = false;
        FallbackHeading = $"Не удалось открыть чат {PlatformVisuals.GetDisplayName(SelectedPlatform)}";
        FallbackDescription = ExternalUri is not null ? "Откройте чат в браузере или попробуйте обновить его."
            : "Попробуйте обновить чат. Подробности – в журнале.";
    }

    public void ShowChatContent()
    {
        HasFallback = false;
        ShowRuntimeDownload = false;
    }

    partial void OnZoomFactorChanged(double value)
    {
        _store.SaveZoom(value);
    }

    partial void OnSelectedPlatformChanged(StreamingPlatform value)
    {
        _resetZoomAction.IsVisible = IsTwitchSelected;
        _resetSessionAction.IsVisible = IsTwitchSelected;
        _blockersAction.IsVisible = IsTwitchSelected;
        RefreshChat(force: true);
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
        if (@event.ChatDisplayAccount != _chatDisplayAccount)
        {
            _chatDisplayAccount = @event.ChatDisplayAccount;
            AccountLabel = ResolveAccountLabel(@event.ChatDisplayAccount);
            AccountChangeRequiresRestart = true;
        }

        RefreshChat();
    }

    private void RefreshChat(bool force = false)
    {
        var platform = _platformRegistry?.Find(SelectedPlatform);
        var stream = platform?.StreamStatus is { CurrentStatus: StreamStatus.Online } status ? status.CurrentStream : null;
        var uri = IsTwitchSelected ? BuildChannelUri(_targetChannelProvider.Current.Login)
            : stream is not null ? ParseWebUri(platform?.BuildNativeChatUrl(stream)) : null;
        ExternalUri = IsTwitchSelected ? uri : ParseWebUri(platform?.BuildStreamUrl(stream));

        if (!force && uri == ChannelUri)
        {
            return;
        }

        ChannelUri = uri;
        _reloadAction.IsEnabled = uri is not null;
        _resetZoomAction.IsEnabled = uri is not null;
        _resetSessionAction.IsEnabled = uri is not null;

        if (uri is null)
        {
            ShowNoChannelFallback();
        }
        else
        {
            ShowChatContent();
        }

        ChannelUriChanged?.Invoke();
    }

    private static Uri? ParseWebUri(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;

    private void DisableWebViewActions()
    {
        _reloadAction.IsEnabled = false;
        _resetZoomAction.IsEnabled = false;
        _resetSessionAction.IsEnabled = false;
    }

    [RelayCommand]
    private void Reload()
    {
        if (ChannelUri is null)
        {
            return;
        }

        HasFallback = false;
        ReloadRequested?.Invoke();
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

    [RelayCommand]
    private void OpenInBrowser()
    {
        if (ExternalUri is null)
        {
            return;
        }

        _shellLauncher.Open(ExternalUri.ToString());
    }

    [RelayCommand]
    private void OpenRuntimeDownload()
    {
        _shellLauncher.Open(WebView2RuntimeDownloadUrl);
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
