using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Collections.ObjectModel;
using System.Net.Http;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class BotConnectionPageViewModel : OnboardingPageViewModelBase, IDisposable
{
    private readonly IBotConnectionController _botConnectionManager;
    private readonly IEventBus _eventBus;
    private readonly SettingsManager _settingsManager;
    private readonly AccountsStore _accountsStore;
    private readonly YouTubeSettingsStore? _youtubeSettingsStore;
    private readonly YouTubeAccountsStore? _youtubeAccountsStore;
    private readonly IChatPlatformRegistry? _platformRegistry;
    private readonly ILogger<BotConnectionPageViewModel> _logger;
    private readonly List<IDisposable> _subs = [];
    private readonly Dictionary<StreamingPlatform, string> _joinedChannels = [];

    private OnboardingContext? _context;
    private bool _setupSaved;
    private string _lastStatusMessage = string.Empty;

    [ObservableProperty]
    private BotLifecyclePhase _phase = BotLifecyclePhase.Idle;

    [ObservableProperty]
    private StatusSeverity _headerSeverity = StatusSeverity.None;

    [ObservableProperty]
    private string _statusText = "Готовимся к подключению…";

    [ObservableProperty]
    private string _detailsText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool _showRetry;

    public BotConnectionPageViewModel(
        IBotConnectionController botConnectionManager,
        IEventBus eventBus,
        SettingsManager settingsManager,
        AccountsStore accountsStore,
        ILogger<BotConnectionPageViewModel> logger,
        YouTubeSettingsStore? youtubeSettingsStore = null,
        YouTubeAccountsStore? youtubeAccountsStore = null,
        IChatPlatformRegistry? platformRegistry = null)
    {
        _botConnectionManager = botConnectionManager;
        _eventBus = eventBus;
        _settingsManager = settingsManager;
        _accountsStore = accountsStore;
        _youtubeSettingsStore = youtubeSettingsStore;
        _youtubeAccountsStore = youtubeAccountsStore;
        _platformRegistry = platformRegistry;
        _logger = logger;

        _subs.Add(eventBus.SubscribeOnUi<BotLifecyclePhaseChanged>(OnPhaseChanged));
        _subs.Add(eventBus.SubscribeOnUi<PlatformConnectionChanged>(OnPlatformConnectionChanged));
        _subs.Add(eventBus.SubscribeOnUi<BotConnectionStatusUpdated>(OnStatusUpdated));
        _subs.Add(eventBus.SubscribeOnUi<BotJoinedChannel>(OnJoinedChannel));
    }

    public override string PageTitle => "Подключение бота";

    public ObservableCollection<PlatformConnectionRow> PlatformStatuses { get; } = [];

    public override void OnEnter(OnboardingContext context)
    {
        _context = context;
        _setupSaved = false;
        _joinedChannels.Clear();
        PopulatePlatformRows(context);
        Phase = _botConnectionManager.CurrentPhase;
        CanAdvance = false;

        try
        {
            if (context.SaveSetup(_settingsManager, _accountsStore, _youtubeSettingsStore, _youtubeAccountsStore) is { } error)
            {
                _logger.BotConnectionSetupNotSaved(error);
                ApplyPhase(BotLifecyclePhase.Failed, error);
                StatusText = "Подключение не запущено";
                ShowRetry = false;
                return;
            }
        }
        catch (Exception exception)
        {
            _logger.BotConnectionSaveSettingsFailed(exception);
            ApplyPhase(BotLifecyclePhase.Failed, context.HasIncompleteRollback
                ? OnboardingContext.RollbackIncomplete
                : "Не удалось сохранить настройки. Повторите попытку.");
            StatusText = "Подключение не запущено";
            ShowRetry = !context.HasIncompleteRollback;
            return;
        }

        _setupSaved = true;
        _ = _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Bot,
            context.TwitchContextId(PlatformAuthRole.Bot)));
        _ = _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.Twitch, PlatformAuthRole.Broadcaster,
            context.TwitchContextId(PlatformAuthRole.Broadcaster)));
        _ = _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Bot));
        _ = _eventBus.PublishAsync(new PlatformAuthorizationRefreshed(StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster));

        SyncRowsFromController();
        RefreshAggregateFromRows();

        if (_platformRegistry is not null)
        {
            StartMissingEnabledPlatforms();
            return;
        }

        if (_botConnectionManager.IsBusy || Phase == BotLifecyclePhase.Connected)
        {
            return;
        }

        TryStartConnection();
    }

    private void StartMissingEnabledPlatforms()
    {
        if (_platformRegistry is null)
        {
            return;
        }

        foreach (var platform in _platformRegistry.All)
        {
            if (!platform.IsEnabled || _botConnectionManager.IsPlatformBusy(platform.Id))
            {
                continue;
            }

            if (_botConnectionManager.GetPhase(platform.Id) == BotLifecyclePhase.Connected)
            {
                continue;
            }

            try
            {
                _botConnectionManager.StartConnection(platform.Id);
                if (PlatformStatuses.FirstOrDefault(r => r.Platform == platform.Id) is { } row)
                {
                    UpdateRow(row, BotLifecyclePhase.Connecting, null);
                }
            }
            catch (InvalidOperationException exception)
            {
                _logger.BotConnectionStartRejected(exception);
                if (PlatformStatuses.FirstOrDefault(r => r.Platform == platform.Id) is { } row)
                {
                    UpdateRow(row, BotLifecyclePhase.Failed, "Не удалось запустить подключение. Повторите попытку.");
                }
            }
        }

        RefreshAggregateFromRows();
    }

    private void RetryPlatform(StreamingPlatform platform)
    {
        if (!_setupSaved || _context?.CancellationStarted == true)
        {
            return;
        }

        if (_botConnectionManager.IsPlatformBusy(platform) || _botConnectionManager.GetPhase(platform) == BotLifecyclePhase.Connected)
        {
            return;
        }

        try
        {
            if (_platformRegistry is null && PlatformStatuses.Count <= 1)
            {
                _botConnectionManager.StartConnection();
            }
            else
            {
                _botConnectionManager.StartConnection(platform);
            }
        }
        catch (InvalidOperationException exception)
        {
            _logger.BotConnectionStartRejected(exception);
            if (PlatformStatuses.FirstOrDefault(r => r.Platform == platform) is { } failedRow)
            {
                UpdateRow(failedRow, BotLifecyclePhase.Failed, "Не удалось запустить подключение. Повторите попытку.");
                RefreshAggregateFromRows();
            }

            return;
        }

        if (PlatformStatuses.FirstOrDefault(r => r.Platform == platform) is { } row)
        {
            UpdateRow(row, BotLifecyclePhase.Connecting, null);
        }

        RefreshAggregateFromRows();
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private void Retry()
    {
        if (!CanRetry())
        {
            return;
        }

        ShowRetry = false;
        if (!_setupSaved && _context is { } context)
        {
            OnEnter(context);
            return;
        }

        if (_platformRegistry is not null)
        {
            StartMissingEnabledPlatforms();
            return;
        }

        TryStartConnection();
    }

    private bool CanRetry() => ShowRetry && _context?.CancellationStarted == false;

    private void OnPhaseChanged(BotLifecyclePhaseChanged @event)
    {
        if (!_setupSaved)
        {
            return;
        }

        Phase = @event.Phase;

        if (@event.Phase == BotLifecyclePhase.Failed && @event.Exception is not null)
        {
            _lastStatusMessage = SafeFailureMessage(@event.Exception);
        }

        if (PlatformStatuses.Count <= 1)
        {
            foreach (var row in PlatformStatuses)
            {
                UpdateRow(row, @event.Phase, @event.Exception is not null ? SafeFailureMessage(@event.Exception) : null);
            }
        }

        RefreshAggregateFromRows(@event.Phase);
    }

    private void OnPlatformConnectionChanged(PlatformConnectionChanged @event)
    {
        if (!_setupSaved)
        {
            return;
        }

        var errorDetails = @event.Phase == BotLifecyclePhase.Failed && @event.Exception is not null
            ? SafeFailureMessage(@event.Exception)
            : null;
        if (errorDetails is not null)
        {
            _lastStatusMessage = errorDetails;
        }

        var row = PlatformStatuses.FirstOrDefault(r => r.Platform == @event.Platform);
        if (row is not null)
        {
            UpdateRow(row, @event.Phase, errorDetails);
        }

        RefreshAggregateFromRows(@event.Phase);
    }

    private void OnStatusUpdated(BotConnectionStatusUpdated @event)
    {
        _lastStatusMessage = @event.Message;
        if (_setupSaved
            && _context?.CancellationStarted == false
            && Phase is BotLifecyclePhase.Idle or BotLifecyclePhase.Connecting or BotLifecyclePhase.Disconnecting)
        {
            DetailsText = @event.Message;
        }
    }

    private void OnJoinedChannel(BotJoinedChannel @event)
    {
        _joinedChannels[@event.Platform] = @event.Channel;
        if (!_setupSaved)
        {
            return;
        }

        var row = PlatformStatuses.FirstOrDefault(r => r.Platform == @event.Platform);
        if (row is not null)
        {
            UpdateRow(row, BotLifecyclePhase.Connected, null);
        }

        RefreshAggregateFromRows();
    }

    private void PopulatePlatformRows(OnboardingContext context)
    {
        PlatformStatuses.Clear();

        if (context.Settings.Twitch.Enabled)
        {
            PlatformStatuses.Add(new PlatformConnectionRow(StreamingPlatform.Twitch, PlatformVisuals.GetDisplayName(StreamingPlatform.Twitch), RetryPlatform));
        }

        if (context.YouTubeSettings.Enabled)
        {
            PlatformStatuses.Add(new PlatformConnectionRow(StreamingPlatform.YouTube, PlatformVisuals.GetDisplayName(StreamingPlatform.YouTube), RetryPlatform));
        }
    }

    private void SyncRowsFromController()
    {
        foreach (var row in PlatformStatuses)
        {
            var phase = _platformRegistry is not null
                ? _botConnectionManager.GetPhase(row.Platform)
                : _botConnectionManager.CurrentPhase;
            UpdateRow(row, phase, null);
        }
    }

    private void UpdateRow(PlatformConnectionRow row, BotLifecyclePhase phase, string? errorDetails)
    {
        row.Phase = phase;
        row.StatusText = phase switch
        {
            BotLifecyclePhase.Connecting => "Подключение…",
            BotLifecyclePhase.Connected => FormatConnectedPlatformStatus(row.Platform),
            BotLifecyclePhase.Disconnecting => "Отключение…",
            BotLifecyclePhase.Disconnected => "Отключён",
            BotLifecyclePhase.Cancelled => "Отменено",
            BotLifecyclePhase.Failed => "Ошибка подключения",
            _ => "Ожидание…",
        };
        row.DetailsText = phase == BotLifecyclePhase.Failed
            ? (errorDetails ?? string.Empty)
            : string.Empty;
        row.ShowRetry = _setupSaved
            && _context?.CancellationStarted != true
            && PlatformStatuses.Count > 1
            && phase is BotLifecyclePhase.Failed or BotLifecyclePhase.Cancelled or BotLifecyclePhase.Disconnected;
    }

    private string FormatConnectedPlatformStatus(StreamingPlatform platform)
    {
        var channel = _joinedChannels.TryGetValue(platform, out var joined) && !string.IsNullOrWhiteSpace(joined)
            ? joined
            : platform switch
            {
                StreamingPlatform.Twitch => _context?.Settings.Twitch.Channel ?? string.Empty,
                StreamingPlatform.YouTube => _context?.YouTubeSettings.Channel ?? string.Empty,
                _ => string.Empty,
            };
        return string.IsNullOrWhiteSpace(channel) ? "Подключён" : $"Подключён ({channel})";
    }

    private void RefreshAggregateFromRows(BotLifecyclePhase? fallbackPhase = null)
    {
        if (_context?.CancellationStarted == true)
        {
            foreach (var row in PlatformStatuses)
            {
                row.ShowRetry = false;
            }

            ApplyPhase(fallbackPhase ?? Phase);
            return;
        }

        if (PlatformStatuses.Count == 0)
        {
            ApplyPhase(fallbackPhase ?? _botConnectionManager.CurrentPhase);
            return;
        }

        if (PlatformStatuses.All(r => r.Phase == BotLifecyclePhase.Connected))
        {
            ApplyPhase(BotLifecyclePhase.Connected);
            return;
        }

        if (PlatformStatuses.Any(r => r.Phase == BotLifecyclePhase.Connecting))
        {
            ApplyPhase(BotLifecyclePhase.Connecting);
            return;
        }

        if (PlatformStatuses.Any(r => r.Phase == BotLifecyclePhase.Disconnecting))
        {
            ApplyPhase(BotLifecyclePhase.Disconnecting);
            return;
        }

        if (PlatformStatuses.Any(r => r.Phase == BotLifecyclePhase.Idle))
        {
            ApplyPhase(PlatformStatuses.All(r => r.Phase == BotLifecyclePhase.Idle)
                ? BotLifecyclePhase.Idle
                : BotLifecyclePhase.Connecting);
            return;
        }

        var connectedCount = PlatformStatuses.Count(r => r.Phase == BotLifecyclePhase.Connected);
        if (connectedCount > 0 && connectedCount < PlatformStatuses.Count)
        {
            Phase = PlatformStatuses.Any(r => r.Phase == BotLifecyclePhase.Failed)
                ? BotLifecyclePhase.Failed
                : BotLifecyclePhase.Cancelled;
            HeaderSeverity = StatusSeverity.Warning;
            StatusText = connectedCount == 1
                ? $"Подключена 1 из {PlatformStatuses.Count} площадок"
                : $"Подключены {connectedCount} из {PlatformStatuses.Count} площадок";
            DetailsText = "Можно повторить подключение для отказавшей площадки или продолжить настройку.";
            ShowRetry = false;
            CanAdvance = true;
            return;
        }

        if (PlatformStatuses.Any(r => r.Phase == BotLifecyclePhase.Failed))
        {
            ApplyPhase(BotLifecyclePhase.Failed);
            return;
        }

        if (PlatformStatuses.Any(r => r.Phase == BotLifecyclePhase.Cancelled))
        {
            ApplyPhase(BotLifecyclePhase.Cancelled);
            return;
        }

        if (PlatformStatuses.Any(r => r.Phase == BotLifecyclePhase.Disconnected))
        {
            ApplyPhase(BotLifecyclePhase.Disconnected);
            return;
        }

        ApplyPhase(BotLifecyclePhase.Idle);
    }

    private void ApplyPhase(BotLifecyclePhase phase, string? overrideDetails = null)
    {
        Phase = phase;
        if (_context?.CancellationStarted == true)
        {
            HeaderSeverity = StatusSeverity.Warning;
            StatusText = "Настройка отменяется";
            DetailsText = "Закройте мастер, чтобы завершить восстановление настроек.";
            ShowRetry = false;
            CanAdvance = false;
            return;
        }

        var singleOrUnsavedRetry = !_setupSaved || PlatformStatuses.Count <= 1;
        var finishedGuidance = PlatformStatuses.Count > 1
            ? "Можно повторить подключение нужной площадки или продолжить настройку."
            : "Можно повторить подключение или продолжить настройку.";

        switch (phase)
        {
            case BotLifecyclePhase.Connecting:
                HeaderSeverity = StatusSeverity.Info;
                StatusText = "Подключение…";
                DetailsText = overrideDetails ?? _lastStatusMessage;
                ShowRetry = false;
                CanAdvance = false;
                break;

            case BotLifecyclePhase.Connected:
                HeaderSeverity = StatusSeverity.Success;
                StatusText = PlatformStatuses.Count == 1
                    ? $"{PlatformStatuses[0].PlatformName}: {PlatformStatuses[0].StatusText.ToLowerInvariant()}"
                    : "Бот подключён ко всем выбранным площадкам";
                DetailsText = "Можно идти дальше.";
                ShowRetry = false;
                CanAdvance = true;
                break;

            case BotLifecyclePhase.Disconnecting:
                HeaderSeverity = StatusSeverity.Warning;
                StatusText = "Отключение…";
                DetailsText = overrideDetails ?? _lastStatusMessage;
                ShowRetry = false;
                CanAdvance = false;
                break;

            case BotLifecyclePhase.Disconnected:
                HeaderSeverity = StatusSeverity.None;
                StatusText = "Бот отключён";
                DetailsText = _setupSaved && PlatformStatuses.Count > 0
                    ? finishedGuidance
                    : "Нажмите «Повторить подключение».";
                ShowRetry = singleOrUnsavedRetry;
                CanAdvance = _setupSaved;
                break;

            case BotLifecyclePhase.Cancelled:
                HeaderSeverity = StatusSeverity.Warning;
                StatusText = "Подключение отменено";
                DetailsText = _setupSaved && PlatformStatuses.Count > 0
                    ? finishedGuidance
                    : "Нажмите «Повторить подключение».";
                ShowRetry = singleOrUnsavedRetry;
                CanAdvance = _setupSaved;
                break;

            case BotLifecyclePhase.Failed:
                HeaderSeverity = StatusSeverity.Error;
                StatusText = "Не удалось подключиться";
                DetailsText = _setupSaved && PlatformStatuses.Count > 0
                    ? finishedGuidance
                    : (overrideDetails ?? _lastStatusMessage);
                ShowRetry = singleOrUnsavedRetry;
                CanAdvance = _setupSaved;
                break;

            default:
                HeaderSeverity = StatusSeverity.None;
                StatusText = "Готовимся к подключению…";
                DetailsText = overrideDetails ?? _lastStatusMessage;
                ShowRetry = false;
                CanAdvance = false;
                break;
        }
    }

    private void TryStartConnection()
    {
        try
        {
            _botConnectionManager.StartConnection();
        }
        catch (InvalidOperationException exception)
        {
            _logger.BotConnectionStartRejected(exception);
            const string message = "Не удалось запустить подключение. Повторите попытку.";
            _lastStatusMessage = message;
            foreach (var row in PlatformStatuses)
            {
                UpdateRow(row, BotLifecyclePhase.Failed, message);
            }

            RefreshAggregateFromRows(BotLifecyclePhase.Failed);
        }
    }

    private static string SafeFailureMessage(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "Подключение отменено",
            HttpRequestException => "Ошибка сети при подключении",
            InvalidOperationException invalid => invalid.Message,
            _ => "Неизвестная ошибка подключения",
        };
    }

    public void Dispose()
    {
        foreach (var sub in _subs)
        {
            sub.Dispose();
        }
    }

    public sealed partial class PlatformConnectionRow(
        StreamingPlatform platform,
        string platformName,
        Action<StreamingPlatform> retryAction) : ObservableObject
    {
        public StreamingPlatform Platform { get; } = platform;

        public string PlatformName { get; } = platformName;

        public string RetryButtonText => $"Повторить для {PlatformName}";

        [ObservableProperty]
        private BotLifecyclePhase _phase = BotLifecyclePhase.Idle;

        [ObservableProperty]
        private string _statusText = "Ожидание…";

        [ObservableProperty]
        private string _detailsText = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
        private bool _showRetry;

        [RelayCommand(CanExecute = nameof(CanRetryRow))]
        private void Retry() => retryAction(Platform);

        private bool CanRetryRow() => ShowRetry;
    }
}
