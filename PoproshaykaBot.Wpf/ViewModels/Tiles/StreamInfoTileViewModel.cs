using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class StreamInfoTileViewModel : DashboardTileViewModel, IDisposable
{
    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly TilePlatformSelection _platformSelection;
    private readonly ILogger<StreamInfoTileViewModel> _logger;
    private readonly List<IDisposable> _subs = [];
    private readonly IUiDispatcher _uiDispatcher;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly IUiTimer _refreshTimer;
    private readonly IShellLauncher _shellLauncher;
    private readonly ToolbarItemViewModel _switchPlatformToolbarItem;
    private ToolbarItemViewModel _openChannelToolbarItem;
    private bool _refreshTimerRunning;
    private bool _hasMultipleYouTubeBroadcasts;
    private StreamInfo? _currentStreamInfo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleBroadcastsNotice), nameof(SelectedPlatformName))]
    private StreamingPlatform _selectedPlatform = StreamingPlatform.Twitch;

    [ObservableProperty]
    private bool _hasMultiplePlatforms;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleBroadcastsNotice))]
    private StreamStatus _status = StreamStatus.Unknown;

    [ObservableProperty]
    private string _streamTitle = string.Empty;

    [ObservableProperty]
    private string _detailLabel = "Игра";

    [ObservableProperty]
    private string _detailValue = "–";

    [ObservableProperty]
    private int _viewerCount;

    [ObservableProperty]
    private TimeSpan _uptime = TimeSpan.Zero;

    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private DateTime _lastUpdated = DateTime.Now;

    public StreamInfoTileViewModel(
        IChatPlatformRegistry platformRegistry,
        IEventBus bus,
        ILogger<StreamInfoTileViewModel> logger,
        IUiDispatcher uiDispatcher,
        IShellLauncher shellLauncher,
        YouTubeSettingsStore? youtubeSettingsStore = null)
        : base("stream-info", "Информация о стриме", maxWidth: 420, maxHeight: 280, minHeight: 194)
    {
        ArgumentNullException.ThrowIfNull(platformRegistry);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(shellLauncher);

        _platformRegistry = platformRegistry;
        _platformSelection = new TilePlatformSelection(platformRegistry);
        _logger = logger;
        _uiDispatcher = uiDispatcher;
        _youtubeSettingsStore = youtubeSettingsStore;
        _shellLauncher = shellLauncher;

        _refreshTimer = uiDispatcher.CreateAsyncTimer(
            TimeSpan.FromSeconds(30),
            OnRefreshTimerTickAsync,
            exception => _logger.StreamInfoAutoUpdateFailed(exception));

        _switchPlatformToolbarItem = new ToolbarItemViewModel(
            PackIconLucideKind.ArrowLeftRight,
            SwitchPlatformCommand,
            toolTip: "Переключить площадку")
        {
            IsVisible = _platformSelection.HasMultiplePlatforms,
        };

        _openChannelToolbarItem = new ToolbarItemViewModel(
            PackIconLucideKind.ExternalLink,
            OpenChannelCommand,
            toolTip: "Открыть канал")
        {
            IsVisible = false,
        };

        HeaderActions.Add(new ToolbarItemViewModel(
            PackIconLucideKind.RefreshCw,
            RefreshCommand,
            toolTip: "Обновить данные стрима"));
        HeaderActions.Add(_switchPlatformToolbarItem);
        HeaderActions.Add(_openChannelToolbarItem);

        _subs.Add(bus.SubscribeOnUi<StreamWentOnline>(_ => OnPlatformStateChanged()));
        _subs.Add(bus.SubscribeOnUi<StreamWentOffline>(_ => OnPlatformStateChanged()));
        _subs.Add(bus.SubscribeOnUi<StreamMetadataResolved>(_ => OnPlatformStateChanged()));
        _subs.Add(bus.SubscribeOnUi<ChannelUpdated>(_ => OnPlatformStateChanged()));
        _subs.Add(bus.SubscribeOnUi<PlatformConnectionChanged>(_ => OnPlatformStateChanged()));
        _subs.Add(bus.SubscribeOnUi<YouTubeActiveBroadcastChanged>(OnYouTubeActiveBroadcastChanged));
        _subs.Add(bus.SubscribeOnUi<YouTubeMultipleBroadcastsDetected>(OnYouTubeMultipleBroadcastsDetected));

        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed += OnYouTubeSettingsChanged;
        }

        ApplyCurrentStatus();
    }

    public override PackIconLucideKind Icon => PackIconLucideKind.Radio;

    public override bool SizesToContent => true;

    public string SelectedPlatformName => PlatformVisuals.GetDisplayName(SelectedPlatform);

    public bool HasMultipleBroadcastsNotice =>
        SelectedPlatform == StreamingPlatform.YouTube
        && _hasMultipleYouTubeBroadcasts;

    [RelayCommand]
    private void SwitchPlatform()
    {
        if (_platformSelection.Cycle())
        {
            ApplyCurrentStatus();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        RefreshCommand.NotifyCanExecuteChanged();
        try
        {
            _platformSelection.Refresh();
            if (_platformSelection.GetSelectedPlatform() is { } platform)
            {
                await platform.StreamStatus.RefreshLiveSnapshotAsync();
            }

            ApplyCurrentStatus();
        }
        catch (Exception ex)
        {
            _logger.StreamInfoUpdateFailed(ex);
        }
        finally
        {
            IsRefreshing = false;
            RefreshCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRefresh() => !IsRefreshing;

    [RelayCommand]
    private void OpenChannel()
    {
        var url = _platformRegistry.BuildStreamUrl(SelectedPlatform, _currentStreamInfo);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        _shellLauncher.Open(url);
    }

    private void OnPlatformStateChanged()
    {
        _platformSelection.Refresh();
        ApplyCurrentStatus();
    }

    private void OnYouTubeSettingsChanged()
    {
        _uiDispatcher.Invoke(OnPlatformStateChanged);
    }

    private void OnYouTubeActiveBroadcastChanged(YouTubeActiveBroadcastChanged activeChanged)
    {
        if (activeChanged.ActiveBroadcast is not null)
        {
            _hasMultipleYouTubeBroadcasts = false;
            OnPropertyChanged(nameof(HasMultipleBroadcastsNotice));
        }

        OnPlatformStateChanged();
    }

    private void OnYouTubeMultipleBroadcastsDetected(YouTubeMultipleBroadcastsDetected detectedEvent)
    {
        _hasMultipleYouTubeBroadcasts = detectedEvent.Candidates.Count > 1;
        OnPropertyChanged(nameof(HasMultipleBroadcastsNotice));
    }

    private void ApplyCurrentStatus()
    {
        SelectedPlatform = _platformSelection.SelectedPlatform;
        HasMultiplePlatforms = _platformSelection.HasMultiplePlatforms;
        _switchPlatformToolbarItem.IsVisible = HasMultiplePlatforms;
        UpdateOpenChannelToolTip(PlatformVisuals.Get(SelectedPlatform).OpenStreamTooltip);

        var platform = _platformSelection.GetSelectedPlatform();

        if (platform is null || !platform.IsEnabled)
        {
            _hasMultipleYouTubeBroadcasts = false;
        }

        if (platform is null)
        {
            Status = StreamStatus.Unknown;
            StreamTitle = "Статус не определён";
            DetailLabel = "Игра";
            DetailValue = "–";
            ViewerCount = 0;
            Uptime = TimeSpan.Zero;
            _currentStreamInfo = null;
            _openChannelToolbarItem.IsVisible = false;
            OnPropertyChanged(nameof(HasMultipleBroadcastsNotice));
            UpdateTimer();
            return;
        }

        var info = platform.StreamStatus.CurrentStream;
        _currentStreamInfo = info;
        Status = platform.StreamStatus.CurrentStatus;
        LastUpdated = DateTime.Now;

        var supportsGameCategory = platform.Capabilities.HasFlag(PlatformCapabilities.ChannelMetadataEdit);
        DetailLabel = supportsGameCategory ? "Игра" : "Трансляция";

        if (Status == StreamStatus.Online && info is { IsBare: false })
        {
            StreamTitle = info.Title;
            DetailValue = supportsGameCategory
                ? (string.IsNullOrWhiteSpace(info.GameName) ? "–" : info.GameName)
                : "Активна";
            ViewerCount = info.ViewerCount;

            var elapsed = DateTime.UtcNow - info.StartedAt;
            Uptime = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;

            var url = _platformRegistry.BuildStreamUrl(SelectedPlatform, info);
            _openChannelToolbarItem.IsVisible = !string.IsNullOrWhiteSpace(url);
        }
        else
        {
            StreamTitle = Status switch
            {
                StreamStatus.Offline => "Стрим завершён",
                StreamStatus.Online => "Загрузка данных...",
                _ => "Статус не определён",
            };
            DetailValue = supportsGameCategory ? "–" : "Не запущена";
            ViewerCount = 0;
            Uptime = TimeSpan.Zero;
            _openChannelToolbarItem.IsVisible = false;

            if (Status == StreamStatus.Offline && SelectedPlatform == StreamingPlatform.YouTube)
            {
                _hasMultipleYouTubeBroadcasts = false;
            }
        }

        OnPropertyChanged(nameof(HasMultipleBroadcastsNotice));
        UpdateTimer();
    }

    private void UpdateOpenChannelToolTip(string expectedToolTip)
    {
        if (string.Equals(_openChannelToolbarItem.ToolTip, expectedToolTip, StringComparison.Ordinal))
        {
            return;
        }

        var updated = new ToolbarItemViewModel(
            PackIconLucideKind.ExternalLink,
            OpenChannelCommand,
            toolTip: expectedToolTip)
        {
            IsVisible = _openChannelToolbarItem.IsVisible,
        };

        var index = HeaderActions.IndexOf(_openChannelToolbarItem);
        if (index >= 0)
        {
            HeaderActions[index] = updated;
        }

        _openChannelToolbarItem = updated;
    }

    private void UpdateTimer()
    {
        if (Status == StreamStatus.Online)
        {
            if (!_refreshTimerRunning)
            {
                _refreshTimer.Start();
                _refreshTimerRunning = true;
            }
        }
        else if (_refreshTimerRunning)
        {
            _refreshTimer.Stop();
            _refreshTimerRunning = false;
        }
    }

    private async Task OnRefreshTimerTickAsync()
    {
        var platform = _platformSelection.GetSelectedPlatform();
        if (platform is null || platform.StreamStatus.CurrentStatus != StreamStatus.Online)
        {
            return;
        }

        await platform.StreamStatus.RefreshLiveSnapshotAsync();
        ApplyCurrentStatus();
    }

    public void Dispose()
    {
        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed -= OnYouTubeSettingsChanged;
        }

        _refreshTimer.Stop();
        _refreshTimerRunning = false;

        foreach (var sub in _subs)
        {
            sub.Dispose();
        }
        _subs.Clear();
    }
}
