using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class AutoBroadcastIndicatorViewModel : ObservableObject, IDisposable
{
    private readonly BroadcastScheduler _scheduler;
    private readonly SettingsManager _settings;
    private readonly IBroadcastState _broadcastState;
    private readonly IBroadcastPlatformTargetsProvider _platformTargetsProvider;
    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly BotMessagingNavigation? _messaging;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IUiTimer _countdownTimer;
    private readonly TimeProvider _time;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly ChannelCatalogStore? _catalogStore;
    private readonly List<IDisposable> _subscriptions = [];
    private bool _timerRunning;
    private bool _disposed;

    [ObservableProperty]
    private string _shortText = "Рассылка: выкл";

    [ObservableProperty]
    private StatusSeverity _severity = StatusSeverity.None;

    [ObservableProperty]
    private string _tooltip = string.Empty;

    [ObservableProperty]
    private bool _isVisible = true;

    public AutoBroadcastIndicatorViewModel(
        BroadcastScheduler scheduler,
        SettingsManager settings,
        IBroadcastState broadcastState,
        IBroadcastPlatformTargetsProvider platformTargetsProvider,
        IChatPlatformRegistry platformRegistry,
        IEventBus eventBus,
        IUiDispatcher uiDispatcher,
        BotMessagingNavigation? messaging = null,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        ChannelCatalogStore? catalogStore = null,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(broadcastState);
        ArgumentNullException.ThrowIfNull(platformTargetsProvider);
        ArgumentNullException.ThrowIfNull(platformRegistry);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(uiDispatcher);

        _scheduler = scheduler;
        _settings = settings;
        _broadcastState = broadcastState;
        _platformTargetsProvider = platformTargetsProvider;
        _platformRegistry = platformRegistry;
        _messaging = messaging;
        _uiDispatcher = uiDispatcher;
        _youtubeSettingsStore = youtubeSettingsStore;
        _catalogStore = catalogStore;
        _time = time ?? TimeProvider.System;

        _countdownTimer = uiDispatcher.CreateTimer(TimeSpan.FromSeconds(30), RefreshState);

        _subscriptions.Add(eventBus.SubscribeOnUi<BroadcastSchedulerStateChanged>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<BroadcastScheduleSettingsChanged>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<StreamWentOnline>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<StreamWentOffline>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<PlatformConnectionChanged>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<ActiveChannelChanged>(_ => RefreshState()));

        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed += OnExternalChanged;
        }

        if (_catalogStore is not null)
        {
            _catalogStore.Changed += OnCatalogChanged;
        }

        RefreshState();
    }

    [RelayCommand]
    private void OpenBroadcastRule()
    {
        _messaging?.OpenRule("broadcast");
    }

    public void RefreshState()
    {
        if (_disposed)
        {
            return;
        }

        var isActive = _scheduler.IsActive;
        var autoSettings = _settings.Current.Twitch.AutoBroadcast;
        var isAuto = autoSettings.AutoBroadcastEnabled;
        var streamOnline = _broadcastState.IsAnyLive;
        var nextTime = _scheduler.NextBroadcastTime;
        var sentCount = _scheduler.QueuedDispatchCount;

        if (isActive && nextTime.HasValue)
        {
            var remaining = nextTime.Value - _time.GetLocalNow().DateTime;
            var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
            ShortText = $"Рассылка: {minutes} мин";
            Severity = StatusSeverity.Success;
        }
        else if (isActive)
        {
            ShortText = "Рассылка: активна";
            Severity = StatusSeverity.Success;
        }
        else if (isAuto && !streamOnline)
        {
            ShortText = "Рассылка: авто";
            Severity = StatusSeverity.Info;
        }
        else if (isAuto)
        {
            ShortText = "Рассылка: пауза";
            Severity = StatusSeverity.Warning;
        }
        else
        {
            ShortText = "Рассылка: выкл";
            Severity = StatusSeverity.None;
        }

        var targets = _platformTargetsProvider.GetTargetPlatforms()
            .Order()
            .Select(p => _platformRegistry.GetDisplayName(p))
            .ToArray();
        var targetsText = targets.Length > 0 ? string.Join(", ", targets) : "не выбраны";
        var modeText = isAuto ? $"Авто (каждые {autoSettings.BroadcastIntervalMinutes} мин)" : "Ручной";
        var statusLine = isActive
            ? (nextTime.HasValue ? $"Активна, следующая в {nextTime.Value:HH:mm:ss}" : "Активна")
            : (isAuto && !streamOnline ? "Ожидание эфира" : "Остановлена");

        Tooltip = $"Авто-рассылка: {statusLine}\nРежим: {modeText}\nПлощадки: {targetsText}\nОтправлено за эфир: {sentCount}";

        UpdateTimer(isActive && nextTime.HasValue);
    }

    private void UpdateTimer(bool shouldRun)
    {
        if (shouldRun && !_timerRunning)
        {
            _countdownTimer.Start();
            _timerRunning = true;
        }
        else if (!shouldRun && _timerRunning)
        {
            _countdownTimer.Stop();
            _timerRunning = false;
        }
    }

    private void OnExternalChanged()
    {
        if (!_disposed)
        {
            _uiDispatcher.Invoke(RefreshState);
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        OnExternalChanged();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _countdownTimer.Stop();
        _timerRunning = false;

        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed -= OnExternalChanged;
        }

        if (_catalogStore is not null)
        {
            _catalogStore.Changed -= OnCatalogChanged;
        }

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }
}
