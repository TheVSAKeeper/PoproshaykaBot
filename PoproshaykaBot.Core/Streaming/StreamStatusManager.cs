using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;
using System.Text.Json;

namespace PoproshaykaBot.Core.Streaming;

public class StreamStatusManager : IStreamStatus, IStreamHostedComponent, IAsyncDisposable
{
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITwitchEventSubClient _eventSubClient;
    private readonly ITwitchHelixClient _helix;
    private readonly IBroadcasterIdProvider _broadcasterIdProvider;
    private readonly SettingsManager _settingsManager;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StreamStatusManager> _logger;

    private readonly StreamStateMachine _state = new();
    private readonly StreamMetadataRetryLoop _metadataRetryLoop;
    private readonly CancellationTokenSource _disposeCts = new();

    private IDisposable? _channelUpdatedSubscription;
    private CancellationTokenSource? _runCts;
    private bool _subscribed;
    private bool _disposed;

    public StreamStatusManager(
        ITwitchEventSubClient eventSubClient,
        [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
        ITwitchHelixClient helix,
        IBroadcasterIdProvider broadcasterIdProvider,
        SettingsManager settingsManager,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<StreamStatusManager> logger)
    {
        _eventSubClient = eventSubClient;
        _helix = helix;
        _broadcasterIdProvider = broadcasterIdProvider;
        _settingsManager = settingsManager;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
        _metadataRetryLoop = new(FetchMetadataAttemptAsync, IsCurrentlyOnline, logger);
    }

    public StreamStatus CurrentStatus => _state.CurrentStatus;

    public StreamInfo? CurrentStream => _state.CurrentStream;

    public string Name => "Отслеживание статуса стрима";

    public int StartOrder => 256;

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

        _channelUpdatedSubscription = _eventBus.Subscribe<ChannelUpdated>(OnChannelUpdated);
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
        _subscribed = false;

        if (_runCts != null)
        {
            await _runCts.CancelAsync().ConfigureAwait(false);
        }

        await _metadataRetryLoop.CancelAndDrainAsync().ConfigureAwait(false);

        _runCts?.Dispose();
        _runCts = null;

        _state.ResetToUnknown();

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

        await _metadataRetryLoop.DisposeAsync().ConfigureAwait(false);

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
            _subscribed = false;
        }

