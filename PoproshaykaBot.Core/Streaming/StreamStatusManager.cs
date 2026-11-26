using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Streaming;

public sealed class StreamStatusManager : IStreamStatus, IPlatformChannelMonitor, IAsyncDisposable
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan RefreshGateDrainTimeout = TimeSpan.FromSeconds(2);

    private readonly ITwitchEventSubClient _eventSubClient;
    private readonly ITwitchHelixClient _helix;
    private readonly IBroadcasterIdProvider _broadcasterIdProvider;
    private readonly ITargetChannelProvider _targetChannelProvider;
    private readonly SettingsManager _settingsManager;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StreamStatusManager> _logger;
    private readonly ChannelCatalogStore? _channelCatalogStore;
    private readonly PlatformChannelRegistry? _channelRegistry;
    private readonly IPlatformAuthService? _authService;

    private readonly Lock _slotsLock = new();
    private readonly Dictionary<string, TwitchChannelSlot> _slots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _subscriptionToChannelId = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _subscriptionsGate = new(1, 1);
    private readonly EventSubSubscriptionLedger _ledger;
    private readonly EventSubSubscriptionRetry _retry;

    private string? _activeSlotId;
    private string _sessionBotContextId = AuthContextIds.Default;
    private string[] _lastSubscribedChannelIds = [];
    private IDisposable? _channelUpdatedSubscription;
    private IDisposable? _activeChannelChangedSubscription;
    private CancellationTokenSource? _runCts;
    private bool _subscribed;
    private bool _disposed;

    public StreamStatusManager(
        [FromKeyedServices(TwitchEndpoints.EventSubBotSession)]
        ITwitchEventSubClient eventSubClient,
        [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
        ITwitchHelixClient helix,
        IBroadcasterIdProvider broadcasterIdProvider,
        ITargetChannelProvider targetChannelProvider,
        SettingsManager settingsManager,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<StreamStatusManager> logger,
        ChannelCatalogStore? channelCatalogStore = null,
        PlatformChannelRegistry? channelRegistry = null,
        [FromKeyedServices(StreamingPlatform.Twitch)] IPlatformAuthService? authService = null)
    {
        _eventSubClient = eventSubClient;
        _helix = helix;
        _broadcasterIdProvider = broadcasterIdProvider;
        _targetChannelProvider = targetChannelProvider;
        _settingsManager = settingsManager;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
        _channelCatalogStore = channelCatalogStore;
        _channelRegistry = channelRegistry;
        _authService = authService;
        _ledger = new(helix, logger);
        _retry = new(helix, eventSubClient, timeProvider, logger);
    }

    public StreamingPlatform Platform => StreamingPlatform.Twitch;

    public StreamStatus CurrentStatus => GetActiveSlot()?.State.CurrentStatus ?? StreamStatus.Unknown;

    public StreamInfo? CurrentStream => GetActiveSlot()?.State.CurrentStream;

    public StreamStatusSnapshot GetSnapshot() =>
        GetActiveSlot()?.State.GetSnapshot() ?? new StreamStatusSnapshot(StreamStatus.Unknown, null, null);

    public bool RequiresBackgroundPoll
    {
        get
        {
            lock (_slotsLock)
            {
                return _slots.Values.Any(static s => s.UsesPollingFallback);
            }
        }
    }

    public ChannelMonitoringSnapshot GetSnapshot(ChannelKey channel)
    {
        if (channel.Platform != StreamingPlatform.Twitch)
        {
            return new(channel, channel.ChannelId, ChannelMonitoringAvailability.Unavailable, MonitoringUnavailableReason.NotSupportedByPlatform, "Неверный тип площадки", StreamStatus.Unknown, null, null, false);
        }

        lock (_slotsLock)
        {
            if (_slots.TryGetValue(channel.ChannelId, out var slot))
            {
                return slot.ToMonitoringSnapshot();
            }
        }

        var entry = _channelCatalogStore?.FindEntry(channel);
        var displayName = entry is null ? channel.ChannelId : ResolveEntryLabel(entry);
        return new(
            channel,
            displayName,
            entry is { MonitoringEnabled: true } ? ChannelMonitoringAvailability.Unavailable : ChannelMonitoringAvailability.Disabled,
            entry is { MonitoringEnabled: true } ? MonitoringUnavailableReason.TransportDisconnected : MonitoringUnavailableReason.MonitoringDisabled,
            entry is { MonitoringEnabled: true } ? "Мониторинг не запущен" : "Мониторинг канала выключен",
            StreamStatus.Unknown,
            null,
            null,
            false);
    }

    public IReadOnlyList<ChannelMonitoringSnapshot> GetSnapshots()
    {
        lock (_slotsLock)
        {
            return _slots.Values.Select(static s => s.ToMonitoringSnapshot()).ToArray();
        }
    }

    public string Name => "Отслеживание статуса стрима";

    public int StartOrder => 256;

    internal Task? MetadataRetryTask => GetActiveSlot()?.MetadataRetryLoop.CurrentTask;

    private TimeSpan StuckOnlineThreshold =>
        TimeSpan.FromSeconds(_settingsManager.Current.Twitch.Infrastructure.StreamStuckOnlineThresholdSeconds);

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            _logger.LogDebug("StreamStatusManager.StartAsync проигнорирован: подписки уже установлены");
            return Task.CompletedTask;
        }

        _runCts = new();

        _eventSubClient.OnSessionWelcome += OnSessionWelcomeAsync;
        _eventSubClient.OnNotification += OnNotificationAsync;
        _eventSubClient.OnSessionReconnect += OnSessionReconnectAsync;
        _eventSubClient.OnRevocation += OnRevocationAsync;
        _eventSubClient.OnDisconnected += OnDisconnectedAsync;

        _channelUpdatedSubscription = _eventBus.SubscribePlatform<ChannelUpdated>(StreamingPlatform.Twitch, OnChannelUpdated);
        _activeChannelChangedSubscription = _eventBus.Subscribe<ActiveChannelChanged>(OnActiveChannelChanged);
        if (_channelCatalogStore is not null)
        {
            _channelCatalogStore.Changed += OnCatalogChanged;
        }

        _subscribed = true;

        _logger.LogInformation("StreamStatusManager: подписки на EventSub установлены");
        return Task.CompletedTask;
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (!_subscribed)
        {
            _logger.LogDebug("StreamStatusManager.StopAsync проигнорирован: подписки уже сняты");
            return;
        }

        _eventSubClient.OnSessionWelcome -= OnSessionWelcomeAsync;
        _eventSubClient.OnNotification -= OnNotificationAsync;
        _eventSubClient.OnSessionReconnect -= OnSessionReconnectAsync;
        _eventSubClient.OnRevocation -= OnRevocationAsync;
        _eventSubClient.OnDisconnected -= OnDisconnectedAsync;

        _channelUpdatedSubscription?.Dispose();
        _channelUpdatedSubscription = null;
        _activeChannelChangedSubscription?.Dispose();
        _activeChannelChangedSubscription = null;
        if (_channelCatalogStore is not null)
        {
            _channelCatalogStore.Changed -= OnCatalogChanged;
        }

        _subscribed = false;

        if (_runCts != null)
        {
            await _runCts.CancelAsync().ConfigureAwait(false);
        }

        TwitchChannelSlot[] slotsCopy;
        lock (_slotsLock)
        {
            slotsCopy = [.. _slots.Values];
        }

        foreach (var slot in slotsCopy)
        {
            await slot.MetadataRetryLoop.CancelAndDrainAsync().ConfigureAwait(false);
            slot.State.ResetToUnknown();
            slot.Availability = ChannelMonitoringAvailability.Disabled;
            slot.Reason = MonitoringUnavailableReason.TransportDisconnected;
        }

        _runCts?.Dispose();
        _runCts = null;

        await _retry.CancelAndDrainAsync(cancellationToken).ConfigureAwait(false);
        await _ledger.CloseAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("StreamStatusManager: подписки на EventSub сняты, состояние сброшено в Unknown");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Уничтожение ресурсов StreamStatusManager");
        _disposed = true;

        await _disposeCts.CancelAsync();

        if (_runCts != null)
        {
            await _runCts.CancelAsync();
        }

        _retry.Cancel();

        TwitchChannelSlot[] slotsCopy;
        lock (_slotsLock)
        {
            slotsCopy = [.. _slots.Values];
        }

        foreach (var slot in slotsCopy)
        {
            await slot.MetadataRetryLoop.DisposeAsync().ConfigureAwait(false);
        }

        if (await _refreshGate.WaitAsync(RefreshGateDrainTimeout, CancellationToken.None).ConfigureAwait(false))
        {
            _refreshGate.Dispose();
        }
        else
        {
            _logger.LogDebug("Опрос Helix не завершился за {Timeout} – семафор опроса оставлен сборщику мусора", RefreshGateDrainTimeout);
        }

        if (await _subscriptionsGate.WaitAsync(RefreshGateDrainTimeout, CancellationToken.None).ConfigureAwait(false))
        {
            _subscriptionsGate.Dispose();
        }
        else
        {
            _logger.LogDebug("Синхронизация подписок EventSub не завершилась за {Timeout} – семафор оставлен сборщику мусора", RefreshGateDrainTimeout);
        }

        _disposeCts.Dispose();
        _runCts?.Dispose();
        _runCts = null;

        if (_subscribed)
        {
            _eventSubClient.OnSessionWelcome -= OnSessionWelcomeAsync;
            _eventSubClient.OnNotification -= OnNotificationAsync;
            _eventSubClient.OnSessionReconnect -= OnSessionReconnectAsync;
            _eventSubClient.OnRevocation -= OnRevocationAsync;
            _eventSubClient.OnDisconnected -= OnDisconnectedAsync;

            _channelUpdatedSubscription?.Dispose();
            _channelUpdatedSubscription = null;
            _activeChannelChangedSubscription?.Dispose();
            _activeChannelChangedSubscription = null;
            if (_channelCatalogStore is not null)
            {
                _channelCatalogStore.Changed -= OnCatalogChanged;
            }

            _subscribed = false;
        }
    }

    public Task RefreshLiveSnapshotAsync()
    {
        return RefreshAsync(null, _disposeCts.Token);
    }

    public async Task RefreshAsync(ChannelKey? channel = null, CancellationToken cancellationToken = default)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        var token = linkedCts.Token;

        try
        {
            await _refreshGate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Live-snapshot отменён в очереди: StreamStatusManager уничтожается");
            return;
        }

        try
        {
            var slots = await EnsureMonitoredSlotsAsync(token).ConfigureAwait(false);
            if (channel is { Platform: StreamingPlatform.Twitch } targetKey)
            {
                slots = slots.Where(s => string.Equals(s.ChannelId, targetKey.ChannelId, StringComparison.Ordinal)).ToArray();
            }

            if (slots.Count == 0)
            {
                _logger.LogDebug("Live-snapshot пропущен: BroadcasterId недоступен");
                return;
            }

            var (streamsByUserId, failedAuthChannelIds) = await FetchStreamsMapAsync(slots, token).ConfigureAwait(false);

            foreach (var slot in slots)
            {
                var previousSnapshot = slot.ToMonitoringSnapshot();
                if (failedAuthChannelIds.Contains(slot.ChannelId))
                {
                    await MarkSlotMissingBotAuthAsync(slot, previousSnapshot).ConfigureAwait(false);
                    continue;
                }

                streamsByUserId.TryGetValue(slot.ChannelId, out var stream);

                if (stream != null)
                {
                    var transition = slot.State.ApplyOnlineSnapshot(StreamInfoMapper.MapFromHelix(stream));
                    slot.LastObservedAtUtc = _timeProvider.GetUtcNow();
                    if (slot.Availability != ChannelMonitoringAvailability.Degraded
                        && slot.Reason != MonitoringUnavailableReason.MissingBotAuth)
                    {
                        slot.Availability = ChannelMonitoringAvailability.Active;
                        slot.Reason = MonitoringUnavailableReason.None;
                        slot.Detail = null;
                    }

                    var isInitialBaseline = !slot.BaselinePublished;
                    slot.BaselinePublished = true;
                    var currentSnapshot = slot.ToMonitoringSnapshot(isInitialBaseline);

                    if (HasMonitoringStateChanged(previousSnapshot.Availability, previousSnapshot.Reason, previousSnapshot.Detail,
                            currentSnapshot.Availability, currentSnapshot.Reason, currentSnapshot.Detail))
                    {
                        await _eventBus.PublishAsync(
                            new ChannelMonitoringStateChanged(StreamingPlatform.Twitch, new ChannelKey(StreamingPlatform.Twitch, slot.ChannelId), currentSnapshot),
                            CancellationToken.None).ConfigureAwait(false);
                    }

                    if (transition.Transitioned)
                    {
                        _logger.LogInformation("Live-snapshot: переход {OldStatus} → Online для BroadcasterId {BroadcasterId}, стрим {StreamId}, начат {StartedAt}",
                            transition.Previous,
                            slot.ChannelId,
                            stream.Id,
                            stream.StartedAt);

                        await PublishSlotObservedAsync(previousSnapshot, currentSnapshot, isCatchUp: true).ConfigureAwait(false);
                        if (IsActiveSlot(slot.ChannelId))
                        {
                            await PublishStatusTransitionAsync(slot, StreamStatus.Online, true).ConfigureAwait(false);
                        }
                    }
                    else if (transition.SnapshotChanged)
                    {
                        _logger.LogInformation("Live-snapshot: метаданные стрима {StreamId} обновлены без смены статуса", stream.Id);

                        await PublishSlotObservedAsync(previousSnapshot, currentSnapshot, isCatchUp: false).ConfigureAwait(false);
                        if (IsActiveSlot(slot.ChannelId))
                        {
                            await PublishMetadataResolvedAsync(slot).ConfigureAwait(false);
                        }
                    }

                    if (IsActiveSlot(slot.ChannelId))
                    {
                        await PublishViewerSampleObservedAsync(slot, stream).ConfigureAwait(false);
                    }

                    continue;
                }

                var probe = slot.State.ProbeOfflineDivergence(_timeProvider.GetUtcNow().UtcDateTime, StuckOnlineThreshold);
                switch (probe.Action)
                {
                    case OfflineProbeAction.ForcedOffline:
                    {
                        _logger.LogWarning("Принудительный перевод в Offline после {ElapsedSeconds} с расхождения с API для BroadcasterId {BroadcasterId}, стрим {StreamId}",
                            (int)probe.Elapsed.TotalSeconds, slot.ChannelId, slot.State.CurrentStream?.Id);

                        slot.LastObservedAtUtc = _timeProvider.GetUtcNow();
                        var isInitialBaseline = !slot.BaselinePublished;
                        slot.BaselinePublished = true;
                        var currentSnapshot = slot.ToMonitoringSnapshot(isInitialBaseline);
                        await PublishSlotObservedAsync(previousSnapshot, currentSnapshot, isCatchUp: false).ConfigureAwait(false);
                        if (IsActiveSlot(slot.ChannelId))
                        {
                            await PublishStatusTransitionAsync(slot, StreamStatus.Offline).ConfigureAwait(false);
                        }

                        break;
                    }

                    case OfflineProbeAction.Pending:
                        _logger.LogWarning("API сообщает офлайн при локальном Online, ждём подтверждения (расхождение {Elapsed}/{Threshold}), стрим {StreamId}",
                            probe.Elapsed, StuckOnlineThreshold, slot.State.CurrentStream?.Id);
                        break;

                    default:
                        if (!slot.BaselinePublished && slot.State.CurrentStatus == StreamStatus.Unknown)
                        {
                            var offlineTransition = slot.State.ApplyOffline();
                            slot.LastObservedAtUtc = _timeProvider.GetUtcNow();
                            slot.BaselinePublished = true;
                            var currentSnapshot = slot.ToMonitoringSnapshot(isInitialBaseline: true);
                            if (offlineTransition.Transitioned)
                            {
                                await PublishSlotObservedAsync(previousSnapshot, currentSnapshot, isCatchUp: true).ConfigureAwait(false);
                            }
                        }

                        break;
                }
            }
        }
        catch (OperationCanceledException ex) when (_disposeCts.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Live-snapshot отменён");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обновить live-snapshot стрима: {Reason}", StreamingErrorMessages.SafeMessage(ex));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task OnSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventSub WebSocket подключен. SessionId: {SessionId}", args.SessionId);

        _retry.Cancel();
        lock (_slotsLock)
        {
            foreach (var slot in _slots.Values)
            {
                slot.State.ForgetChannelUpdate();
            }
        }

        await CreateEventSubSubscriptionsAsync(args.SessionId, cancellationToken);
        await InitializeFromApiAsync(cancellationToken);
    }

    private async Task OnNotificationAsync(EventSubNotificationArgs args, CancellationToken cancellationToken)
    {
        switch (args.SubscriptionType)
        {
            case "stream.online":
                await HandleStreamOnlineAsync(args.Payload);
                break;

            case "stream.offline":
                await HandleStreamOfflineAsync(args.Payload);
                break;

            default:
                _logger.LogDebug("StreamStatusManager игнорирует EventSub-нотификацию типа {Type}", args.SubscriptionType);
                break;
        }
    }

    private Task OnSessionReconnectAsync(EventSubReconnectArgs args, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Сессия EventSub мигрировала: {OldSession} → {NewSession}; подписки и статус стрима сохраняются", args.OldSessionId, args.NewSessionId);
        return Task.CompletedTask;
    }

    private async Task OnRevocationAsync(EventSubRevocationArgs args, CancellationToken cancellationToken)
    {
        if (args.SubscriptionType is not ("stream.online" or "stream.offline"))
        {
            _logger.LogDebug("Revocation {Type} не относится к StreamStatusManager – игнорируется", args.SubscriptionType);
            return;
        }

        _logger.LogWarning("EventSub revocation: id={Id}, type={Type}, status={Status}", args.SubscriptionId, args.SubscriptionType, args.Status);
        string? channelId;
        lock (_slotsLock)
        {
            _subscriptionToChannelId.Remove(args.SubscriptionId, out channelId);
        }

        var ledgerKey = string.IsNullOrEmpty(channelId)
            ? args.SubscriptionType
            : GetLedgerKey(channelId, args.SubscriptionType);
        _ledger.Forget(ledgerKey, args.SubscriptionId);

        var sessionId = _eventSubClient.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            _logger.LogDebug("Восстановление подписки {Type} пропущено: SessionId уже отсутствует", args.SubscriptionType);
            return;
        }

        var generation = _ledger.Generation;

        try
        {
            var broadcasterId = !string.IsNullOrEmpty(channelId)
                ? channelId
                : await _broadcasterIdProvider.GetAsync(cancellationToken);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                _logger.LogWarning("Восстановление подписки {Type} прервано: BroadcasterId недоступен", args.SubscriptionType);
                return;
            }

            var key = GetLedgerKey(broadcasterId, args.SubscriptionType);
            var condition = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "broadcaster_user_id", broadcasterId },
            };

            var sessionBotContextId = Volatile.Read(ref _sessionBotContextId);
            var result = await WithContextAsync(
                sessionBotContextId,
                () => _retry.CreateAsync(args.SubscriptionType,
                    "1",
                    condition,
                    sessionId,
                    async retried => await RecordSubscriptionAsync(generation, broadcasterId, key, retried),
                    cancellationToken));

            await RecordSubscriptionAsync(generation, broadcasterId, key, result);

            if (result.Outcome == EventSubSubscribeOutcome.Created)
            {
                _logger.LogInformation("Подписка {Type} для {BroadcasterId} восстановлена после revocation. Новый id: {Id}", args.SubscriptionType, broadcasterId, result.SubscriptionIds[0]);
            }
            else if (result.Outcome == EventSubSubscribeOutcome.Reused)
            {
                _logger.LogInformation("Подписка {Type} {SubscriptionIds} для {BroadcasterId} уже существует для текущей EventSub-сессии – переиспользуем",
                    args.SubscriptionType, result.SubscriptionIds, broadcasterId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Не удалось восстановить подписку {Type} после revocation: {Reason}",
                args.SubscriptionType, StreamingErrorMessages.SafeMessage(ex));
        }
    }

    private Task OnDisconnectedAsync(EventSubDisconnectedArgs args, CancellationToken cancellationToken)
    {
        lock (_slotsLock)
        {
            foreach (var slot in _slots.Values)
            {
                _logger.LogWarning("EventSub WebSocket отключен ({Reason}) – статус стрима {BroadcasterId} ({OldStatus}, стрим {StreamId}) сброшен в Unknown до восстановления соединения",
                    args.Reason,
                    slot.ChannelId,
                    slot.State.CurrentStatus,
                    slot.State.CurrentStream?.Id);

                slot.State.ResetToUnknown();
                slot.Availability = ChannelMonitoringAvailability.Unavailable;
                slot.Reason = MonitoringUnavailableReason.TransportDisconnected;
                slot.Detail = "EventSub отключён";
            }
        }

        _retry.Cancel();
        return Task.CompletedTask;
    }

    private async Task InitializeFromApiAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        var token = linkedCts.Token;

        try
        {
            await _refreshGate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Инициализация по API отменена в очереди опроса");
            return;
        }

        try
        {
            var slots = await EnsureMonitoredSlotsAsync(token).ConfigureAwait(false);

            if (slots.Count == 0)
            {
                _logger.LogDebug("Инициализация по API пропущена: BroadcasterId недоступен");
                return;
            }

            _logger.LogDebug("Запрос начального статуса стримов через API для {Count} каналов Twitch", slots.Count);
            var (streamsByUserId, failedAuthChannelIds) = await FetchStreamsMapAsync(slots, token).ConfigureAwait(false);

            foreach (var slot in slots)
            {
                var previousSnapshot = slot.ToMonitoringSnapshot();
                if (failedAuthChannelIds.Contains(slot.ChannelId))
                {
                    await MarkSlotMissingBotAuthAsync(slot, previousSnapshot).ConfigureAwait(false);
                    continue;
                }

                streamsByUserId.TryGetValue(slot.ChannelId, out var stream);

                StatusTransition transition;
                StreamStatus newStatus;

                if (stream != null)
                {
                    transition = slot.State.ApplyOnlineSnapshot(StreamInfoMapper.MapFromHelix(stream));
                    newStatus = StreamStatus.Online;
                }
                else
                {
                    transition = slot.State.ApplyOffline();
                    newStatus = StreamStatus.Offline;
                }

                slot.LastObservedAtUtc = _timeProvider.GetUtcNow();
                if (slot.Availability != ChannelMonitoringAvailability.Degraded
                    && slot.Reason != MonitoringUnavailableReason.MissingBotAuth)
                {
                    slot.Availability = ChannelMonitoringAvailability.Active;
                    slot.Reason = MonitoringUnavailableReason.None;
                    slot.Detail = null;
                }

                var isInitialBaseline = !slot.BaselinePublished;
                slot.BaselinePublished = true;
                var currentSnapshot = slot.ToMonitoringSnapshot(isInitialBaseline);

                if (HasMonitoringStateChanged(previousSnapshot.Availability, previousSnapshot.Reason, previousSnapshot.Detail,
                        currentSnapshot.Availability, currentSnapshot.Reason, currentSnapshot.Detail))
                {
                    await _eventBus.PublishAsync(
                        new ChannelMonitoringStateChanged(StreamingPlatform.Twitch, new ChannelKey(StreamingPlatform.Twitch, slot.ChannelId), currentSnapshot),
                        CancellationToken.None).ConfigureAwait(false);
                }

                if (transition.Transitioned)
                {
                    var isCatchUp = transition.Previous == StreamStatus.Unknown;

                    _logger.LogInformation("Инициализация: статус стрима {OldStatus} → {NewStatus} для BroadcasterId {BroadcasterId}, стрим {StreamId}, начат {StartedAt}, catch-up {IsCatchUp}",
                        transition.Previous,
                        newStatus,
                        slot.ChannelId,
                        stream?.Id,
                        stream?.StartedAt,
                        isCatchUp);

                    await PublishSlotObservedAsync(previousSnapshot, currentSnapshot, isCatchUp).ConfigureAwait(false);
                    if (IsActiveSlot(slot.ChannelId))
                    {
                        await PublishStatusTransitionAsync(slot, newStatus, isCatchUp).ConfigureAwait(false);
                    }
                }
                else
                {
                    _logger.LogDebug("Инициализация: статус стрима {Status} подтверждён для BroadcasterId {BroadcasterId}, стрим {StreamId}",
                        newStatus,
                        slot.ChannelId,
                        stream?.Id);

                    if (transition.SnapshotChanged && newStatus == StreamStatus.Online)
                    {
                        _logger.LogInformation("Инициализация: метаданные стрима {StreamId} обновлены без смены статуса", stream?.Id);

                        await PublishSlotObservedAsync(previousSnapshot, currentSnapshot, isCatchUp: false).ConfigureAwait(false);
                        if (IsActiveSlot(slot.ChannelId))
                        {
                            await PublishMetadataResolvedAsync(slot).ConfigureAwait(false);
                        }
                    }
                }

                if (stream != null && IsActiveSlot(slot.ChannelId))
                {
                    await PublishViewerSampleObservedAsync(slot, stream).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Инициализация по API отменена (cancellationToken: {CallerCancelled}, dispose: {DisposeCancelled})",
                cancellationToken.IsCancellationRequested, _disposeCts.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось получить начальный статус стрима: {Reason}", StreamingErrorMessages.SafeMessage(ex));
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void OnChannelUpdated(ChannelUpdated @event)
    {
        TwitchChannelSlot? targetSlot;
        lock (_slotsLock)
        {
            if (!string.IsNullOrWhiteSpace(@event.ChannelId))
            {
                _slots.TryGetValue(@event.ChannelId, out targetSlot);
            }
            else
            {
                targetSlot = !string.IsNullOrEmpty(_activeSlotId) && _slots.TryGetValue(_activeSlotId, out var active)
                    ? active
                    : _slots.Values.FirstOrDefault();
            }
        }

        if (targetSlot is not null && targetSlot.State.ApplyChannelUpdate(@event))
        {
            _logger.LogInformation("Метаданные стрима ({BroadcasterId}) обновлены через channel.update: title={Title}, game={GameName}",
                targetSlot.ChannelId, @event.Title, @event.GameName);
        }
        else
        {
            _logger.LogDebug("ChannelUpdated сохранён без обновления CurrentStream: стрим не в состоянии online");
        }
    }

    private async Task HandleStreamOnlineAsync(JsonElement payload)
    {
        var data = payload.Deserialize<EventSubStreamOnlinePayloadDto>(WebJsonOptions);
        const bool isCatchUp = false;

        var streamId = data?.Event?.Id;
        var startedAt = data?.Event?.StartedAt ?? default;
        var eventType = data?.Event?.Type ?? "live";
        var broadcasterUserId = data?.Event?.BroadcasterUserId;
        var broadcasterLogin = data?.Event?.BroadcasterUserLogin;

        var slot = await ResolveOrEnsureSlotAsync(broadcasterUserId, broadcasterLogin).ConfigureAwait(false);
        if (slot is null)
        {
            _logger.LogDebug("EventSub stream.online для неизвестного канала {BroadcasterId} проигнорирован", broadcasterUserId);
            return;
        }

        var previousSnapshot = slot.ToMonitoringSnapshot();
        _logger.LogInformation("Стрим запущен (EventSub): канал {BroadcasterId}, стрим {StreamId}, начат {StartedAt}, тип {EventType}",
            slot.ChannelId,
            streamId,
            startedAt,
            eventType);

        var transition = slot.State.MarkOnline(streamId, startedAt);
        slot.LastObservedAtUtc = _timeProvider.GetUtcNow();
        slot.BaselinePublished = true;
        var currentSnapshot = slot.ToMonitoringSnapshot();

        if (transition.Transitioned)
        {
            _logger.LogInformation("EventSub: переход {OldStatus} → Online для {BroadcasterId}, стрим {StreamId}, начат {StartedAt}, catch-up {IsCatchUp}",
                transition.Previous,
                slot.ChannelId,
                streamId,
                startedAt,
                isCatchUp);

            await PublishSlotObservedAsync(previousSnapshot, currentSnapshot, isCatchUp).ConfigureAwait(false);
            if (IsActiveSlot(slot.ChannelId))
            {
                await PublishStatusTransitionAsync(slot, StreamStatus.Online, isCatchUp).ConfigureAwait(false);
            }
        }

        var runToken = _runCts?.Token ?? CancellationToken.None;
        await slot.MetadataRetryLoop.RestartAsync(runToken).ConfigureAwait(false);
    }

    private async Task HandleStreamOfflineAsync(JsonElement payload)
    {
        EventSubStreamOfflinePayloadDto? data = null;
        if (payload.ValueKind == JsonValueKind.Object)
        {
            try
            {
                data = payload.Deserialize<EventSubStreamOfflinePayloadDto>(WebJsonOptions);
            }
            catch (JsonException)
            {
            }
        }

        var broadcasterUserId = data?.Event?.BroadcasterUserId;
        var slot = await ResolveOrEnsureSlotAsync(broadcasterUserId, data?.Event?.BroadcasterUserLogin).ConfigureAwait(false);
        if (slot is null)
        {
            return;
        }

        var previousSnapshot = slot.ToMonitoringSnapshot();
        var streamId = slot.State.CurrentStream?.Id;

        _logger.LogInformation("Стрим завершен (EventSub): канал {BroadcasterId}, стрим {StreamId}", slot.ChannelId, streamId);

        var transition = slot.State.ApplyOffline();
        slot.LastObservedAtUtc = _timeProvider.GetUtcNow();
        slot.BaselinePublished = true;
        var currentSnapshot = slot.ToMonitoringSnapshot();

        if (transition.Transitioned)
        {
            _logger.LogInformation("EventSub: переход {OldStatus} → Offline для {BroadcasterId}, стрим {StreamId}", transition.Previous, slot.ChannelId, streamId);
            await PublishSlotObservedAsync(previousSnapshot, currentSnapshot, isCatchUp: false).ConfigureAwait(false);
            if (IsActiveSlot(slot.ChannelId))
            {
                await PublishStatusTransitionAsync(slot, StreamStatus.Offline).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> FetchMetadataAttemptAsync(TwitchChannelSlot slot, int attempt, CancellationToken cancellationToken)
    {
        try
        {
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                var stream = await WithContextAsync(slot.BotContextId, () => _helix.GetStreamAsync(slot.ChannelId, cancellationToken)).ConfigureAwait(false);

                if (stream == null)
                {
                    return false;
                }

                var previousSnapshot = slot.ToMonitoringSnapshot();
                slot.State.UpdateStreamSnapshot(StreamInfoMapper.MapFromHelix(stream));
                slot.LastObservedAtUtc = _timeProvider.GetUtcNow();

                if (slot.State.CurrentStream == null)
                {
                    return false;
                }

                await PublishSlotObservedAsync(previousSnapshot, slot.ToMonitoringSnapshot(), isCatchUp: false).ConfigureAwait(false);
                if (IsActiveSlot(slot.ChannelId))
                {
                    await PublishMetadataResolvedAsync(slot).ConfigureAwait(false);
                    await PublishViewerSampleObservedAsync(slot, stream).ConfigureAwait(false);
                }

                return true;
            }
            finally
            {
                _refreshGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка получения метаданных стрима {BroadcasterId} (попытка {Attempt}): {Reason}",
                slot.ChannelId, attempt, StreamingErrorMessages.SafeMessage(ex));

            return false;
        }
    }

    private Task PublishSlotObservedAsync(
        ChannelMonitoringSnapshot previous,
        ChannelMonitoringSnapshot current,
        bool isCatchUp)
    {
        return _eventBus.PublishAsync(new ChannelStreamObserved(previous, current, isCatchUp), CancellationToken.None);
    }

    private Task PublishStatusTransitionAsync(TwitchChannelSlot slot, StreamStatus newStatus, bool isCatchUp = false)
    {
        var channel = ResolvePublishChannelName(slot);
        if (string.IsNullOrWhiteSpace(channel))
        {
            _logger.LogDebug("Публикация события статуса стрима пропущена: имя канала неизвестно");
            return Task.CompletedTask;
        }

        var stream = slot.State.CurrentStream;
        var channelId = string.IsNullOrWhiteSpace(stream?.UserId) ? slot.ChannelId : stream.UserId;

        return newStatus switch
        {
            StreamStatus.Online => _eventBus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, channel, stream, isCatchUp, channelId), CancellationToken.None),
            StreamStatus.Offline => _eventBus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, channel, isCatchUp, channelId), CancellationToken.None),
            _ => Task.CompletedTask,
        };
    }

    private Task PublishMetadataResolvedAsync(TwitchChannelSlot slot)
    {
        var channel = ResolvePublishChannelName(slot);
        var stream = slot.State.CurrentStream;

        if (string.IsNullOrWhiteSpace(channel) || stream == null)
        {
            _logger.LogDebug("Публикация StreamMetadataResolved пропущена (channel пуст или stream null)");
            return Task.CompletedTask;
        }

        var channelId = string.IsNullOrWhiteSpace(stream.UserId) ? slot.ChannelId : stream.UserId;
        return _eventBus.PublishAsync(new StreamMetadataResolved(StreamingPlatform.Twitch, channel, stream, channelId), CancellationToken.None);
    }

    private Task PublishViewerSampleObservedAsync(TwitchChannelSlot slot, HelixStreamInfo stream)
    {
        var channel = ResolvePublishChannelName(slot);
        if (string.IsNullOrWhiteSpace(channel))
        {
            return Task.CompletedTask;
        }

        var sampledAtUtc = slot.State.GetSnapshot().ViewerSampledAtUtc ?? _timeProvider.GetUtcNow();
        var channelId = string.IsNullOrWhiteSpace(stream.UserId) ? slot.ChannelId : stream.UserId;

        return _eventBus.PublishAsync(
            new ViewerSampleObserved(
                StreamingPlatform.Twitch,
                channel,
                stream.ViewerCount,
                sampledAtUtc,
                string.IsNullOrEmpty(stream.Id) ? null : stream.Id,
                channelId),
            CancellationToken.None);
    }

    private async Task CreateEventSubSubscriptionsAsync(string sessionId, CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        var token = linkedCts.Token;

        try
        {
            await _subscriptionsGate.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            await CreateEventSubSubscriptionsCoreAsync(sessionId, token).ConfigureAwait(false);
        }
        finally
        {
            _subscriptionsGate.Release();
        }
    }

    private async Task CreateEventSubSubscriptionsCoreAsync(string sessionId, CancellationToken cancellationToken)
    {
        var generation = await _ledger.BeginSessionAsync(cancellationToken);
        var slots = await EnsureMonitoredSlotsAsync(cancellationToken);

        if (slots.Count == 0)
        {
            _logger.LogWarning("Подписки EventSub пропущены – BroadcasterId недоступен (вероятно, нет токена бота). Подписки создадутся после авторизации.");
            return;
        }

        var previousStates = slots.ToDictionary(
            static slot => slot.ChannelId,
            static slot => (slot.Availability, slot.Reason, slot.Detail),
            StringComparer.Ordinal);

        if (_authService is not null)
        {
            foreach (var slot in slots)
            {
                var hasToken = AuthContextIds.IsDefault(slot.BotContextId)
                    ? _authService.HasUsableAccessToken(PlatformAuthRole.Bot)
                    : _authService.HasUsableAccessToken(PlatformAuthRole.Bot, slot.BotContextId);
                if (!hasToken)
                {
                    slot.Availability = ChannelMonitoringAvailability.Unavailable;
                    slot.Reason = MonitoringUnavailableReason.MissingBotAuth;
                    slot.Detail = "нет авторизации бота";
                }
                else if (slot.Reason == MonitoringUnavailableReason.MissingBotAuth)
                {
                    slot.Availability = ChannelMonitoringAvailability.Active;
                    slot.Reason = MonitoringUnavailableReason.None;
                    slot.Detail = null;
                }
            }
        }

        var routing = _channelCatalogStore?.GetPlatformSettings(StreamingPlatform.Twitch);
        var orderedSlots = slots
            .OrderBy(s => IsActiveSlot(s.ChannelId) ? 0 : string.Equals(s.ChannelId, routing?.PreselectedNextChannelId, StringComparison.Ordinal) ? 1 : 2)
            .ThenBy(static s => s.ChannelId, StringComparer.Ordinal)
            .ToArray();

        var sessionBotContextId = ResolveSessionBotContextId(orderedSlots);
        Volatile.Write(ref _sessionBotContextId, sessionBotContextId);
        lock (_slotsLock)
        {
            _lastSubscribedChannelIds = [.. orderedSlots.Select(static s => s.ChannelId).OrderBy(static id => id, StringComparer.Ordinal)];
        }

        foreach (var slot in orderedSlots)
        {
            if (slot.Reason == MonitoringUnavailableReason.MissingBotAuth)
            {
                await _eventBus.PublishAsync(
                    new ChannelMonitoringStateChanged(StreamingPlatform.Twitch, new ChannelKey(StreamingPlatform.Twitch, slot.ChannelId), slot.ToMonitoringSnapshot()),
                    CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            _logger.LogInformation("Создание подписок EventSub для BroadcasterId: {BroadcasterId}", slot.ChannelId);

            var subscriptionsCreated = 0;
            var hitCostOrLimit = false;
            var condition = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "broadcaster_user_id", slot.ChannelId },
            };

            foreach (var type in new[] { "stream.online", "stream.offline" })
            {
                try
                {
                    var key = GetLedgerKey(slot.ChannelId, type);
                    var result = await WithContextAsync(
                        sessionBotContextId,
                        () => _retry.CreateAsync(type,
                            "1",
                            condition,
                            sessionId,
                            async retried => await RecordSubscriptionAsync(generation, slot.ChannelId, key, retried),
                            cancellationToken)).ConfigureAwait(false);

                    if (await RecordSubscriptionAsync(generation, slot.ChannelId, key, result) && result.IsSubscribed)
                    {
                        subscriptionsCreated++;
                    }
                    else if (result.Outcome == EventSubSubscribeOutcome.TakenByOtherSession)
                    {
                        hitCostOrLimit = true;
                    }

                    if (result.Outcome == EventSubSubscribeOutcome.Created)
                    {
                        _logger.LogInformation("Подписка '{Type}' для {BroadcasterId} создана. SubscriptionId: {SubscriptionId}, SessionId: {SessionId}",
                            type, slot.ChannelId, result.SubscriptionIds[0], sessionId);
                    }
                    else if (result.Outcome == EventSubSubscribeOutcome.Reused)
                    {
                        _logger.LogInformation("Подписка '{Type}' {SubscriptionIds} для {BroadcasterId} уже существует для текущей EventSub-сессии – переиспользуем (SessionId: {SessionId})",
                            type, result.SubscriptionIds, slot.ChannelId, sessionId);
                    }
                }
                catch (HelixRequestException ex) when (ex.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.Forbidden)
                {
                    hitCostOrLimit = true;
                    _logger.LogWarning(ex, "Лимит EventSub для {BroadcasterId} ({Type}): HTTP {Status}; канал переведён на периодический опрос Helix",
                        slot.ChannelId, type, (int)ex.StatusCode);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка создания подписки {Type} для BroadcasterId {BroadcasterId}: {Reason}",
                        type, slot.ChannelId, StreamingErrorMessages.SafeMessage(ex));
                }
            }

            if (hitCostOrLimit || subscriptionsCreated < 2)
            {
                slot.UsesPollingFallback = true;
                slot.Availability = hitCostOrLimit ? ChannelMonitoringAvailability.Degraded : ChannelMonitoringAvailability.Active;
                slot.Reason = hitCostOrLimit ? MonitoringUnavailableReason.SubscriptionLimitReached : MonitoringUnavailableReason.None;
                slot.Detail = hitCostOrLimit ? "превышен лимит подписок EventSub, включён периодический опрос API" : null;
            }
            else
            {
                slot.UsesPollingFallback = false;
                slot.Availability = ChannelMonitoringAvailability.Active;
                slot.Reason = MonitoringUnavailableReason.None;
                slot.Detail = null;
                _logger.LogInformation("Все необходимые подписки EventSub успешно созданы для BroadcasterId: {BroadcasterId}", slot.ChannelId);
            }

            if (hitCostOrLimit
                || !previousStates.TryGetValue(slot.ChannelId, out var previous)
                || HasMonitoringStateChanged(previous.Availability, previous.Reason, previous.Detail,
                    slot.Availability, slot.Reason, slot.Detail))
            {
                await _eventBus.PublishAsync(
                    new ChannelMonitoringStateChanged(StreamingPlatform.Twitch, new ChannelKey(StreamingPlatform.Twitch, slot.ChannelId), slot.ToMonitoringSnapshot()),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private async Task MarkSlotMissingBotAuthAsync(TwitchChannelSlot slot, ChannelMonitoringSnapshot previousSnapshot)
    {
        slot.Availability = ChannelMonitoringAvailability.Unavailable;
        slot.Reason = MonitoringUnavailableReason.MissingBotAuth;
        slot.Detail = "нет авторизации бота";
        var failedSnapshot = slot.ToMonitoringSnapshot();
        if (HasMonitoringStateChanged(previousSnapshot.Availability, previousSnapshot.Reason, previousSnapshot.Detail,
                failedSnapshot.Availability, failedSnapshot.Reason, failedSnapshot.Detail))
        {
            await _eventBus.PublishAsync(
                new ChannelMonitoringStateChanged(StreamingPlatform.Twitch, new ChannelKey(StreamingPlatform.Twitch, slot.ChannelId), failedSnapshot),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool HasMonitoringStateChanged(
        ChannelMonitoringAvailability previousAvailability,
        MonitoringUnavailableReason previousReason,
        string? previousDetail,
        ChannelMonitoringAvailability currentAvailability,
        MonitoringUnavailableReason currentReason,
        string? currentDetail)
    {
        return previousAvailability != currentAvailability
            || previousReason != currentReason
            || !string.Equals(previousDetail, currentDetail, StringComparison.Ordinal);
    }

    private async Task<bool> RecordSubscriptionAsync(
        long generation,
        string broadcasterId,
        string ledgerKey,
        EventSubSubscribeResult result)
    {
        var recorded = await _ledger.RecordAsync(generation, ledgerKey, result).ConfigureAwait(false);
        if (recorded && result.IsSubscribed)
        {
            lock (_slotsLock)
            {
                foreach (var id in result.SubscriptionIds)
                {
                    _subscriptionToChannelId[id] = broadcasterId;
                }
            }
        }

        return recorded;
    }

    private async Task<IReadOnlyList<TwitchChannelSlot>> EnsureMonitoredSlotsAsync(CancellationToken cancellationToken)
    {
        if (!_targetChannelProvider.Current.IsDebugSession && _settingsManager?.Current?.Twitch?.Enabled == false)
        {
            List<TwitchChannelSlot> disabledSlots;
            lock (_slotsLock)
            {
                disabledSlots = [.. _slots.Values];
                _slots.Clear();
                _activeSlotId = null;
            }

            foreach (var slot in disabledSlots)
            {
                await slot.MetadataRetryLoop.CancelAndDrainAsync().ConfigureAwait(false);
            }

            return [];
        }

        var entries = _channelCatalogStore?.GetMonitoredChannels(StreamingPlatform.Twitch) ?? [];

        if (entries.Count == 0 || _targetChannelProvider.Current.IsDebugSession)
        {
            var broadcasterId = await _broadcasterIdProvider.GetAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(broadcasterId))
            {
                return [];
            }

            var label = _targetChannelProvider.Current.Login;
            lock (_slotsLock)
            {
                _activeSlotId = broadcasterId;
                var slot = GetOrCreateSlotLocked(broadcasterId, string.IsNullOrWhiteSpace(label) ? broadcasterId : label);
                return [slot];
            }
        }

        var routing = _channelCatalogStore!.GetPlatformSettings(StreamingPlatform.Twitch);
        var activeChannelId = _channelRegistry?.GetBinding(StreamingPlatform.Twitch)?.Key.ChannelId
            ?? routing.ActiveChannelId
            ?? entries[0].ChannelId;

        var wantedIds = new HashSet<string>(entries.Select(static e => e.ChannelId), StringComparer.Ordinal);
        List<TwitchChannelSlot> removedSlots = [];
        TwitchChannelSlot[] result;

        lock (_slotsLock)
        {
            _activeSlotId = activeChannelId;

            foreach (var existingId in _slots.Keys.ToArray())
            {
                if (!wantedIds.Contains(existingId) && _slots.Remove(existingId, out var removed))
                {
                    removedSlots.Add(removed);
                }
            }

            foreach (var entry in entries)
            {
                var slot = GetOrCreateSlotLocked(entry.ChannelId, ResolveEntryLabel(entry));
                slot.ChannelLabel = ResolveEntryLabel(entry);
                slot.BotContextId = AuthContextIds.Normalize(entry.BotContextId);
            }

            result = [.. _slots.Values];
        }

        foreach (var removed in removedSlots)
        {
            await removed.MetadataRetryLoop.CancelAndDrainAsync().ConfigureAwait(false);
        }

        return result;
    }

    private async Task<TwitchChannelSlot?> ResolveOrEnsureSlotAsync(string? broadcasterUserId, string? broadcasterLogin)
    {
        if (!string.IsNullOrWhiteSpace(broadcasterUserId))
        {
            lock (_slotsLock)
            {
                if (_slots.TryGetValue(broadcasterUserId, out var existing))
                {
                    return existing;
                }
            }

            var entry = _channelCatalogStore?.FindEntry(new(StreamingPlatform.Twitch, broadcasterUserId));
            if (entry is not null && (entry.MonitoringEnabled || IsActiveSlot(entry.ChannelId)))
            {
                lock (_slotsLock)
                {
                    return GetOrCreateSlotLocked(entry.ChannelId, ResolveEntryLabel(entry));
                }
            }

            if (_slots.Count == 0 && !_targetChannelProvider.Current.IsDebugSession)
            {
                var fallbackId = await _broadcasterIdProvider.GetAsync(CancellationToken.None).ConfigureAwait(false);
                if (string.Equals(fallbackId, broadcasterUserId, StringComparison.Ordinal))
                {
                    lock (_slotsLock)
                    {
                        _activeSlotId = broadcasterUserId;
                        return GetOrCreateSlotLocked(broadcasterUserId, string.IsNullOrWhiteSpace(broadcasterLogin) ? broadcasterUserId : broadcasterLogin);
                    }
                }
            }

            return null;
        }

        lock (_slotsLock)
        {
            if (!string.IsNullOrEmpty(_activeSlotId) && _slots.TryGetValue(_activeSlotId, out var active))
            {
                return active;
            }

            if (_slots.Count > 0)
            {
                return _slots.Values.First();
            }
        }

        var ensured = await EnsureMonitoredSlotsAsync(CancellationToken.None).ConfigureAwait(false);
        return ensured.Count > 0 ? ensured[0] : null;
    }

    private async Task<(Dictionary<string, HelixStreamInfo> Streams, HashSet<string> FailedAuthChannelIds)> FetchStreamsMapAsync(
        IReadOnlyList<TwitchChannelSlot> slots,
        CancellationToken token)
    {
        var map = new Dictionary<string, HelixStreamInfo>(StringComparer.Ordinal);
        var failedAuthChannelIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in slots.GroupBy(static s => s.BotContextId, StringComparer.Ordinal))
        {
            var groupSlots = group.ToArray();
            if (!HasUsableBotContext(group.Key))
            {
                foreach (var slot in groupSlots)
                {
                    failedAuthChannelIds.Add(slot.ChannelId);
                }

                continue;
            }

            try
            {
                if (groupSlots.Length == 1)
                {
                    var single = await WithContextAsync(group.Key, () => _helix.GetStreamAsync(groupSlots[0].ChannelId, token)).ConfigureAwait(false);
                    if (single is not null)
                    {
                        map[groupSlots[0].ChannelId] = single;
                    }

                    continue;
                }

                var streams = await WithContextAsync(group.Key, () => _helix.GetStreamsAsync(groupSlots.Select(static s => s.ChannelId), token)).ConfigureAwait(false);
                foreach (var stream in streams)
                {
                    if (!string.IsNullOrWhiteSpace(stream.UserId))
                    {
                        map[stream.UserId] = stream;
                    }
                }
            }
            catch (TwitchAuthorizationMissingException ex)
            {
                _logger.LogWarning(ex, "Опрос статуса стримов Twitch пропущен для контекста {ContextId} ({Count} кан.)",
                    group.Key, groupSlots.Length);
                foreach (var slot in groupSlots)
                {
                    failedAuthChannelIds.Add(slot.ChannelId);
                }
            }
        }

        return (map, failedAuthChannelIds);
    }

    private static async Task<T> WithContextAsync<T>(string contextId, Func<Task<T>> action)
    {
        using var _ = PlatformAuthRequestOptions.UseContext(contextId);
        return await action().ConfigureAwait(false);
    }

    private TwitchChannelSlot GetOrCreateSlotLocked(string channelId, string channelLabel)
    {
        if (!_slots.TryGetValue(channelId, out var slot))
        {
            slot = new(channelId, channelLabel, _timeProvider, _logger, FetchMetadataAttemptAsync);
            _slots[channelId] = slot;
        }

        return slot;
    }

    private TwitchChannelSlot? GetActiveSlot()
    {
        lock (_slotsLock)
        {
            if (!string.IsNullOrEmpty(_activeSlotId) && _slots.TryGetValue(_activeSlotId, out var slot))
            {
                return slot;
            }

            return _slots.Values.FirstOrDefault();
        }
    }

    private bool IsActiveSlot(string channelId)
    {
        lock (_slotsLock)
        {
            return string.Equals(_activeSlotId, channelId, StringComparison.Ordinal);
        }
    }

    private string ResolvePublishChannelName(TwitchChannelSlot slot)
    {
        var activeLabel = _targetChannelProvider.Current.Login;
        if (!string.IsNullOrWhiteSpace(activeLabel) && (_slots.Count <= 1 || _targetChannelProvider.Current.IsDebugSession))
        {
            return activeLabel;
        }

        return string.IsNullOrWhiteSpace(slot.ChannelLabel) ? activeLabel : slot.ChannelLabel;
    }

    private void OnActiveChannelChanged(ActiveChannelChanged @event)
    {
        if (@event.Platform != StreamingPlatform.Twitch)
        {
            return;
        }

        lock (_slotsLock)
        {
            _activeSlotId = @event.NewChannel.ChannelId;
        }
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        var routing = _channelCatalogStore?.GetPlatformSettings(StreamingPlatform.Twitch);
        if (routing?.ActiveChannelId is { } activeId)
        {
            lock (_slotsLock)
            {
                _activeSlotId = activeId;
            }
        }

        if (_eventSubClient.SessionId is { Length: > 0 } sessionId
            && !_disposeCts.IsCancellationRequested
            && HasMonitoredSetChanged())
        {
            _ = SyncCatalogSubscriptionsAsync(sessionId);
        }
    }

    private string ResolveSessionBotContextId(IReadOnlyList<TwitchChannelSlot> orderedSlots)
    {
        var boundContext = _channelRegistry?.GetBinding(StreamingPlatform.Twitch)?.BotContextId;
        if (!string.IsNullOrWhiteSpace(boundContext) && HasUsableBotContext(boundContext))
        {
            return AuthContextIds.Normalize(boundContext);
        }

        foreach (var slot in orderedSlots)
        {
            if (slot.Reason != MonitoringUnavailableReason.MissingBotAuth && HasUsableBotContext(slot.BotContextId))
            {
                return AuthContextIds.Normalize(slot.BotContextId);
            }
        }

        return AuthContextIds.Default;
    }

    private bool HasUsableBotContext(string contextId)
    {
        if (_authService is null)
        {
            return true;
        }

        var normalized = AuthContextIds.Normalize(contextId);
        return AuthContextIds.IsDefault(normalized)
            ? _authService.HasUsableAccessToken(PlatformAuthRole.Bot)
            : _authService.HasUsableAccessToken(PlatformAuthRole.Bot, normalized);
    }

    private bool HasMonitoredSetChanged()
    {
        if (_channelCatalogStore is null || _targetChannelProvider.Current.IsDebugSession)
        {
            return true;
        }

        var currentIds = _channelCatalogStore.GetMonitoredChannels(StreamingPlatform.Twitch)
            .Select(static c => c.ChannelId)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        lock (_slotsLock)
        {
            return !_lastSubscribedChannelIds.SequenceEqual(currentIds, StringComparer.Ordinal);
        }
    }

    private async Task SyncCatalogSubscriptionsAsync(string sessionId)
    {
        try
        {
            await CreateEventSubSubscriptionsAsync(sessionId, _disposeCts.Token).ConfigureAwait(false);
            await RefreshAsync(null, _disposeCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ошибка обновления фонового мониторинга Twitch после изменения каталога: {Reason}", StreamingErrorMessages.SafeMessage(ex));
        }
    }

    private static string ResolveEntryLabel(ChannelCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Handle))
        {
            return entry.Handle;
        }

        if (!string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            return entry.DisplayName;
        }

        return entry.ChannelId;
    }

    private static string GetLedgerKey(string broadcasterId, string type)
    {
        return $"{type}:{broadcasterId}";
    }

    private sealed class TwitchChannelSlot
    {
        public TwitchChannelSlot(
            string channelId,
            string channelLabel,
            TimeProvider timeProvider,
            ILogger logger,
            Func<TwitchChannelSlot, int, CancellationToken, Task<bool>> fetchMetadata)
        {
            ChannelId = channelId;
            ChannelLabel = channelLabel;
            State = new(timeProvider);
            MetadataRetryLoop = new(
                (attempt, ct) => fetchMetadata(this, attempt, ct),
                () => State.CurrentStatus == StreamStatus.Online,
                logger);
        }

        public string ChannelId { get; }

        public string ChannelLabel { get; set; }

        public string BotContextId { get; set; } = AuthContextIds.Default;

        public StreamStateMachine State { get; }

        public StreamMetadataRetryLoop MetadataRetryLoop { get; }

        public ChannelMonitoringAvailability Availability { get; set; } = ChannelMonitoringAvailability.Active;

        public MonitoringUnavailableReason Reason { get; set; }

        public string? Detail { get; set; }

        public bool BaselinePublished { get; set; }

        public bool UsesPollingFallback { get; set; }

        public DateTimeOffset? LastObservedAtUtc { get; set; }

        public ChannelMonitoringSnapshot ToMonitoringSnapshot(bool isInitialBaseline = false)
        {
            return new(
                new ChannelKey(StreamingPlatform.Twitch, ChannelId),
                ChannelLabel,
                Availability,
                Reason,
                Detail,
                State.CurrentStatus,
                State.CurrentStream,
                LastObservedAtUtc,
                isInitialBaseline);
        }
    }
}
