using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
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

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class BroadcastStatusTileViewModel : DashboardTileViewModel, IDisposable
{
    private readonly BroadcastScheduler _scheduler;
    private readonly SettingsManager _settings;
    private readonly IBroadcastState _broadcastState;
    private readonly IChatPlatformRegistry _platformRegistry;
    private readonly IBroadcastPlatformTargetsProvider _platformTargetsProvider;
    private readonly IDialogService _dialogService;
    private readonly IUiDispatcher? _uiDispatcher;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly ChannelCatalogStore? _catalogStore;
    private readonly List<IDisposable> _subscriptions = [];

    private readonly ToolbarItemViewModel _startItem;
    private readonly ToolbarItemViewModel _stopItem;
    private readonly ToolbarItemViewModel _modeToggleItem;
    private readonly ToolbarItemViewModel _sendNowItem;

    [ObservableProperty]
    private string _statusText = "Неактивна";

    [ObservableProperty]
    private StatusSeverity _statusSeverity = StatusSeverity.Error;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _modeText = "Ручной";

    [ObservableProperty]
    private string _platformsText = "–";

    [ObservableProperty]
    private bool _hasTargetPlatformChips;

    public ObservableCollection<BroadcastPlatformTargetChipViewModel> TargetPlatformChips { get; } = [];

    [ObservableProperty]
    private int _sentCount;

    [ObservableProperty]
    private string _nextBroadcastText = "–";

    public BroadcastStatusTileViewModel(
        BroadcastScheduler scheduler,
        SettingsManager settings,
        IBroadcastState broadcastState,
        IChatPlatformRegistry platformRegistry,
        IBroadcastPlatformTargetsProvider platformTargetsProvider,
        IEventBus eventBus,
        IDialogService dialogService,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        IUiDispatcher? uiDispatcher = null,
        ChannelCatalogStore? catalogStore = null)
        : base("broadcast-status", "Рассылка", maxWidth: 360, minHeight: 160)
    {
        _scheduler = scheduler;
        _settings = settings;
        _broadcastState = broadcastState;
        _platformRegistry = platformRegistry;
        _platformTargetsProvider = platformTargetsProvider;
        _dialogService = dialogService;
        _youtubeSettingsStore = youtubeSettingsStore;
        _uiDispatcher = uiDispatcher;
        _catalogStore = catalogStore;

        _startItem = new ToolbarItemViewModel(PackIconLucideKind.Play, StartCommand, "Запустить рассылку");
        _stopItem = new ToolbarItemViewModel(PackIconLucideKind.Square, StopCommand, "Остановить рассылку");
        _modeToggleItem = new ToolbarItemViewModel(PackIconLucideKind.Repeat, ModeToggleCommand, "Переключить режим");
        _sendNowItem = new ToolbarItemViewModel(PackIconLucideKind.Send, SendNowCommand, "Отправить сейчас");

        HeaderActions.Add(_startItem);
        HeaderActions.Add(_stopItem);
        HeaderActions.Add(_modeToggleItem);
        HeaderActions.Add(_sendNowItem);

        _subscriptions.Add(eventBus.SubscribeOnUi<BroadcastSchedulerStateChanged>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<BroadcastProfilesChanged>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<StreamWentOnline>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<StreamWentOffline>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<StreamMetadataResolved>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<PlatformConnectionChanged>(_ => RefreshState()));
        _subscriptions.Add(eventBus.SubscribeOnUi<ActiveChannelChanged>(_ => RefreshState()));

        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed += OnYouTubeSettingsChanged;
        }

        if (_catalogStore is not null)
        {
            _catalogStore.Changed += OnCatalogChanged;
        }

        RefreshState();
    }

    private void OnYouTubeSettingsChanged()
    {
        DispatchRefreshState();
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        DispatchRefreshState();
    }

    private void DispatchRefreshState()
    {
        if (_uiDispatcher is not null)
        {
            _uiDispatcher.Invoke(RefreshState);
        }
        else
        {
            RefreshState();
        }
    }

    public override PackIconLucideKind Icon => PackIconLucideKind.Megaphone;

    public override bool SizesToContent => true;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        var channel = ResolvePrimaryChannel();
        if (!string.IsNullOrEmpty(channel))
        {
            _scheduler.Start(channel);
        }

        RefreshState();
    }

    private bool CanStart()
    {
        return !IsActive && !_settings.Current.Twitch.AutoBroadcast.AutoBroadcastEnabled;
    }

    [RelayCommand]
    private void Stop()
    {
        _scheduler.Stop();
        RefreshState();
    }

    [RelayCommand]
    private void ModeToggle()
    {
        var willEnableAuto = !_settings.Current.Twitch.AutoBroadcast.AutoBroadcastEnabled;

        if (willEnableAuto && _scheduler.IsActive && !_broadcastState.IsAnyLive)
        {
            var confirmed = _dialogService.Confirm(
                "Переключение режима",
                "При переключении в автоматический режим активная рассылка будет остановлена, так как стрим сейчас оффлайн.\n\nПродолжить?");

            if (!confirmed)
            {
                return;
            }
        }

        _settings.Mutate(settings => settings.Twitch.AutoBroadcast.AutoBroadcastEnabled = willEnableAuto);

        if (willEnableAuto)
        {
            if (_broadcastState.IsAnyLive)
            {
                var channel = ResolvePrimaryChannel();
                if (!_scheduler.IsActive && !string.IsNullOrEmpty(channel))
                {
                    _scheduler.Start(channel);
                }
            }
            else
            {
                _scheduler.Stop();
            }
        }

        RefreshState();
    }

    [RelayCommand(CanExecute = nameof(CanSendNow))]
    private async Task SendNowAsync()
    {
        _sendNowItem.IsEnabled = false;
        await _scheduler.ManualSendAsync();
        _sendNowItem.IsEnabled = IsActive;
    }

    private bool CanSendNow() => IsActive;

    private void RefreshState()
    {
        var isActive = _scheduler.IsActive;
        var isAuto = _settings.Current.Twitch.AutoBroadcast.AutoBroadcastEnabled;
        var streamOnline = _broadcastState.IsAnyLive;

        IsActive = isActive;
        ModeText = isAuto ? "Авто" : "Ручной";
        var targets = _platformTargetsProvider.GetTargetPlatforms();
        PlatformsText = FormatTargetPlatforms(targets);
        RefreshTargetPlatformChips(targets);
        SentCount = _scheduler.SentMessagesCount;

        var nextTime = _scheduler.NextBroadcastTime;
        NextBroadcastText = nextTime.HasValue ? nextTime.Value.ToString("HH:mm:ss") : "–";

        (StatusText, StatusSeverity) = ResolveStatus(isActive, isAuto, streamOnline);

        _startItem.IsVisible = !isActive;
        _startItem.IsEnabled = !isAuto && !string.IsNullOrEmpty(ResolvePrimaryChannel());
        _stopItem.IsVisible = isActive;
        _sendNowItem.IsEnabled = isActive;

        StartCommand.NotifyCanExecuteChanged();
        SendNowCommand.NotifyCanExecuteChanged();
    }

    private string? ResolvePrimaryChannel()
    {
        var platforms = _platformRegistry.All;
        foreach (var platform in platforms)
        {
            if (_broadcastState.IsPlatformLive(platform.Id)
                && !string.IsNullOrWhiteSpace(platform.ConfiguredChannel))
            {
                return platform.ConfiguredChannel;
            }
        }

        foreach (var platform in platforms)
        {
            if (platform.IsEnabled && !string.IsNullOrWhiteSpace(platform.ConfiguredChannel))
            {
                return platform.ConfiguredChannel;
            }
        }

        foreach (var platform in platforms)
        {
            var configured = platform.ConfiguredChannel;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }
        }

        return null;
    }

    private string FormatTargetPlatforms(IReadOnlySet<StreamingPlatform> targets)
    {
        if (targets.Count == 0)
        {
            return "–";
        }

        var labels = new List<string>(targets.Count);
        foreach (var platform in targets.Order())
        {
            labels.Add(FormatPlatformDisplayName(platform));
        }

        return string.Join(", ", labels);
    }

    private string FormatPlatformDisplayName(StreamingPlatform platform)
    {
        var display = _platformRegistry.GetDisplayName(platform);
        if (_catalogStore is not null && _catalogStore.GetConfiguredChannels(platform).Count > 1)
        {
            var active = _catalogStore.GetActiveChannel(platform)?.DisplayName;
            if (string.IsNullOrWhiteSpace(active))
            {
                active = _platformRegistry.GetConfiguredChannel(platform);
            }

            if (!string.IsNullOrWhiteSpace(active))
            {
                display = $"{display} ({active})";
            }
        }

        return display;
    }

    private void RefreshTargetPlatformChips(IReadOnlySet<StreamingPlatform> targets)
    {
        var nextChips = new List<BroadcastPlatformTargetChipViewModel>(targets.Count);
        foreach (var platform in targets.Order())
        {
            var displayName = FormatPlatformDisplayName(platform);
            var chatPlatform = _platformRegistry.Find(platform);
            var isEnabled = chatPlatform?.IsEnabled ?? false;
            var isOnline = _broadcastState.IsPlatformLive(platform)
                || chatPlatform?.StreamStatus.CurrentStatus == StreamStatus.Online;
            var isConnected = chatPlatform?.IsConnected ?? false;

            var (stateLabel, severity, tooltip) = ResolveTargetPlatformChipState(displayName, isEnabled, isOnline, isConnected);
            nextChips.Add(new BroadcastPlatformTargetChipViewModel(platform, displayName, stateLabel, severity, tooltip));
        }

        var same = TargetPlatformChips.Count == nextChips.Count;
        if (same)
        {
            for (var i = 0; i < nextChips.Count; i++)
            {
                if (TargetPlatformChips[i] != nextChips[i])
                {
                    same = false;
                    break;
                }
            }
        }

        if (!same)
        {
            TargetPlatformChips.Clear();
            foreach (var chip in nextChips)
            {
                TargetPlatformChips.Add(chip);
            }
        }

        HasTargetPlatformChips = TargetPlatformChips.Count > 0;
    }

    private static (string StateLabel, StatusSeverity Severity, string ToolTipText) ResolveTargetPlatformChipState(
        string displayName,
        bool isEnabled,
        bool isOnline,
        bool isConnected)
    {
        if (!isEnabled)
        {
            return ("Выключена", StatusSeverity.Warning, $"{displayName} – площадка выключена, рассылка туда не отправится");
        }

        if (isOnline)
        {
            return isConnected
                ? ("В эфире", StatusSeverity.Success, $"{displayName} – в эфире, чат подключён")
                : ("В эфире", StatusSeverity.Warning, $"{displayName} – в эфире, но чат не подключён");
        }

        return isConnected
            ? ("Подключён", StatusSeverity.Success, $"{displayName} – чат подключён, эфир не идёт")
            : ("Офлайн", StatusSeverity.None, $"{displayName} – офлайн");
    }

    private static (string text, StatusSeverity severity) ResolveStatus(bool isActive, bool isAuto, bool streamOnline)
    {
        if (isActive)
        {
            return ("Активна", StatusSeverity.Success);
        }

        if (isAuto && !streamOnline)
        {
            return ("Ожидание стрима", StatusSeverity.Warning);
        }

        return ("Неактивна", StatusSeverity.Error);
    }

    public void Dispose()
    {
        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed -= OnYouTubeSettingsChanged;
        }

        if (_catalogStore is not null)
        {
            _catalogStore.Changed -= OnCatalogChanged;
        }

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }

        _subscriptions.Clear();
    }
}

public sealed record BroadcastPlatformTargetChipViewModel(
    StreamingPlatform Platform,
    string DisplayName,
    string StateLabel,
    StatusSeverity Severity,
    string ToolTipText);
