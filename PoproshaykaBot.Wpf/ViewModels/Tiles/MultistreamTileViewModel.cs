using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed record MultistreamChannelOption(string ChannelId, string DisplayName, string Label);

public sealed partial class MultistreamTileViewModel : DashboardTileViewModel, IDisposable
{
    public const string TileTypeId = "multistream";

    private readonly ChannelCatalogStore _catalog;
    private readonly YouTubeSettingsStore _youtubeSettings;
    private readonly BotConnectionManager _connectionManager;
    private readonly IPlatformAuthRegistry _authorization;
    private readonly IUiDispatcher _dispatcher;
    private readonly IReadOnlyDictionary<StreamingPlatform, IPlatformChannelMonitor> _monitors;
    private readonly IChannelSwitchCoordinator? _switchCoordinator;
    private readonly ITargetChannelProvider? _targetChannelProvider;
    private readonly ILogger<MultistreamTileViewModel>? _logger;
    private readonly IUiTimer _timer;
    private readonly IDisposable[] _subscriptions;
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _tileOnScreen;
    private bool _pageOnScreen;
    private bool _isBulkOperationInProgress;
    private bool _disposed;

    [ObservableProperty]
    private string _summaryText = "Нет подключённых площадок";

    [ObservableProperty]
    private StatusSeverity _summarySeverity;

