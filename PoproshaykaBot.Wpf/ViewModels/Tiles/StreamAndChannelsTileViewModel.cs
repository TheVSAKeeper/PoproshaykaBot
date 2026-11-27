using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class StreamAndChannelsTileViewModel : DashboardTileViewModel, IDisposable
{
    public const string TileTypeId = "stream-channels";

    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly ChannelCatalogStore _catalog;
    private readonly ILogger<StreamAndChannelsTileViewModel> _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IShellLauncher _shellLauncher;
    private readonly IReadOnlyList<IPlatformEnablementSource> _enablementSources;
    private readonly Dictionary<StreamingPlatform, IPlatformChannelMonitor> _monitorsByPlatform = [];
    private readonly Dictionary<ChannelKey, ChannelMonitoringSnapshot> _monitoringSnapshots = [];
    private readonly IChannelSwitchCoordinator? _switchCoordinator;
    private readonly ITargetChannelProvider? _targetChannelProvider;
    private readonly BotConnectionManager? _connectionManager;
    private readonly IUiTimer _refreshTimer;
    private readonly List<IDisposable> _subs = [];
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly ToolbarItemViewModel _openChannelToolbarItem;

    private bool _refreshTimerRunning;
    private bool _syncingYouTubeChannels;
    private string? _activeYouTubeChannelId;
    private bool _youTubeWasOnline;
    private bool _disposed;
    private StreamInfo? _primaryStreamInfo;
    private StreamingPlatform _primaryStreamPlatform = StreamingPlatform.Twitch;

    [ObservableProperty]
    private StreamStatus _status = StreamStatus.Unknown;

    [ObservableProperty]
    private string _statusBadgeText = "Ожидание эфира";

    [ObservableProperty]
    private string _streamTitle = "Ожидание эфира";

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
    private string _twitchChannelName = "–";

    [ObservableProperty]
    private string _twitchAnchorStatusText = "Ожидание эфира";

    [ObservableProperty]
    private StatusSeverity _twitchAnchorSeverity = StatusSeverity.Info;

    [ObservableProperty]
    private bool _hasTwitchAnchor = true;

    public ObservableCollection<MultistreamChannelOption> YouTubeChannels { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(YouTubeSelectorTooltip))]
    private MultistreamChannelOption? _selectedYouTubeChannel;

    [ObservableProperty]
    private string _youTubeChannelDisplayName = "Канал не настроен";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStaticYouTubeChannel), nameof(CanSwitchYouTubeChannel))]
    private bool _hasYouTubeChannelSelector;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchYouTubeChannel))]
    private bool _isSwitchingYouTubeChannel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchYouTubeChannel), nameof(YouTubeSelectorTooltip))]
    private bool _isYouTubeSwitchBlockedByDebug;

    [ObservableProperty]
    private string _youTubeStatusText = "Ожидание эфира";

    [ObservableProperty]
    private StatusSeverity _youTubeStatusSeverity = StatusSeverity.Info;

    [ObservableProperty]
    private bool _showYouTubeSetupButton;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasYouTubeSwitchError))]
    private string? _youTubeSwitchErrorText;

    [ObservableProperty]
    private string _monitoringIssueText = string.Empty;

    [ObservableProperty]
    private bool _hasMonitoringIssue;

    [ObservableProperty]
    private bool _hasMultipleYouTubeBroadcasts;

    public StreamAndChannelsTileViewModel(
        IChatPlatformRegistry platformRegistry,
        ChannelCatalogStore catalogStore,
        IEventBus bus,
        ILogger<StreamAndChannelsTileViewModel> logger,
        IUiDispatcher uiDispatcher,
        IShellLauncher shellLauncher,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        IChannelSwitchCoordinator? switchCoordinator = null,
        IEnumerable<IPlatformChannelMonitor>? channelMonitors = null,
        IEnumerable<IPlatformEnablementSource>? enablementSources = null,
        ITargetChannelProvider? targetChannelProvider = null,
        BotConnectionManager? connectionManager = null)
        : base(TileTypeId, "Эфир и каналы", maxWidth: 420, maxHeight: 340, minWidth: 220, minHeight: 248)
    {
        ArgumentNullException.ThrowIfNull(platformRegistry);
        ArgumentNullException.ThrowIfNull(catalogStore);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(shellLauncher);

        _platformRegistry = platformRegistry;
        _catalog = catalogStore;
        _logger = logger;
        _uiDispatcher = uiDispatcher;
        _shellLauncher = shellLauncher;
        _switchCoordinator = switchCoordinator;
        _targetChannelProvider = targetChannelProvider;
        _connectionManager = connectionManager;
        _enablementSources = PlatformEnablementSources.BuildList(enablementSources, youtubeSettingsStore);

        if (channelMonitors is not null)
        {
            foreach (var monitor in channelMonitors)
            {
                _monitorsByPlatform[monitor.Platform] = monitor;
            }
        }

        _refreshTimer = uiDispatcher.CreateAsyncTimer(
            TimeSpan.FromSeconds(30),
            OnRefreshTimerTickAsync,
            exception => _logger.StreamAndChannelsAutoUpdateFailed(exception));

        _openChannelToolbarItem = new ToolbarItemViewModel(
            PackIconLucideKind.ExternalLink,
            OpenChannelCommand,
            toolTip: "Открыть трансляцию")
        {
            IsVisible = false,
        };

        HeaderActions.Add(new ToolbarItemViewModel(
            PackIconLucideKind.RefreshCw,
            RefreshCommand,
            toolTip: "Обновить данные эфира"));
        HeaderActions.Add(_openChannelToolbarItem);
        HeaderActions.Add(new ToolbarItemViewModel(
            PackIconLucideKind.Settings,
            OpenChannelsAndProfilesCommand,
            toolTip: "Открыть «Каналы и профили»"));

        _subs.Add(bus.SubscribeOnUi<StreamWentOnline>(_ => ApplyCurrentState()));
        _subs.Add(bus.SubscribeOnUi<StreamWentOffline>(OnStreamWentOffline));
        _subs.Add(bus.SubscribeOnUi<StreamMetadataResolved>(_ => ApplyCurrentState()));
        _subs.Add(bus.SubscribeOnUi<ChannelUpdated>(_ => ApplyCurrentState()));
        _subs.Add(bus.SubscribeOnUi<PlatformConnectionChanged>(_ => ApplyCurrentState()));
        _subs.Add(bus.SubscribeOnUi<BotLifecyclePhaseChanged>(_ => ApplyCurrentState()));
        _subs.Add(bus.SubscribeOnUi<ChatIngestionStarted>(_ => ApplyCurrentState()));
        _subs.Add(bus.SubscribeOnUi<ChatIngestionStopped>(_ => ApplyCurrentState()));
        _subs.Add(bus.SubscribeOnUi<PlatformChatAvailabilityChanged>(_ => ApplyCurrentState()));
        _subs.Add(bus.SubscribeOnUi<YouTubeActiveBroadcastChanged>(OnYouTubeActiveBroadcastChanged));
        _subs.Add(bus.SubscribeOnUi<YouTubeMultipleBroadcastsDetected>(e => UpdateMultipleBroadcasts(e.Platform, e.Candidates.Count)));
        _subs.Add(bus.SubscribeOnUi<PlatformMultipleBroadcastsChanged>(e => UpdateMultipleBroadcasts(e.Platform, e.ActiveBroadcastCount)));
        _subs.Add(bus.SubscribeOnUi<ActiveChannelChanged>(_ => ApplyCurrentState()));
        _subs.Add(bus.SubscribeOnUi<ChannelMonitoringStateChanged>(OnChannelMonitoringStateChanged));

        foreach (var source in _enablementSources)
        {
            source.Changed += OnExternalChanged;
        }

        _catalog.Changed += OnCatalogChanged;

        ApplyCurrentState();
    }

    public event EventHandler<StreamingPlatform>? PlatformRequested;

    public override PackIconLucideKind Icon => PackIconLucideKind.Radio;

    public override bool SizesToContent => true;

    public bool ShowStaticYouTubeChannel => !HasYouTubeChannelSelector && !ShowYouTubeSetupButton;

    public bool CanSwitchYouTubeChannel => HasYouTubeChannelSelector && !IsSwitchingYouTubeChannel && !IsYouTubeSwitchBlockedByDebug;

    public bool HasYouTubeSwitchError => !string.IsNullOrWhiteSpace(YouTubeSwitchErrorText);

    public string YouTubeSelectorTooltip => IsYouTubeSwitchBlockedByDebug
        ? "Переключение канала отключено в режиме отладки"
        : SelectedYouTubeChannel?.Label ?? YouTubeChannelDisplayName;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsRefreshing = true;
        RefreshCommand.NotifyCanExecuteChanged();
        try
        {
            foreach (var platform in _platformRegistry.All)
            {
                if (platform.IsEnabled)
                {
                    await platform.StreamStatus.RefreshLiveSnapshotAsync();
                }
            }

            ApplyCurrentState();
        }
        catch (Exception ex)
        {
            _logger.StreamAndChannelsUpdateFailed(ex);
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
        var url = _platformRegistry.BuildStreamUrl(_primaryStreamPlatform, _primaryStreamInfo);
        if (!string.IsNullOrWhiteSpace(url))
        {
            _shellLauncher.Open(url);
        }
    }

    [RelayCommand]
    private void OpenChannelsAndProfiles()
    {
        PlatformRequested?.Invoke(this, StreamingPlatform.YouTube);
    }

    partial void OnSelectedYouTubeChannelChanged(MultistreamChannelOption? value)
    {
        if (_syncingYouTubeChannels || value is null)
        {
            return;
        }

        if (string.Equals(value.ChannelId, _activeYouTubeChannelId, StringComparison.Ordinal))
        {
            return;
        }

        if (!CanSwitchYouTubeChannel || _switchCoordinator is null)
        {
            RestoreSelectedYouTubeChannel();
            return;
        }

        _ = UiAsync.RunGuardedAsync(
            () => SwitchYouTubeChannelAsync(value),
            ex =>
            {
                _logger.ChannelSwitchOperationFailed(ex, "YouTube");
                _uiDispatcher.Invoke(() =>
                {
                    YouTubeSwitchErrorText = $"Не удалось переключить канал: {StreamingErrorMessages.SafeMessage(ex)}";
                    RestoreSelectedYouTubeChannel();
                });
            });
    }

    internal async Task SwitchYouTubeChannelAsync(MultistreamChannelOption target)
    {
        if (_disposed || _switchCoordinator is null || IsSwitchingYouTubeChannel)
        {
            return;
        }

        YouTubeSwitchErrorText = null;
        IsSwitchingYouTubeChannel = true;

        try
        {
            var result = await _switchCoordinator
                .SwitchActiveChannelAsync(StreamingPlatform.YouTube, new(StreamingPlatform.YouTube, target.ChannelId), _disposeCts.Token)
                .ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            if (result.Succeeded)
            {
                _logger.ChannelSwitchedByUser("YouTube", target.DisplayName);
                YouTubeSwitchErrorText = null;
            }
            else
            {
                _logger.ChannelSwitchOperationFailed(null, "YouTube");
                YouTubeSwitchErrorText = result.ErrorMessage ?? $"Не удалось переключить канал на «{target.DisplayName}».";
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            return;
        }
        finally
        {
            if (!_disposed)
            {
                IsSwitchingYouTubeChannel = false;
                ApplyCurrentState();
                if (!string.Equals(SelectedYouTubeChannel?.ChannelId, target.ChannelId, StringComparison.Ordinal))
                {
                    RestoreSelectedYouTubeChannel();
                }
            }
        }
    }

    private void OnStreamWentOffline(StreamWentOffline evt)
    {
        if (evt.Platform == StreamingPlatform.YouTube)
        {
            _youTubeWasOnline = true;
        }

        ApplyCurrentState();
    }

    private void OnYouTubeActiveBroadcastChanged(YouTubeActiveBroadcastChanged activeChanged)
    {
        if (activeChanged.ActiveBroadcast is not null)
        {
            HasMultipleYouTubeBroadcasts = false;
        }

        ApplyCurrentState();
    }

    private void UpdateMultipleBroadcasts(StreamingPlatform platform, int activeBroadcastCount)
    {
        if (platform == StreamingPlatform.YouTube)
        {
            HasMultipleYouTubeBroadcasts = activeBroadcastCount > 1;
        }
    }

    private void OnChannelMonitoringStateChanged(ChannelMonitoringStateChanged evt)
    {
        _monitoringSnapshots[evt.Channel] = evt.Snapshot;
        ApplyCurrentState();
    }

    private void OnExternalChanged()
    {
        if (!_disposed)
        {
            _uiDispatcher.Invoke(ApplyCurrentState);
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        OnExternalChanged();
    }

    private void ApplyCurrentState()
    {
        if (_disposed)
        {
            return;
        }

        var twitch = _platformRegistry.Find(StreamingPlatform.Twitch);
        var youtube = _platformRegistry.Find(StreamingPlatform.YouTube);

        UpdatePrimaryStreamSummary(twitch, youtube);
        UpdateTwitchAnchor(twitch);
        UpdateYouTubeChannelAndStatus(twitch, youtube);
        UpdateMonitoringIssues();
        UpdateTimer(twitch, youtube);
    }

    private void UpdatePrimaryStreamSummary(IChatPlatform? twitch, IChatPlatform? youtube)
    {
        var primary = SelectPrimaryPlatform(twitch, youtube);
        if (primary is null)
        {
            _primaryStreamPlatform = StreamingPlatform.Twitch;
            _primaryStreamInfo = null;
            Status = StreamStatus.Unknown;
            StatusBadgeText = "Ожидание эфира";
            StreamTitle = "Ожидание эфира";
            DetailLabel = "Игра";
            DetailValue = "–";
            ViewerCount = 0;
            Uptime = TimeSpan.Zero;
            _openChannelToolbarItem.IsVisible = false;
            return;
        }

        _primaryStreamPlatform = primary.Id;
        var info = primary.StreamStatus.CurrentStream;
        _primaryStreamInfo = info;
        var currentStatus = primary.StreamStatus.CurrentStatus;
        Status = currentStatus;

        var supportsGameCategory = primary.Capabilities.HasFlag(PlatformCapabilities.ChannelMetadataEdit);
        DetailLabel = supportsGameCategory ? "Игра" : "Трансляция";

        if (currentStatus == StreamStatus.Online && info is { IsBare: false })
        {
            StatusBadgeText = "В ЭФИРЕ";
            StreamTitle = info.Title;
            DetailValue = supportsGameCategory
                ? (string.IsNullOrWhiteSpace(info.GameName) ? "–" : info.GameName)
                : "Активна";
            ViewerCount = info.ViewerCount;

            var elapsed = DateTime.UtcNow - info.StartedAt;
            Uptime = elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;

            var url = _platformRegistry.BuildStreamUrl(primary.Id, info);
            _openChannelToolbarItem.IsVisible = !string.IsNullOrWhiteSpace(url);
        }
        else if (currentStatus == StreamStatus.Online)
        {
            StatusBadgeText = "В ЭФИРЕ";
            StreamTitle = "Загрузка данных…";
            DetailValue = "–";
            ViewerCount = 0;
            Uptime = TimeSpan.Zero;
            _openChannelToolbarItem.IsVisible = false;
        }
        else if (currentStatus == StreamStatus.Offline)
        {
            StatusBadgeText = "Эфир завершён / не в сети";
            StreamTitle = "Эфир завершён / не в сети";
            DetailValue = supportsGameCategory ? "–" : "Не запущена";
            ViewerCount = 0;
            Uptime = TimeSpan.Zero;
            _openChannelToolbarItem.IsVisible = false;
        }
        else
        {
            StatusBadgeText = "Ожидание эфира";
            StreamTitle = "Ожидание эфира";
            DetailValue = supportsGameCategory ? "–" : "Ожидание";
            ViewerCount = 0;
            Uptime = TimeSpan.Zero;
            _openChannelToolbarItem.IsVisible = false;
        }
    }

    private static IChatPlatform? SelectPrimaryPlatform(IChatPlatform? twitch, IChatPlatform? youtube)
    {
        if (twitch is { IsEnabled: true, StreamStatus.CurrentStatus: StreamStatus.Online })
        {
            return twitch;
        }

        if (youtube is { IsEnabled: true, StreamStatus.CurrentStatus: StreamStatus.Online })
        {
            return youtube;
        }

        if (twitch is { IsEnabled: true })
        {
            return twitch;
        }

        return youtube is { IsEnabled: true } ? youtube : twitch ?? youtube;
    }

    private void UpdateTwitchAnchor(IChatPlatform? twitch)
    {
        if (twitch is null)
        {
            HasTwitchAnchor = false;
            TwitchChannelName = "–";
            TwitchAnchorStatusText = "Не настроен";
            TwitchAnchorSeverity = StatusSeverity.None;
            return;
        }

        HasTwitchAnchor = true;
        var configuredChannel = _platformRegistry.GetConfiguredChannel(StreamingPlatform.Twitch);
        TwitchChannelName = string.IsNullOrWhiteSpace(configuredChannel) ? "Канал не задан" : configuredChannel;

        if (!twitch.IsEnabled)
        {
            TwitchAnchorStatusText = "Площадка выключена";
            TwitchAnchorSeverity = StatusSeverity.None;
            return;
        }

        var phase = _connectionManager?.GetPhase(StreamingPlatform.Twitch)
            ?? (twitch.IsConnected ? BotLifecyclePhase.Connected : BotLifecyclePhase.Disconnected);
        var chat = twitch.CaptureChatStatus(phase);
        var chatConnected = chat?.State == ConnectionState.Connected && chat.Issue == ChatReadIssue.None;
        var streamOnline = twitch.StreamStatus.CurrentStatus == StreamStatus.Online;

        if (streamOnline && chatConnected)
        {
            TwitchAnchorStatusText = "Якорь активен · эфир и чат";
            TwitchAnchorSeverity = StatusSeverity.Success;
        }
        else if (chatConnected)
        {
            TwitchAnchorStatusText = "Чат на якоре · ожидание эфира";
            TwitchAnchorSeverity = StatusSeverity.Success;
        }
        else if (streamOnline)
        {
            TwitchAnchorStatusText = "В эфире · чат не подключён";
            TwitchAnchorSeverity = StatusSeverity.Warning;
        }
        else if (twitch.StreamStatus.CurrentStatus == StreamStatus.Offline)
        {
            TwitchAnchorStatusText = "Эфир завершён / не в сети";
            TwitchAnchorSeverity = StatusSeverity.Info;
        }
        else
        {
            TwitchAnchorStatusText = "Ожидание эфира";
            TwitchAnchorSeverity = StatusSeverity.Info;
        }
    }

    private void UpdateYouTubeChannelAndStatus(IChatPlatform? twitch, IChatPlatform? youtube)
    {
        var configured = _catalog.GetConfiguredChannels(StreamingPlatform.YouTube);
        var active = _catalog.GetActiveChannel(StreamingPlatform.YouTube);
        var options = BuildChannelOptions(configured);

        var previousActiveId = _activeYouTubeChannelId;
        _activeYouTubeChannelId = active?.ChannelId;
        if (!string.Equals(previousActiveId, _activeYouTubeChannelId, StringComparison.Ordinal) && !IsSwitchingYouTubeChannel)
        {
            YouTubeSwitchErrorText = null;
        }

        _syncingYouTubeChannels = true;
        try
        {
            if (!ChannelsMatch(options))
            {
                YouTubeChannels.Clear();
                foreach (var option in options)
                {
                    YouTubeChannels.Add(option);
                }
            }

            if (!IsSwitchingYouTubeChannel)
            {
                SelectedYouTubeChannel = YouTubeChannels.FirstOrDefault(o =>
                    string.Equals(o.ChannelId, _activeYouTubeChannelId, StringComparison.Ordinal));
            }
        }
        finally
        {
            _syncingYouTubeChannels = false;
        }

        var isYouTubeEnabled = youtube?.IsEnabled == true;
        var isDebugSession = _targetChannelProvider?.Current.IsDebugSession == true;
        IsYouTubeSwitchBlockedByDebug = youtube?.IsBlockedByDebugSession == true
            || (isDebugSession && _targetChannelProvider?.DebugPlatform != StreamingPlatform.YouTube);

        HasYouTubeChannelSelector = isYouTubeEnabled && _switchCoordinator is not null && YouTubeChannels.Count > 1;
        ShowYouTubeSetupButton = !isYouTubeEnabled || YouTubeChannels.Count == 0;

        YouTubeChannelDisplayName = active is not null
            ? ResolveChannelDisplayName(active)
            : (youtube?.ConfiguredChannel is { Length: > 0 } fallbackName
                ? fallbackName
                : (YouTubeChannels.Count > 0 ? YouTubeChannels[0].DisplayName : "Канал не настроен"));

        OnPropertyChanged(nameof(ShowStaticYouTubeChannel));

        if (!isYouTubeEnabled || YouTubeChannels.Count == 0)
        {
            YouTubeStatusText = "Канал YouTube не настроен";
            YouTubeStatusSeverity = StatusSeverity.Info;
            return;
        }

        _monitorsByPlatform.TryGetValue(StreamingPlatform.YouTube, out var ytMonitor);
        var ytSnapshot = active is not null && ytMonitor is not null
            ? ytMonitor.GetSnapshot(active.Key)
            : null;
        var ytStatus = ytSnapshot is { Status: not StreamStatus.Unknown }
            ? ytSnapshot.Status
            : (youtube?.StreamStatus.CurrentStatus ?? StreamStatus.Unknown);

        if (ytStatus == StreamStatus.Online)
        {
            _youTubeWasOnline = true;
            var ytStream = youtube?.StreamStatus.CurrentStream is { IsBare: false } live ? live : ytSnapshot?.Stream;
            YouTubeStatusText = ytStream is { ViewerCount: > 0 }
                ? $"В эфире · {ytStream.ViewerCount.ToString("N0", UiCulture.Russian)} зр."
                : "В эфире";
            YouTubeStatusSeverity = StatusSeverity.Success;
            return;
        }

        if (ytStatus == StreamStatus.Offline)
        {
            var twitchOnline = twitch?.StreamStatus.CurrentStatus == StreamStatus.Online;
            YouTubeStatusText = "Эфир завершён / не в сети";
            YouTubeStatusSeverity = _youTubeWasOnline || twitchOnline ? StatusSeverity.Warning : StatusSeverity.Info;
            return;
        }

        YouTubeStatusText = "Ожидание эфира";
        YouTubeStatusSeverity = StatusSeverity.Info;
    }

    private void UpdateMonitoringIssues()
    {
        var snapshots = new Dictionary<ChannelKey, ChannelMonitoringSnapshot>(_monitoringSnapshots);
        foreach (var monitor in _monitorsByPlatform.Values)
        {
            foreach (var snapshot in monitor.GetSnapshots())
            {
                snapshots[snapshot.Channel] = snapshot;
            }
        }

        var issues = snapshots.Values
            .Where(static s => s.Availability is ChannelMonitoringAvailability.Unavailable or ChannelMonitoringAvailability.Degraded)
            .Select(static s => string.IsNullOrWhiteSpace(s.Detail)
                ? s.ChannelDisplayName
                : $"{s.ChannelDisplayName}: {s.Detail}")
            .ToArray();

        if (issues.Length > 0)
        {
            HasMonitoringIssue = true;
            MonitoringIssueText = $"Мониторинг ограничен: {string.Join("; ", issues)}";
        }
        else
        {
            HasMonitoringIssue = false;
            MonitoringIssueText = string.Empty;
        }
    }

    private void UpdateTimer(IChatPlatform? twitch, IChatPlatform? youtube)
    {
        var anyOnline = twitch?.StreamStatus.CurrentStatus == StreamStatus.Online
            || youtube?.StreamStatus.CurrentStatus == StreamStatus.Online;

        if (anyOnline && !_refreshTimerRunning)
        {
            _refreshTimer.Start();
            _refreshTimerRunning = true;
        }
        else if (!anyOnline && _refreshTimerRunning)
        {
            _refreshTimer.Stop();
            _refreshTimerRunning = false;
        }
    }

    private async Task OnRefreshTimerTickAsync()
    {
        var refreshed = false;
        foreach (var platform in _platformRegistry.All)
        {
            if (platform.IsEnabled && platform.StreamStatus.CurrentStatus == StreamStatus.Online)
            {
                await platform.StreamStatus.RefreshLiveSnapshotAsync();
                refreshed = true;
            }
        }

        if (refreshed)
        {
            ApplyCurrentState();
        }
    }

    private void RestoreSelectedYouTubeChannel()
    {
        _syncingYouTubeChannels = true;
        try
        {
            SelectedYouTubeChannel = YouTubeChannels.FirstOrDefault(o =>
                string.Equals(o.ChannelId, _activeYouTubeChannelId, StringComparison.Ordinal));
        }
        finally
        {
            _syncingYouTubeChannels = false;
        }
    }

    private bool ChannelsMatch(IReadOnlyList<MultistreamChannelOption> options)
    {
        if (YouTubeChannels.Count != options.Count)
        {
            return false;
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (YouTubeChannels[i] != options[i])
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<MultistreamChannelOption> BuildChannelOptions(IReadOnlyList<ChannelCatalogEntry> configured)
    {
        if (configured.Count == 0)
        {
            return [];
        }

        var nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in configured)
        {
            var displayName = ResolveChannelDisplayName(entry);
            nameCounts[displayName] = nameCounts.GetValueOrDefault(displayName) + 1;
        }

        var options = new MultistreamChannelOption[configured.Count];
        for (var i = 0; i < configured.Count; i++)
        {
            var entry = configured[i];
            var displayName = ResolveChannelDisplayName(entry);
            var label = nameCounts[displayName] > 1 ? $"{displayName} ({entry.ChannelId})" : displayName;
            options[i] = new(entry.ChannelId, displayName, label);
        }

        return options;
    }

    private static string ResolveChannelDisplayName(ChannelCatalogEntry entry) =>
        string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.ChannelId : entry.DisplayName;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _refreshTimer.Stop();
        _refreshTimerRunning = false;

        foreach (var source in _enablementSources)
        {
            source.Changed -= OnExternalChanged;
        }

        _catalog.Changed -= OnCatalogChanged;

        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        _subs.Clear();
    }
}