        GC.SuppressFinalize(this);
    }

    public async Task RefreshLiveSnapshotAsync()
    {
        var token = _disposeCts.Token;

        try
        {
            var broadcasterId = await _broadcasterIdProvider.GetAsync(token);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                _logger.LogDebug("Live-snapshot пропущен: BroadcasterId недоступен");
                return;
            }

            _logger.LogDebug("Live-snapshot: запрос статуса для BroadcasterId {BroadcasterId} (текущий статус {CurrentStatus})", broadcasterId, CurrentStatus);
            var stream = await _helix.GetStreamAsync(broadcasterId, token);

            if (stream != null)
            {
                var transition = _state.ApplyOnlineSnapshot(StreamInfoMapper.MapFromHelix(stream));

                if (transition.Transitioned)
                {
                    _logger.LogInformation("Live-snapshot: переход {OldStatus} → Online для BroadcasterId {BroadcasterId}", transition.Previous, broadcasterId);
                    await PublishStatusTransitionAsync(StreamStatus.Online, true).ConfigureAwait(false);
                }
                else
                {
                    _logger.LogDebug("Live-snapshot: статус Online подтверждён для BroadcasterId {BroadcasterId}", broadcasterId);
                }

                return;
            }

            var probe = _state.ProbeOfflineDivergence(_timeProvider.GetUtcNow().UtcDateTime, StuckOnlineThreshold);

            switch (probe.Action)
            {
                case OfflineProbeAction.ForcedOffline:
                    _logger.LogWarning("Принудительный перевод в Offline после {ElapsedSeconds} с расхождения с API для BroadcasterId {BroadcasterId}",
                        (int)probe.Elapsed.TotalSeconds, broadcasterId);

                    await PublishStatusTransitionAsync(StreamStatus.Offline).ConfigureAwait(false);
                    return;

                case OfflineProbeAction.Pending:
                    _logger.LogWarning("API сообщает офлайн при локальном Online, ждём подтверждения (расхождение {Elapsed}/{Threshold})",
                        probe.Elapsed, StuckOnlineThreshold);

                    return;

                case OfflineProbeAction.NotOnline:
                default:
                    _logger.LogDebug("Live-snapshot: статус Offline подтверждён для BroadcasterId {BroadcasterId}", broadcasterId);
                    return;
            }
        }
        catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
        {
            _logger.LogDebug("Live-snapshot отменён: StreamStatusManager уничтожается");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось обновить live-snapshot стрима: {Reason}", StreamingErrorMessages.SafeMessage(ex));
        }
    }

    private async Task OnSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventSub WebSocket подключен. SessionId: {SessionId}", args.SessionId);

        _state.ForgetChannelUpdate();

        await CreateEventSubSubscriptionsAsync(args.SessionId, cancellationToken);
        await InitializeFromApiAsync(cancellationToken);
    }

    private async Task OnNotificationAsync(EventSubNotificationArgs args, CancellationToken cancellationToken)
    {
        switch (args.SubscriptionType)
        {
            case "stream.online":
                await HandleStreamOnlineAsync(args.Payload, cancellationToken);
                break;

            case "stream.offline":
                await HandleStreamOfflineAsync(cancellationToken);
                break;

            default:
                _logger.LogDebug("StreamStatusManager игнорирует EventSub-нотификацию типа {Type}", args.SubscriptionType);
                break;
        }
    }

    private Task OnSessionReconnectAsync(EventSubReconnectArgs args, CancellationToken cancellationToken)
    {
        _logger.LogInformation("EventSub session_reconnect → {Url} (старый session: {OldSession})", args.ReconnectUrl, args.OldSessionId);
        return Task.CompletedTask;
    }

    private async Task OnRevocationAsync(EventSubRevocationArgs args, CancellationToken cancellationToken)
    {
        _logger.LogWarning("EventSub revocation: id={Id}, type={Type}, status={Status}", args.SubscriptionId, args.SubscriptionType, args.Status);

        if (string.IsNullOrEmpty(_eventSubClient.SessionId))
        {
            _logger.LogDebug("Восстановление подписки {Type} пропущено: SessionId уже отсутствует", args.SubscriptionType);
            return;
        }

        try
        {
            var broadcasterId = await _broadcasterIdProvider.GetAsync(cancellationToken);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                _logger.LogWarning("Восстановление подписки {Type} прервано: BroadcasterId недоступен", args.SubscriptionType);
                return;
            }

            var condition = new Dictionary<string, string>
            {
                { "broadcaster_user_id", broadcasterId },
            };

            var newId = await _helix.CreateEventSubSubscriptionAsync(args.SubscriptionType,
                "1",
                condition,
                _eventSubClient.SessionId,
                cancellationToken);

            _logger.LogInformation("Подписка {Type} восстановлена после revocation. Новый id: {Id}", args.SubscriptionType, newId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Восстановление подписки {Type} отменено", args.SubscriptionType);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось восстановить подписку {Type} после revocation: {Reason}",
                args.SubscriptionType, StreamingErrorMessages.SafeMessage(ex));
        }
    }

    private Task OnDisconnectedAsync(EventSubDisconnectedArgs args, CancellationToken cancellationToken)
    {
        _logger.LogWarning("EventSub WebSocket отключен ({Reason}) — статус стрима сброшен в Unknown до восстановления соединения", args.Reason);

        _state.ResetToUnknown();
        return Task.CompletedTask;
    }

    private async Task InitializeFromApiAsync(CancellationToken cancellationToken)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
        var token = linkedCts.Token;

        try
        {
            var broadcasterId = await _broadcasterIdProvider.GetAsync(token);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                _logger.LogDebug("Инициализация по API пропущена: BroadcasterId недоступен");
                return;
            }

            _logger.LogDebug("Запрос начального статуса стрима через API для BroadcasterId: {BroadcasterId}", broadcasterId);
            var stream = await _helix.GetStreamAsync(broadcasterId, token);

            StatusTransition transition;
            StreamStatus newStatus;

            if (stream != null)
            {
                transition = _state.ApplyOnlineSnapshot(StreamInfoMapper.MapFromHelix(stream));
                newStatus = StreamStatus.Online;
            }
            else
            {
                transition = _state.ApplyOffline();
                newStatus = StreamStatus.Offline;
            }

            if (transition.Transitioned)
            {
                _logger.LogInformation("Инициализация: статус стрима {OldStatus} → {NewStatus} для BroadcasterId {BroadcasterId}",
                    transition.Previous, newStatus, broadcasterId);

                await PublishStatusTransitionAsync(newStatus, stream != null).ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug("Инициализация: статус стрима {Status} подтверждён для BroadcasterId {BroadcasterId}", newStatus, broadcasterId);
            }

            _logger.LogInformation(stream != null
                ? "Начальный статус: онлайн (по данным API)"
                : "Начальный статус: офлайн (по данным API)");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _logger.LogDebug("Инициализация по API отменена (cancellationToken: {CallerCancelled}, dispose: {DisposeCancelled})",
                cancellationToken.IsCancellationRequested, _disposeCts.IsCancellationRequested);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Не удалось получить начальный статус стрима: {Reason}", StreamingErrorMessages.SafeMessage(ex));
        }
    }

    private void OnChannelUpdated(ChannelUpdated @event)
    {
        if (_state.ApplyChannelUpdate(@event))
        {
            _logger.LogInformation("Метаданные стрима обновлены через channel.update: title={Title}, game={GameName}",
                @event.Title, @event.GameName);
        }
        else
        {
            _logger.LogDebug("ChannelUpdated сохранён без обновления CurrentStream: стрим не в состоянии online");
        }
    }

    private async Task HandleStreamOnlineAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var data = payload.Deserialize<EventSubStreamOnlinePayloadDto>(WebJsonOptions);

        _logger.LogInformation("Стрим запущен (EventSub), тип: {EventType}", data?.Event?.Type ?? "live");

        var transition = _state.MarkOnline();

        if (transition.Transitioned)
        {
            _logger.LogInformation("EventSub: переход {OldStatus} → Online", transition.Previous);
            await PublishStatusTransitionAsync(StreamStatus.Online).ConfigureAwait(false);
        }

        var runToken = _runCts?.Token ?? CancellationToken.None;
        await _metadataRetryLoop.RestartAsync(runToken).ConfigureAwait(false);
    }

    private async Task HandleStreamOfflineAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Стрим завершен (EventSub)");

        var transition = _state.ApplyOffline();

        if (transition.Transitioned)
        {
            _logger.LogInformation("EventSub: переход {OldStatus} → Offline", transition.Previous);
            await PublishStatusTransitionAsync(StreamStatus.Offline).ConfigureAwait(false);
        }
    }

    private async Task<bool> FetchMetadataAttemptAsync(int attempt, CancellationToken cancellationToken)
    {
        try
        {
            var broadcasterId = await _broadcasterIdProvider.GetAsync(cancellationToken);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                return false;
            }

            var stream = await _helix.GetStreamAsync(broadcasterId, cancellationToken);

            if (stream == null)
            {
                return false;
            }

            _state.UpdateStreamSnapshot(StreamInfoMapper.MapFromHelix(stream));

            if (_state.CurrentStream == null)
            {
                return false;
            }

            await PublishMetadataResolvedAsync().ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка получения метаданных стрима (попытка {Attempt}): {Reason}",
                attempt, StreamingErrorMessages.SafeMessage(ex));

            return false;
        }
    }

    private bool IsCurrentlyOnline()
    {
        return _state.CurrentStatus == StreamStatus.Online;
    }

    private Task PublishStatusTransitionAsync(StreamStatus newStatus, bool isCatchUp = false)
    {
        var channel = _settingsManager.Current.Twitch.Channel;

        if (string.IsNullOrWhiteSpace(channel))
        {
            _logger.LogDebug("Публикация события статуса стрима пропущена: имя канала неизвестно");
            return Task.CompletedTask;
        }

        return newStatus switch
        {
            StreamStatus.Online => _eventBus.PublishAsync(new StreamWentOnline(channel, CurrentStream, isCatchUp)),
            StreamStatus.Offline => _eventBus.PublishAsync(new StreamWentOffline(channel)),
            _ => Task.CompletedTask,
        };
    }

    private Task PublishMetadataResolvedAsync()
    {
        var channel = _settingsManager.Current.Twitch.Channel;
        var stream = CurrentStream;

        if (string.IsNullOrWhiteSpace(channel) || stream == null)
        {
            _logger.LogDebug("Публикация StreamMetadataResolved пропущена (channel пуст или stream null)");
            return Task.CompletedTask;
        }

        return _eventBus.PublishAsync(new StreamMetadataResolved(channel, stream));
    }

    private async Task CreateEventSubSubscriptionsAsync(string sessionId, CancellationToken cancellationToken)
    {
        var broadcasterId = await _broadcasterIdProvider.GetAsync(cancellationToken);

        if (string.IsNullOrEmpty(broadcasterId))
        {
            _logger.LogWarning("Подписки EventSub пропущены — BroadcasterId недоступен (вероятно, нет токена бота). Подписки создадутся после авторизации.");
            return;
        }

        _logger.LogInformation("Создание подписок EventSub для BroadcasterId: {BroadcasterId}", broadcasterId);

        var subscriptionsCreated = 0;
        var condition = new Dictionary<string, string>
        {
            { "broadcaster_user_id", broadcasterId },
        };

        foreach (var type in new[] { "stream.online", "stream.offline" })
        {
            try
            {
                var subscriptionId = await _helix.CreateEventSubSubscriptionAsync(type,
                    "1",
                    condition,
                    sessionId,
                    cancellationToken);

                subscriptionsCreated++;

                _logger.LogInformation("Подписка '{Type}' создана. SubscriptionId: {SubscriptionId}, SessionId: {SessionId}",
                    type, subscriptionId, sessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка создания подписки {Type} для BroadcasterId {BroadcasterId}: {Reason}",
                    type, broadcasterId, StreamingErrorMessages.SafeMessage(ex));
            }
        }

        if (subscriptionsCreated == 2)
        {
            _logger.LogInformation("Все необходимые подписки EventSub успешно созданы для BroadcasterId: {BroadcasterId}", broadcasterId);
        }
        else
        {
            _logger.LogWarning("Создано только {CreatedCount}/2 подписок EventSub stream.* для BroadcasterId {BroadcasterId} — статус стрима будет неполным",
                subscriptionsCreated, broadcasterId);
        }
    }

    internal Task? MetadataRetryTask => _metadataRetryLoop.CurrentTask;
}
