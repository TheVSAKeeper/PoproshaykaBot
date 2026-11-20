using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed class MultistreamTileViewModel : DashboardTileViewModel, IDisposable
{
    public const string TileTypeId = "multistream";

    private readonly ChannelCatalogStore _catalog;
    private readonly YouTubeSettingsStore _youtubeSettings;
    private readonly BotConnectionManager _connectionManager;
    private readonly IPlatformAuthRegistry _authorization;
    private readonly IUiDispatcher _dispatcher;
    private readonly IReadOnlyDictionary<StreamingPlatform, IPlatformChannelMonitor> _monitors;
    private readonly IUiTimer _timer;
    private readonly IDisposable[] _subscriptions;
    private bool _tileOnScreen;
    private bool _pageOnScreen;
    private bool _disposed;

    public MultistreamTileViewModel(
        IChatPlatformRegistry platforms,
        ChannelCatalogStore catalog,
        BotConnectionManager connectionManager,
        IPlatformAuthRegistry authorization,
        IEnumerable<IPlatformChannelMonitor> monitors,
        IEventBus bus,
        IUiDispatcher dispatcher,
        YouTubeSettingsStore youtubeSettings)
        : base(TileTypeId, "Мультистрим", maxWidth: 500, minWidth: 280, minHeight: 324)
    {
        _catalog = catalog;
        _youtubeSettings = youtubeSettings;
        _connectionManager = connectionManager;
        _authorization = authorization;
        _dispatcher = dispatcher;
        _monitors = monitors.ToDictionary(static monitor => monitor.Platform);

        Rows = platforms.All.Select(platform => new MultistreamPlatformRowViewModel(
            platform,
            () => ConnectionToggleRequested?.Invoke(this, platform.Id),
            () => PlatformRequested?.Invoke(this, platform.Id))).ToArray();

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
            bus.SubscribeOnUi<PlatformAuthorizationRefreshed>(_ => RefreshVisible()),
        ];
        _catalog.Changed += OnCatalogChanged;
        _youtubeSettings.Changed += OnYouTubeSettingsChanged;
        Refresh();
    }

    public event EventHandler<StreamingPlatform>? ConnectionToggleRequested;

    public event EventHandler<StreamingPlatform>? PlatformRequested;

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

    private void RefreshVisible()
    {
        if (!_disposed && AnySurfaceVisible)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        foreach (var row in Rows)
        {
            var platform = row.Source;
            var active = platform.UsesChannelCatalog ? _catalog.GetActiveChannel(platform.Id) : null;
            var channelCount = platform.UsesChannelCatalog ? _catalog.GetConfiguredChannels(platform.Id).Count : 0;
            var phase = _connectionManager.GetPhase(platform.Id);
            var enabled = platform.IsEnabled;
            var blockedByDebug = !enabled && platform.Id == StreamingPlatform.YouTube && _youtubeSettings.Load().Enabled;
            var chat = platform.CaptureChatStatus(phase);
            var monitoring = active is not null && _monitors.TryGetValue(platform.Id, out var monitor)
                ? monitor.GetSnapshot(active.Key)
                : null;

            row.ChannelCount = channelCount;
            row.ChannelName = active is null
                ? platform.ConfiguredChannel ?? "Канал не настроен"
                : string.IsNullOrWhiteSpace(active.DisplayName) ? active.ChannelId : active.DisplayName;
            var status = monitoring is { Status: not StreamStatus.Unknown }
                ? monitoring.Status
                : platform.StreamStatus.CurrentStatus;
            row.ApplyBroadcast(enabled, blockedByDebug, status, monitoring?.Reason);
            var lacksBotAuthorization = _authorization.TryGet(platform.Id, out var auth)
                && auth.HasAuthorizationMaterial(PlatformAuthRole.Bot, active?.BotContextId) is false;
            row.ApplyChat(enabled, blockedByDebug, phase, chat, lacksBotAuthorization);
        }
    }
}

public sealed partial class MultistreamPlatformRowViewModel(
    IChatPlatform source,
    Action toggleConnection,
    Action openPlatform) : ObservableObject
{
    internal IChatPlatform Source { get; } = source;

    public StreamingPlatform Platform => Source.Id;

    public string DisplayName => Source.DisplayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationSummary))]
    private string _channelName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleChannels), nameof(ChannelCountText))]
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

    public bool HasMultipleChannels => ChannelCount > 1;

    public string? ChannelCountText => ChannelCount <= 1 ? null : FormatChannelCount(ChannelCount);

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

    internal void ApplyBroadcast(bool enabled, bool blockedByDebug, StreamStatus status, MonitoringUnavailableReason? reason)
    {
        IsPlatformEnabled = enabled;
        (BroadcastText, BroadcastSeverity) = (enabled, blockedByDebug, status) switch
        {
            (_, true, _) => ("Эфир: недоступен в режиме отладки Twitch", StatusSeverity.Info),
            (false, _, _) => ("Эфир: площадка выключена", StatusSeverity.Info),
            (_, _, StreamStatus.Online) => ("Эфир: идёт", StatusSeverity.Success),
            (_, _, StreamStatus.Offline) => ("Эфир: не идёт", StatusSeverity.Info),
            _ => ($"Эфир: {DescribeUnknownBroadcast(reason)}", StatusSeverity.Info),
        };
    }

    internal void ApplyChat(
        bool enabled, bool blockedByDebug, BotLifecyclePhase phase, ChatConnectionStatus? chat, bool lacksBotAuthorization)
    {
        IsPlatformEnabled = enabled;
        IsBlockedByDebugSession = blockedByDebug;
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
        CanToggleConnection = ShowConnectionAction && phase != BotLifecyclePhase.Disconnecting;

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

    private static string DescribeUnknownBroadcast(MonitoringUnavailableReason? reason) => reason switch
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