    [ObservableProperty]
    private string _bulkConnectionLabel = "Подключить все";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BulkConnectionCommand))]
    private bool _showBulkConnectionAction;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BulkConnectionCommand))]
    private bool _isBulkConnectionBusy;

    public MultistreamTileViewModel(
        IChatPlatformRegistry platforms,
        ChannelCatalogStore catalog,
        BotConnectionManager connectionManager,
        IPlatformAuthRegistry authorization,
        IEnumerable<IPlatformChannelMonitor> monitors,
        IEventBus bus,
        IUiDispatcher dispatcher,
        YouTubeSettingsStore youtubeSettings,
        IChannelSwitchCoordinator? switchCoordinator = null,
        ITargetChannelProvider? targetChannelProvider = null,
        ILogger<MultistreamTileViewModel>? logger = null)
        : base(TileTypeId, "Мультистрим", maxWidth: 500, minWidth: 280, minHeight: 324)
    {
        _catalog = catalog;
        _youtubeSettings = youtubeSettings;
        _connectionManager = connectionManager;
        _authorization = authorization;
        _dispatcher = dispatcher;
        _monitors = monitors.ToDictionary(static monitor => monitor.Platform);
        _switchCoordinator = switchCoordinator;
        _targetChannelProvider = targetChannelProvider;
        _logger = logger;

        Rows = platforms.All.Select(platform => new MultistreamPlatformRowViewModel(
            platform,
            () => ConnectionToggleRequested?.Invoke(this, platform.Id),
            () => PlatformRequested?.Invoke(this, platform.Id),
            option => OnChannelSelectionRequested(platform.Id, option))).ToArray();

        _timer = dispatcher.CreateTimer(TimeSpan.FromSeconds(1), RefreshVisible);
        _subscriptions =
        [
            bus.SubscribeOnUi<PlatformConnectionChanged>(_ => RefreshVisible()),
            bus.SubscribeOnUi<ChatIngestionStarted>(_ => RefreshVisible()),
            bus.SubscribeOnUi<ChatIngestionStopped>(_ => RefreshVisible()),
            bus.SubscribeOnUi<PlatformChatAvailabilityChanged>(_ => RefreshVisible()),
            bus.SubscribeOnUi<ChannelMonitoringStateChanged>(_ => RefreshVisible()),
            bus.SubscribeOnUi<ActiveChannelChanged>(_ => RefreshVisible()),
            bus.SubscribeOnUi<StreamWentOnline>(_ => RefreshVisible()),
            bus.SubscribeOnUi<StreamWentOffline>(_ => RefreshVisible()),
            bus.SubscribeOnUi<StreamMetadataResolved>(_ => RefreshVisible()),
            bus.SubscribeOnUi<PlatformAuthorizationRefreshed>(_ => RefreshVisible()),
        ];
        _catalog.Changed += OnCatalogChanged;
        _youtubeSettings.Changed += OnYouTubeSettingsChanged;
        Refresh();
    }

    public event EventHandler<StreamingPlatform>? ConnectionToggleRequested;

    public event EventHandler<StreamingPlatform>? PlatformRequested;

    public event Action<string, StatusSeverity>? StatusReported;

    public IReadOnlyList<MultistreamPlatformRowViewModel> Rows { get; }

    public override PackIconLucideKind Icon => PackIconLucideKind.Radio;

    public override bool GrowsWithSpace => true;

    internal void ApplyPlatformControls(IEnumerable<IMultistreamPlatformControls> platformControls)
    {
        ArgumentNullException.ThrowIfNull(platformControls);

        var controlsByPlatform = platformControls.ToDictionary(static control => control.Platform);
        foreach (var row in Rows)
        {
            row.CanEnableFromSettings = controlsByPlatform.TryGetValue(row.Platform, out var controls)
                && controls.CanEnableFromSettings;
        }
    }

    public void SetOnScreen(bool onScreen) => SetSurfaceVisibility(ref _tileOnScreen, onScreen);

    public void SetPageOnScreen(bool onScreen) => SetSurfaceVisibility(ref _pageOnScreen, onScreen);

    private bool AnySurfaceVisible => _tileOnScreen || _pageOnScreen;

    private void SetSurfaceVisibility(ref bool surfaceOnScreen, bool onScreen)
    {
        if (_disposed || surfaceOnScreen == onScreen)
        {
            return;
        }

        var wasVisible = AnySurfaceVisible;
        surfaceOnScreen = onScreen;
        if (onScreen)
        {
            Refresh();
            if (!wasVisible)
            {
                _timer.Start();
            }
        }
        else if (!AnySurfaceVisible)
        {
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposeCts.Cancel();
        _disposeCts.Dispose();
        _timer.Stop();
        _catalog.Changed -= OnCatalogChanged;
        _youtubeSettings.Changed -= OnYouTubeSettingsChanged;

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs e) => _dispatcher.Invoke(RefreshVisible);

    private void OnYouTubeSettingsChanged() => _dispatcher.Invoke(RefreshVisible);

    private void OnChannelSelectionRequested(StreamingPlatform platform, MultistreamChannelOption target)
    {
        var row = Rows.FirstOrDefault(item => item.Platform == platform);
        if (row is null)
        {
            return;
        }

        _ = UiAsync.RunGuardedAsync(
            () => SwitchChannelAsync(row, target),
            exception => OnSwitchUnhandledFailure(row, exception));
    }

    internal async Task SwitchChannelAsync(MultistreamPlatformRowViewModel row, MultistreamChannelOption target)
    {
        if (_disposed || _switchCoordinator is null || row.IsSwitchingChannel)
        {
            return;
        }

        row.BeginChannelSwitch();
        RefreshSummaryAndBulkState();
        try
        {
            var result = await _switchCoordinator
                .SwitchActiveChannelAsync(row.Platform, new(row.Platform, target.ChannelId), _disposeCts.Token)
                .ConfigureAwait(true);
            if (_disposed)
            {
                return;
            }

            if (result.Succeeded)
            {
                _logger?.ChannelSwitchedByUser(row.DisplayName, target.DisplayName);
                row.ClearSwitchError();
                StatusReported?.Invoke($"Активный канал {row.DisplayName} переключён на «{target.DisplayName}»", StatusSeverity.Info);
            }
            else
            {
                _logger?.ChannelSwitchOperationFailed(null, row.DisplayName);
                var message = result.ErrorMessage ?? $"Не удалось переключить канал {row.DisplayName} на «{target.DisplayName}».";
                var severity = result.Outcome == ChannelSwitchOutcome.FailedNoActiveChat
                    ? StatusSeverity.Error
                    : StatusSeverity.Warning;
                row.SetSwitchError(message, severity);
                StatusReported?.Invoke(message, severity);
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            if (_disposed)
            {
                return;
            }

            OnSwitchUnhandledFailure(row, ex);
        }
        finally
        {
            if (!_disposed)
            {
                row.EndChannelSwitch();
                Refresh();
                if (!row.IsSelectedChannel(target.ChannelId))
                {
                    row.NotifySelectedChannelReverted();
                }
            }
        }
    }

    private void OnSwitchUnhandledFailure(MultistreamPlatformRowViewModel row, Exception exception)
    {
        if (_disposed)
        {
            return;
        }

        _logger?.ChannelSwitchOperationFailed(exception, row.DisplayName);
        var message = $"Не удалось переключить канал {row.DisplayName}: {StreamingErrorMessages.SafeMessage(exception)}";
        _dispatcher.Invoke(() =>
        {
            row.SetSwitchError(message, StatusSeverity.Error);
            StatusReported?.Invoke(message, StatusSeverity.Error);
        });
    }

    [RelayCommand(CanExecute = nameof(CanBulkConnection))]
    private async Task BulkConnectionAsync()
    {
        if (_disposed || _isBulkOperationInProgress)
        {
            return;
        }

        var readyRows = Rows.Where(static r => r.ShowConnectionAction).ToArray();
        if (readyRows.Length < 2)
        {
            return;
        }

        var toConnect = readyRows
            .Where(static r => IsDisconnectedPhase(r.LifecyclePhase) && r.CanToggleConnection)
            .ToArray();
        var toDisconnect = toConnect.Length == 0
            ? readyRows.Where(static r => r.LifecyclePhase == BotLifecyclePhase.Connected && r.CanToggleConnection).ToArray()
            : [];

        if (toConnect.Length == 0 && toDisconnect.Length == 0)
        {
            return;
        }

        _isBulkOperationInProgress = true;
        Refresh();
        try
        {
            if (toConnect.Length > 0)
            {
                var waitTasks = new List<Task>(toConnect.Length);
                foreach (var row in toConnect)
                {
                    try
                    {
                        _connectionManager.StartConnection(row.Platform);
                        waitTasks.Add(_connectionManager.WaitForConnectionAsync(row.Platform));
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

                if (waitTasks.Count > 0)
                {
                    await Task.WhenAll(waitTasks).ConfigureAwait(true);
                }
            }
            else
            {
                await Task.WhenAll(toDisconnect.Select(row => _connectionManager.StopAsync(row.Platform))).ConfigureAwait(true);
            }
        }
        finally
        {
            _isBulkOperationInProgress = false;
            if (!_disposed)
            {
                Refresh();
            }
        }
    }

    private bool CanBulkConnection()
    {
        if (_disposed || !ShowBulkConnectionAction || IsBulkConnectionBusy)
        {
            return false;
        }

        return Rows.Any(static r => r.ShowConnectionAction
            && r.CanToggleConnection
            && (IsDisconnectedPhase(r.LifecyclePhase) || r.LifecyclePhase == BotLifecyclePhase.Connected));
    }

    private static bool IsDisconnectedPhase(BotLifecyclePhase phase) =>
        phase is not (BotLifecyclePhase.Connected or BotLifecyclePhase.Connecting or BotLifecyclePhase.Disconnecting);

    private void RefreshVisible()
    {
        if (!_disposed && AnySurfaceVisible)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var isTwitchDebugSession = _targetChannelProvider?.Current.IsDebugSession == true;
        foreach (var row in Rows)
        {
            var platform = row.Source;
            var configured = platform.UsesChannelCatalog ? _catalog.GetConfiguredChannels(platform.Id) : [];
            var active = platform.UsesChannelCatalog ? _catalog.GetActiveChannel(platform.Id) : null;
            var channelCount = configured.Count;
            var phase = _connectionManager.GetPhase(platform.Id);
            var enabled = platform.IsEnabled;
            var blockedByDebug = !enabled && platform.Id == StreamingPlatform.YouTube && _youtubeSettings.Load().Enabled;
            var isSwitchBlockedByDebug = blockedByDebug || (platform.Id == StreamingPlatform.Twitch && isTwitchDebugSession);
            var chat = platform.CaptureChatStatus(phase);
            _monitors.TryGetValue(platform.Id, out var monitor);
            var monitoring = active is not null && monitor is not null
                ? monitor.GetSnapshot(active.Key)
                : null;

            row.ChannelCount = channelCount;
            row.ChannelName = active is null
                ? platform.ConfiguredChannel ?? "Канал не настроен"
                : ResolveChannelDisplayName(active);
            row.ApplyChannels(
                BuildChannelOptions(configured),
                active?.ChannelId,
                platform.UsesChannelCatalog && _switchCoordinator is not null,
                isSwitchBlockedByDebug);
            var status = monitoring is { Status: not StreamStatus.Unknown }
                ? monitoring.Status
                : platform.StreamStatus.CurrentStatus;
            row.ApplyBroadcast(enabled, blockedByDebug, status, monitoring?.Reason);

            var isOnline = enabled && !blockedByDebug && status == StreamStatus.Online;
            var liveInfo = isOnline
                ? ResolveLiveStreamInfo(platform.StreamStatus.CurrentStream, monitoring?.Stream)
                : null;
            row.ApplyLiveStreamInfo(isOnline, liveInfo);

            var hasAuthService = _authorization.TryGet(platform.Id, out var auth);
            var lacksBotAuthorization = hasAuthService
                && auth!.HasAuthorizationMaterial(PlatformAuthRole.Bot, active?.BotContextId) is false;
            var lacksBroadcasterAuthorization = hasAuthService
                && auth!.HasAuthorizationMaterial(PlatformAuthRole.Broadcaster, active?.BroadcasterContextId) is false;
            row.ApplyChat(enabled, blockedByDebug, phase, chat, lacksBotAuthorization);

            var isBotOnly = enabled
                && !blockedByDebug
                && !row.NeedsBotAuthorization
                && lacksBroadcasterAuthorization
                && (row.ShowConnectionAction || phase == BotLifecyclePhase.Connected || chat?.State == ConnectionState.Connected);

            var (switchModeBadge, switchModeTooltip) = ResolveSwitchModeBadge(
                platform, enabled, blockedByDebug, channelCount);

            var allSnapshots = CollectPlatformSnapshots(platform.Id, monitor, monitoring);
            var otherLive = enabled && !blockedByDebug
                ? allSnapshots
                    .Where(s => s.Status == StreamStatus.Online
                        && (active is null || !string.Equals(s.Channel.ChannelId, active.ChannelId, StringComparison.Ordinal)))
                    .ToArray()
                : [];
            var backgroundLiveText = otherLive.Length > 0 ? $"Ещё {otherLive.Length} в эфире" : null;
            var backgroundLiveTooltip = otherLive.Length > 0
                ? $"В эфире на других каналах: {string.Join(", ", otherLive.Select(static s => string.IsNullOrWhiteSpace(s.ChannelDisplayName) ? s.Channel.ChannelId : s.ChannelDisplayName))}"
                : null;
            var monitoringWarning = enabled && !blockedByDebug
                ? FormatMonitoringWarning(allSnapshots, channelCount > 1)
                : null;

            row.ApplyContextSignals(
                isBotOnly,
                switchModeBadge,
                switchModeTooltip,
                otherLive.Length,
                backgroundLiveText,
                backgroundLiveTooltip,
                monitoringWarning);
        }

        RefreshSummaryAndBulkState();
    }

    private void RefreshSummaryAndBulkState()
    {
        var onlineCount = 0;
        var totalViewers = 0;
        var enabledCount = 0;
        var connectedCount = 0;

        foreach (var row in Rows)
        {
            if (row.IsOnline)
            {
                onlineCount++;
                totalViewers += row.ViewerCount;
            }

            if (row.IsPlatformEnabled)
            {
                enabledCount++;
            }

            if (row.LifecyclePhase == BotLifecyclePhase.Connected || row.IsChatConnected)
            {
                connectedCount++;
            }
        }

        if (onlineCount > 0)
        {
            SummaryText = totalViewers > 0
                ? $"В эфире: {onlineCount} · {totalViewers.ToString("N0", UiCulture.Russian)} зр."
                : $"В эфире: {onlineCount}";
            SummarySeverity = StatusSeverity.Success;
        }
        else if (connectedCount > 0)
        {
            var denominator = Math.Max(connectedCount, enabledCount);
            SummaryText = $"Подключено {connectedCount} из {denominator}";
            SummarySeverity = connectedCount >= denominator ? StatusSeverity.Success : StatusSeverity.Info;
        }
        else
        {
            SummaryText = "Нет подключённых площадок";
            SummarySeverity = StatusSeverity.None;
        }

        var readyRows = Rows.Where(static r => r.ShowConnectionAction).ToArray();
        ShowBulkConnectionAction = readyRows.Length >= 2;
        var hasDisconnectedReady = readyRows.Any(static r => IsDisconnectedPhase(r.LifecyclePhase));
        var anyTransitioning = readyRows.Any(static r =>
            r.LifecyclePhase is BotLifecyclePhase.Connecting or BotLifecyclePhase.Disconnecting
            || r.IsSwitchingChannel);
        BulkConnectionLabel = hasDisconnectedReady ? "Подключить все" : "Отключить все";
        IsBulkConnectionBusy = _isBulkOperationInProgress || anyTransitioning;
        BulkConnectionCommand.NotifyCanExecuteChanged();
    }

    private (string? BadgeText, string? Tooltip) ResolveSwitchModeBadge(
        IChatPlatform platform,
        bool enabled,
        bool blockedByDebug,
        int channelCount)
    {
        if (!enabled || blockedByDebug || !platform.UsesChannelCatalog || channelCount <= 1)
        {
            return (null, null);
        }

        var routing = _catalog.GetPlatformSettings(platform.Id);
        return routing.SwitchMode switch
        {
            ChannelSwitchMode.AutoDiscover => (
                "Автопереключение",
                "Автоматический поиск и переключение активного канала"),
            ChannelSwitchMode.PreselectedNext => (
                "Следующий канал",
                ResolvePreselectedNextTooltip(platform.Id, routing.PreselectedNextChannelId)),
            _ => (null, null),
        };
    }

    private string ResolvePreselectedNextTooltip(StreamingPlatform platform, string? nextChannelId)
    {
        var nextEntry = !string.IsNullOrWhiteSpace(nextChannelId)
            ? _catalog.FindEntry(new(platform, nextChannelId))
            : null;
        return nextEntry is not null
            ? $"Следующий канал: {ResolveChannelDisplayName(nextEntry)}"
            : "Запланирован переход на следующий канал";
    }

    private static StreamInfo? ResolveLiveStreamInfo(StreamInfo? platformStream, StreamInfo? monitoredStream)
    {
        if (platformStream is { IsBare: false })
        {
            return platformStream;
        }

        return monitoredStream is { IsBare: false } ? monitoredStream : null;
    }

    private static IReadOnlyList<ChannelMonitoringSnapshot> CollectPlatformSnapshots(
        StreamingPlatform platform,
        IPlatformChannelMonitor? monitor,
        ChannelMonitoringSnapshot? activeSnapshot)
    {
        if (monitor is null && activeSnapshot is null)
        {
            return [];
        }

        var byChannel = new Dictionary<ChannelKey, ChannelMonitoringSnapshot>();
        if (monitor is not null)
        {
            foreach (var snapshot in monitor.GetSnapshots())
            {
                if (snapshot.Channel.Platform == platform)
                {
                    byChannel[snapshot.Channel] = snapshot;
                }
            }
        }

        if (activeSnapshot is not null && activeSnapshot.Channel.Platform == platform)
        {
            byChannel[activeSnapshot.Channel] = activeSnapshot;
        }

        return [.. byChannel.Values];
    }

    private static string? FormatMonitoringWarning(
        IReadOnlyList<ChannelMonitoringSnapshot> snapshots,
        bool includeChannelName)
    {
        var issues = new List<string>();
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Availability is not (ChannelMonitoringAvailability.Degraded or ChannelMonitoringAvailability.Unavailable))
            {
                continue;
            }

            var detail = !string.IsNullOrWhiteSpace(snapshot.Detail)
                ? snapshot.Detail.Trim()
                : MultistreamPlatformRowViewModel.DescribeUnavailableReason(snapshot.Reason);
            if (string.IsNullOrWhiteSpace(detail))
            {
                continue;
            }

            var channelName = string.IsNullOrWhiteSpace(snapshot.ChannelDisplayName)
                ? snapshot.Channel.ChannelId
                : snapshot.ChannelDisplayName;
            issues.Add(includeChannelName ? $"{channelName}: {detail}" : detail);
        }

        if (issues.Count == 0)
        {
            return null;
        }

        return issues.Count <= 2
            ? $"Мониторинг: {string.Join("; ", issues)}"
            : $"Мониторинг: {issues[0]}; {issues[1]}; …";
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
}

public sealed partial class MultistreamPlatformRowViewModel(
    IChatPlatform source,
    Action toggleConnection,
    Action openPlatform,
    Action<MultistreamChannelOption>? switchChannel = null) : ObservableObject
{
    private bool _syncingChannels;
    private string? _activeChannelId;
    private bool _canToggleConnectionBase;

    internal IChatPlatform Source { get; } = source;

    public StreamingPlatform Platform => Source.Id;

    public string DisplayName => Source.DisplayName;

    public ObservableCollection<MultistreamChannelOption> Channels { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChannelSelectorTooltip))]
    private MultistreamChannelOption? _selectedChannel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStaticChannelName), nameof(CanSwitchChannel))]
    private bool _hasChannelSelector;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchChannel), nameof(HasChannelBadge), nameof(ChannelBadgeText))]
    private bool _isSwitchingChannel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSwitchChannel), nameof(ChannelSelectorTooltip))]
    private bool _isChannelSwitchBlockedByDebug;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSwitchError))]
    private string? _switchErrorText;

    [ObservableProperty]
    private StatusSeverity _switchErrorSeverity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationSummary), nameof(ChannelSelectorTooltip))]
    private string _channelName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleChannels), nameof(ChannelCountText), nameof(HasChannelBadge), nameof(ChannelBadgeText))]
    private int _channelCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisabled), nameof(StatusBadgeText), nameof(StatusBadgeSeverity),
        nameof(PlatformActionLabel), nameof(PlatformActionName), nameof(AutomationSummary))]
    private bool _isPlatformEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationSummary))]
    private string _broadcastText = string.Empty;

    [ObservableProperty]
    private StatusSeverity _broadcastSeverity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationSummary))]
    private string _chatText = string.Empty;

    [ObservableProperty]
    private StatusSeverity _chatSeverity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionActionName), nameof(ConnectionActionWithPlatform))]
    private string _connectionActionLabel = "Подключить";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionActionWithPlatform))]
    private BotLifecyclePhase _lifecyclePhase;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlatformActionLabel), nameof(PlatformActionName),
        nameof(StatusBadgeText), nameof(StatusBadgeSeverity), nameof(AutomationSummary))]
    private bool _needsBotAuthorization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDisabled), nameof(PlatformActionLabel), nameof(PlatformActionName),
        nameof(StatusBadgeText), nameof(StatusBadgeSeverity), nameof(AutomationSummary))]
    private bool _isBlockedByDebugSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlatformActionLabel), nameof(PlatformActionName))]
    private bool _canEnableFromSettings;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleConnectionCommand))]
    private bool _canToggleConnection;

    [ObservableProperty]
    private bool _isSetupRequired;

    [ObservableProperty]
    private bool _showConnectionAction;

    [ObservableProperty]
    private bool _isOnline;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasViewerCount))]
    private string? _viewerCountText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLiveSummary))]
    private string? _liveSummaryText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasContextBadges))]
    private bool _isBotOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSwitchModeBadge), nameof(HasContextBadges))]
    private string? _switchModeBadgeText;

    [ObservableProperty]
    private string? _switchModeBadgeTooltip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBackgroundLive), nameof(HasContextBadges))]
    private int _backgroundLiveCount;

    [ObservableProperty]
    private string? _backgroundLiveText;

    [ObservableProperty]
    private string? _backgroundLiveTooltip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMonitoringWarning))]
    private string? _monitoringWarningText;

    internal int ViewerCount { get; private set; }

    internal bool IsChatConnected { get; private set; }

    public bool HasViewerCount => !string.IsNullOrWhiteSpace(ViewerCountText);

    public bool HasLiveSummary => !string.IsNullOrWhiteSpace(LiveSummaryText);

    public bool HasSwitchModeBadge => !string.IsNullOrWhiteSpace(SwitchModeBadgeText);

    public bool HasBackgroundLive => BackgroundLiveCount > 0 && !string.IsNullOrWhiteSpace(BackgroundLiveText);

    public bool HasContextBadges => IsBotOnly || HasSwitchModeBadge || HasBackgroundLive;

    public bool HasMonitoringWarning => !string.IsNullOrWhiteSpace(MonitoringWarningText);

    public bool HasMultipleChannels => ChannelCount > 1;

    public string? ChannelCountText => ChannelCount <= 1 ? null : FormatChannelCount(ChannelCount);

    public bool ShowStaticChannelName => !HasChannelSelector;

    public bool CanSwitchChannel => HasChannelSelector && !IsSwitchingChannel && !IsChannelSwitchBlockedByDebug;

    public bool HasChannelBadge => HasMultipleChannels || IsSwitchingChannel;

    public string? ChannelBadgeText => IsSwitchingChannel ? "Переключение…" : ChannelCountText;

    public bool HasSwitchError => !string.IsNullOrWhiteSpace(SwitchErrorText);

    public string ChannelSelectorName => $"Активный канал – {DisplayName}";

    public string ChannelSelectorTooltip => IsChannelSwitchBlockedByDebug
        ? "Переключение канала отключено в режиме отладки"
        : SelectedChannel?.Label ?? ChannelName;

    public bool IsDisabled => !IsPlatformEnabled && !IsBlockedByDebugSession;

    public string? StatusBadgeText => IsDisabled
        ? "Отключён"
        : IsBlockedByDebugSession
            ? "Отладка Twitch"
            : NeedsBotAuthorization
                ? "Нужен вход бота"
                : null;

    public StatusSeverity StatusBadgeSeverity => NeedsBotAuthorization ? StatusSeverity.Warning : StatusSeverity.Info;

    public string ConnectionActionWithPlatform => LifecyclePhase switch
    {
        BotLifecyclePhase.Connecting or BotLifecyclePhase.Disconnecting => ConnectionActionLabel,
        _ => $"{ConnectionActionLabel} {DisplayName}",
    };

    public string PlatformActionLabel => IsBlockedByDebugSession
        ? "Подробнее"
        : NeedsBotAuthorization
            ? "Войти как бот"
            : IsDisabled && CanEnableFromSettings
                ? "Включить"
                : "Настроить";

    public string PlatformActionName => $"{PlatformActionLabel} – {DisplayName}";

    public string ConnectionActionName => $"{ConnectionActionLabel} – {DisplayName}";

    public string AutomationSummary => string.Join(", ", new[]
    {
        DisplayName,
        ChannelName,
        StatusBadgeText,
        BroadcastText,
        ChatText,
    }.Where(static part => !string.IsNullOrWhiteSpace(part)));

    [RelayCommand(CanExecute = nameof(CanToggleConnection))]
    private void ToggleConnection() => toggleConnection();

    [RelayCommand]
    private void OpenPlatform() => openPlatform();

    partial void OnSelectedChannelChanged(MultistreamChannelOption? value)
    {
        if (_syncingChannels || value is null)
        {
            return;
        }

        if (string.Equals(value.ChannelId, _activeChannelId, StringComparison.Ordinal))
        {
            return;
        }

        if (!CanSwitchChannel || switchChannel is null)
        {
            RestoreSelectedChannel();
            NotifySelectedChannelReverted();
            return;
        }

        switchChannel(value);
    }

    internal void ApplyChannels(
        IReadOnlyList<MultistreamChannelOption> options,
        string? activeChannelId,
        bool canSelectFromCatalog,
        bool isBlockedByDebug)
    {
        var previousActiveId = _activeChannelId;
        _activeChannelId = activeChannelId;
        if (!string.Equals(previousActiveId, activeChannelId, StringComparison.Ordinal) && !IsSwitchingChannel)
        {
            ClearSwitchError();
        }

        _syncingChannels = true;
        try
        {
            if (!ChannelsMatch(options))
            {
                Channels.Clear();
                foreach (var option in options)
                {
                    Channels.Add(option);
                }
            }

            if (!IsSwitchingChannel)
            {
                SelectedChannel = Channels.FirstOrDefault(option =>
                    string.Equals(option.ChannelId, activeChannelId, StringComparison.Ordinal));
            }
        }
        finally
        {
            _syncingChannels = false;
        }

        HasChannelSelector = canSelectFromCatalog && Channels.Count > 1;
        IsChannelSwitchBlockedByDebug = isBlockedByDebug;
    }

    internal void BeginChannelSwitch()
    {
        ClearSwitchError();
        IsSwitchingChannel = true;
        CanToggleConnection = _canToggleConnectionBase && !IsSwitchingChannel;
    }

    internal void EndChannelSwitch()
    {
        IsSwitchingChannel = false;
        CanToggleConnection = _canToggleConnectionBase && !IsSwitchingChannel;
    }

    internal void SetSwitchError(string message, StatusSeverity severity)
    {
        SwitchErrorSeverity = severity;
        SwitchErrorText = message;
    }

    internal void ClearSwitchError()
    {
        SwitchErrorText = null;
        SwitchErrorSeverity = StatusSeverity.None;
    }

    internal bool IsSelectedChannel(string channelId) =>
        string.Equals(SelectedChannel?.ChannelId, channelId, StringComparison.Ordinal);

    internal void NotifySelectedChannelReverted()
    {
        if (SynchronizationContext.Current is { } syncContext)
        {
            syncContext.Post(static state =>
            {
                var row = (MultistreamPlatformRowViewModel)state!;
                row.OnPropertyChanged(nameof(SelectedChannel));
            }, this);
        }
    }

    private void RestoreSelectedChannel()
    {
        _syncingChannels = true;
        try
        {
            SelectedChannel = Channels.FirstOrDefault(option =>
                string.Equals(option.ChannelId, _activeChannelId, StringComparison.Ordinal));
        }
        finally
        {
            _syncingChannels = false;
        }
    }

    private bool ChannelsMatch(IReadOnlyList<MultistreamChannelOption> options)
    {
        if (Channels.Count != options.Count)
        {
            return false;
        }

        for (var i = 0; i < options.Count; i++)
        {
            if (Channels[i] != options[i])
            {
                return false;
            }
        }

        return true;
    }

    internal void ApplyBroadcast(bool enabled, bool blockedByDebug, StreamStatus status, MonitoringUnavailableReason? reason)
    {
        IsPlatformEnabled = enabled;
        (BroadcastText, BroadcastSeverity) = (enabled, blockedByDebug, status) switch
        {
            (_, true, _) => ("Эфир: недоступен в режиме отладки Twitch", StatusSeverity.Info),
            (false, _, _) => ("Эфир: площадка выключена", StatusSeverity.Info),
            (_, _, StreamStatus.Online) => ("Эфир: идёт", StatusSeverity.Success),
            (_, _, StreamStatus.Offline) => ("Эфир: не идёт", StatusSeverity.Info),
            _ => ($"Эфир: {DescribeUnavailableReason(reason)}", StatusSeverity.Info),
        };
    }

    internal void ApplyLiveStreamInfo(bool isOnline, StreamInfo? streamInfo)
    {
        IsOnline = isOnline;
        if (!isOnline || streamInfo is null)
        {
            ViewerCount = 0;
            ViewerCountText = null;
            LiveSummaryText = null;
            return;
        }

        ViewerCount = Math.Max(0, streamInfo.ViewerCount);
        ViewerCountText = ViewerCount > 0
            ? $"{ViewerCount.ToString("N0", UiCulture.Russian)} зр."
            : null;
        LiveSummaryText = FormatLiveSummary(streamInfo);
    }

    internal void ApplyContextSignals(
        bool isBotOnly,
        string? switchModeBadgeText,
        string? switchModeBadgeTooltip,
        int backgroundLiveCount,
        string? backgroundLiveText,
        string? backgroundLiveTooltip,
        string? monitoringWarningText)
    {
        IsBotOnly = isBotOnly;
        SwitchModeBadgeText = switchModeBadgeText;
        SwitchModeBadgeTooltip = switchModeBadgeTooltip;
        BackgroundLiveCount = backgroundLiveCount;
        BackgroundLiveText = backgroundLiveText;
        BackgroundLiveTooltip = backgroundLiveTooltip;
        MonitoringWarningText = monitoringWarningText;
    }

    internal void ApplyChat(
        bool enabled, bool blockedByDebug, BotLifecyclePhase phase, ChatConnectionStatus? chat, bool lacksBotAuthorization)
    {
        IsPlatformEnabled = enabled;
        IsBlockedByDebugSession = blockedByDebug;
        IsChatConnected = chat?.State == ConnectionState.Connected && chat.Issue == ChatReadIssue.None;
        NeedsBotAuthorization = enabled && (chat?.Issue == ChatReadIssue.MissingBotAuth
            || (chat?.State != ConnectionState.Connected && lacksBotAuthorization));
        IsSetupRequired = !enabled || NeedsBotAuthorization;
        LifecyclePhase = phase;
        ConnectionActionLabel = phase switch
        {
            BotLifecyclePhase.Connecting => "Отменить подключение",
            BotLifecyclePhase.Connected => "Отключить",
            BotLifecyclePhase.Disconnecting => "Отключается…",
            _ => "Подключить",
        };
        ShowConnectionAction = !IsSetupRequired
            || phase is BotLifecyclePhase.Connecting or BotLifecyclePhase.Connected or BotLifecyclePhase.Disconnecting;
        _canToggleConnectionBase = ShowConnectionAction && phase != BotLifecyclePhase.Disconnecting;
        CanToggleConnection = _canToggleConnectionBase && !IsSwitchingChannel;

        (ChatText, ChatSeverity) = DescribeChat(enabled, phase, chat);
    }

    private (string, StatusSeverity) DescribeChat(bool enabled, BotLifecyclePhase phase, ChatConnectionStatus? chat)
    {
        if (phase == BotLifecyclePhase.Connecting)
        {
            return ("Чат: подключается…", StatusSeverity.Info);
        }

        if (phase == BotLifecyclePhase.Disconnecting)
        {
            return ("Чат: отключается…", StatusSeverity.Info);
        }

        if (IsBlockedByDebugSession)
        {
            return ("Чат: недоступен в режиме отладки Twitch", StatusSeverity.Info);
        }

        if (chat?.State == ConnectionState.Connected && chat.Issue == ChatReadIssue.None)
        {
            return ("Чат: подключён", StatusSeverity.Success);
        }

        if (!enabled)
        {
            return ("Чат: площадка выключена", StatusSeverity.Info);
        }

        if (NeedsBotAuthorization)
        {
            return ("Чат: нужна авторизация бота", StatusSeverity.Warning);
        }

        return chat?.Issue switch
        {
            ChatReadIssue.WaitingForActiveBroadcast => ("Чат: ожидание чата эфира", StatusSeverity.Info),
            ChatReadIssue.QuotaExhausted => ("Чат: исчерпана квота API", StatusSeverity.Warning),
            ChatReadIssue.TransientError => ("Чат: сбой чтения, повтор подключения", StatusSeverity.Warning),
            ChatReadIssue.SubscriptionFailed => ("Чат: подписка не подтверждена", StatusSeverity.Error),
            _ => (phase, chat?.State) switch
            {
                (_, ConnectionState.Connecting) => ("Чат: подключается…", StatusSeverity.Info),
                (_, ConnectionState.Reconnecting) => ("Чат: переподключается…", StatusSeverity.Warning),
                (BotLifecyclePhase.Failed, _) or (_, ConnectionState.Failed) =>
                    ("Чат: сбой подключения, подробности в журнале", StatusSeverity.Error),
                (BotLifecyclePhase.Connected, null or ConnectionState.Unknown) =>
                    ("Чат: статус неизвестен", StatusSeverity.Info),
                _ => ("Чат: не подключён", StatusSeverity.Info),
            },
        };
    }

    private static string FormatChannelCount(int count)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;
        var noun = mod100 is >= 11 and <= 14
            ? "каналов"
            : mod10 == 1
                ? "канал"
                : mod10 is >= 2 and <= 4
                    ? "канала"
                    : "каналов";
        return $"{count} {noun}";
    }

    private static string? FormatLiveSummary(StreamInfo info)
    {
        string? uptimeText = null;
        if (info.StartedAt != default)
        {
            var elapsed = DateTime.UtcNow - info.StartedAt;
            if (elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromDays(7))
            {
                uptimeText = elapsed.TotalHours >= 1
                    ? $"{(int)elapsed.TotalHours} ч {elapsed.Minutes:D2} м"
                    : $"{Math.Max(1, (int)elapsed.TotalMinutes)} мин";
            }
        }

        string? titleSnippet = null;
        if (!string.IsNullOrWhiteSpace(info.Title))
        {
            var normalized = string.Join(' ', info.Title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            titleSnippet = normalized.Length > 60 ? $"{normalized[..59]}…" : normalized;
        }

        return (uptimeText, titleSnippet) switch
        {
            ({ } u, { } t) => $"{u} · {t}",
            ({ } u, null) => u,
            (null, { } t) => t,
            _ => null,
        };
    }

    internal static string DescribeUnavailableReason(MonitoringUnavailableReason? reason) => reason switch
    {
        MonitoringUnavailableReason.MonitoringDisabled => "мониторинг выключен",
        MonitoringUnavailableReason.MissingBroadcasterAuth => "нужна авторизация ведущего",
        MonitoringUnavailableReason.MissingBotAuth => "нужна авторизация бота",
        MonitoringUnavailableReason.AccessUnverified => "доступ к каналу не проверен",
        MonitoringUnavailableReason.QuotaExhausted => "исчерпана квота API",
        MonitoringUnavailableReason.SubscriptionLimitReached => "достигнут лимит подписок",
        MonitoringUnavailableReason.MultipleActiveBroadcasts => "найдено несколько трансляций",
        MonitoringUnavailableReason.TransportDisconnected => "мониторинг недоступен",
        MonitoringUnavailableReason.InvalidChannel => "канал не найден",
        _ => "статус неизвестен",
    };
}
