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
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;

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
    private string _platformsText = "Twitch";

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
        IUiDispatcher? uiDispatcher = null)
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

        if (_youtubeSettingsStore is not null)
        {
            _youtubeSettingsStore.Changed += OnYouTubeSettingsChanged;
        }

        RefreshState();
    }

    private void OnYouTubeSettingsChanged()
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
        PlatformsText = FormatTargetPlatforms(_platformTargetsProvider.GetTargetPlatforms());
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
        foreach (var platformId in PlatformVisuals.OrderedPlatforms)
        {
            if (!_broadcastState.IsPlatformLive(platformId))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(_platformRegistry.GetConfiguredChannel(platformId)))
            {
                return _platformRegistry.GetConfiguredChannel(platformId);
            }
        }

        foreach (var platformId in PlatformVisuals.OrderedPlatforms)
        {
            if (_platformRegistry.TryGet(platformId, out var platform)
                && platform.IsEnabled
                && !string.IsNullOrWhiteSpace(platform.ConfiguredChannel))
            {
                return platform.ConfiguredChannel;
            }
        }

        foreach (var platformId in PlatformVisuals.OrderedPlatforms)
        {
            var configured = _platformRegistry.GetConfiguredChannel(platformId);
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured;
            }
        }

        return null;
    }

    private static string FormatTargetPlatforms(IReadOnlySet<StreamingPlatform> targets)
    {
        if (targets.Count == 0)
        {
            return "–";
        }

        var labels = new List<string>(targets.Count);
        foreach (var platform in PlatformVisuals.OrderedPlatforms)
        {
            if (targets.Contains(platform))
            {
                labels.Add(PlatformVisuals.GetDisplayName(platform));
            }
        }

        return labels.Count > 0 ? string.Join(", ", labels) : "–";
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

        foreach (var sub in _subscriptions)
        {
            sub.Dispose();
        }

        _subscriptions.Clear();
    }
}
