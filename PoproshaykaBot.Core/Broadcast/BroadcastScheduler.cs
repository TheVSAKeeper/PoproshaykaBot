using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Broadcast;

public sealed record BroadcastPlatformDelivery(
    ChatEnqueueResult EnqueueResult, bool AwaitingConfirmation, ChatAccountSendResult? Delivery = null);

public sealed class BroadcastScheduler(
    IBroadcastMessenger messenger,
    IBroadcastPlatformTargetsProvider platformTargetsProvider,
    SettingsManager settingsManager,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<BroadcastScheduler> logger)
    : IBroadcastScheduler, IAsyncDisposable
{
    public const int MaximumIntervalMinutes = (int)((uint.MaxValue - 1) / 60_000);

    private readonly Lock _stateLock = new();
    private readonly Lock _sendLock = new();
    private int _sentMessagesCount;
    private int _runEpoch;
    private DateTimeOffset? _lastBroadcastAt;
    private string? _lastError;
    private long _dispatchVersion;
    private Dictionary<StreamingPlatform, BroadcastPlatformDelivery> _lastDispatch = [];

    private TimeSpan _interval = TimeSpan.FromMinutes(15);
    private PeriodicTimer? _timer;
    private string? _channel;
    private bool _disposed;
    private Task? _runnerTask;
    private CancellationTokenSource? _stopCts;

    public bool IsActive => _runnerTask is { IsCompleted: false } && _stopCts is { IsCancellationRequested: false };

    public int SentMessagesCount => _sentMessagesCount;
    public int QueuedDispatchCount => Volatile.Read(ref _sentMessagesCount);
    public DateTime? NextBroadcastTime { get; private set; }

    public IReadOnlyDictionary<StreamingPlatform, BroadcastPlatformDelivery> GetLastDispatch()
    {
        lock (_stateLock)
        {
            return new Dictionary<StreamingPlatform, BroadcastPlatformDelivery>(_lastDispatch);
        }
    }

    public TimeSpan Interval
    {
        get
        {
            lock (_stateLock)
            {
                return _interval;
            }
        }
    }

    public DateTimeOffset? LastBroadcastAt
    {
        get
        {
            lock (_stateLock)
            {
                return _lastBroadcastAt;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_stateLock)
            {
                return _lastError;
            }
        }
    }

    public void Start(string channel)
    {
        logger.LogDebug("Попытка запуска планировщика рассылки для канала {Channel}", channel);

        if (string.IsNullOrWhiteSpace(channel))
        {
            logger.LogWarning("Запуск отменен: имя канала не задано или состоит из пробелов");
            return;
        }

        lock (_stateLock)
        {
            _channel = channel;
            _lastError = null;
            _runEpoch++;
            Interlocked.Exchange(ref _sentMessagesCount, 0);

            var minutes = Math.Clamp(settingsManager.Current.Twitch.AutoBroadcast.BroadcastIntervalMinutes, 1, MaximumIntervalMinutes);
            _interval = TimeSpan.FromMinutes(minutes);

            logger.LogInformation("Рассылка запущена для канала {Channel}. Интервал: {IntervalMinutes} мин", _channel, minutes);

            _stopCts?.Cancel();
            _stopCts?.Dispose();
            _stopCts = new();

            _timer?.Dispose();
            _timer = new(_interval, timeProvider);

            NextBroadcastTime = timeProvider.GetLocalNow().DateTime + _interval;

            var previousTask = _runnerTask;
            _runnerTask = RunLoopAsync(previousTask, _stopCts.Token);
        }

        PublishStateChanged();
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            logger.LogInformation("Остановка планировщика рассылки для канала {Channel}", _channel);

            _channel = null;
            _runEpoch++;
            Interlocked.Exchange(ref _sentMessagesCount, 0);
            NextBroadcastTime = null;
            _stopCts?.Cancel();
        }

        PublishStateChanged();
    }

    public Task ManualSendAsync()
    {
        string? currentChannel;
        lock (_stateLock)
        {
            currentChannel = _channel;
        }

        logger.LogDebug("Запрошена ручная отправка для канала {Channel}", currentChannel);

        if (string.IsNullOrWhiteSpace(currentChannel))
        {
            logger.LogWarning("Ручная отправка отменена: канал не установлен или пуст");
            return Task.CompletedTask;
        }

        TrySend(currentChannel, true);
        PublishStateChanged();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        logger.LogDebug("Начало освобождения ресурсов (Dispose) планировщика рассылки");

        if (_disposed)
        {
            return;
        }

        Stop();

        lock (_stateLock)
        {
            _timer?.Dispose();
            _timer = null;
            _stopCts?.Dispose();
            _stopCts = null;
        }

        if (_runnerTask != null)
        {
            try
            {
                await _runnerTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Произошла ошибка при ожидании завершения фоновой задачи во время Dispose");
            }
        }

        _disposed = true;

        logger.LogDebug("Освобождение ресурсов планировщика успешно завершено");
    }

    private ChatEnqueueResult TrySend(string channel, bool manual)
    {
        lock (_sendLock)
        {
            int epoch;
            int number;

            lock (_stateLock)
            {
                epoch = _runEpoch;
                number = _sentMessagesCount + 1;
            }

            var targetPlatforms = platformTargetsProvider.GetTargetPlatforms();
            var sendResult = messenger.SendTracked(
                platform => GetMessage(number, platform),
                targetPlatforms,
                ChatSendCategory.ScheduledBroadcast);

            long? version = null;
            lock (_stateLock)
            {
                if (_runEpoch == epoch)
                {
                    version = ++_dispatchVersion;
                    _lastDispatch = sendResult.PerPlatform.ToDictionary(entry => entry.Key, entry =>
                        new BroadcastPlatformDelivery(entry.Value, sendResult.Deliveries?.ContainsKey(entry.Key) == true));
                    if (sendResult.AnyQueued)
                    {
                        Interlocked.Exchange(ref _sentMessagesCount, number);
                        _lastBroadcastAt = timeProvider.GetUtcNow();
                    }
                }
            }

            if (version is { } dispatch && sendResult.Deliveries is { } deliveries)
            {
                foreach (var (platform, delivery) in deliveries)
                {
                    _ = ObserveDeliveryAsync(dispatch, platform, delivery);
                }
            }

            if (!sendResult.AnyQueued)
            {
                var refusal = sendResult.FirstRefusalOr(ChatEnqueueResult.NotConnected);

                if (refusal is ChatEnqueueResult.EmptyMessage)
                {
                    if (manual)
                    {
                        logger.LogWarning("Ручная отправка отменена: провайдер вернул пустое сообщение");
                    }
                    else
                    {
                        logger.LogWarning("Запланированная отправка пропущена: провайдер вернул пустое сообщение");
                    }
                }
                else
                {
                    if (manual)
                    {
                        logger.LogDebug("Ручная отправка в канал {Channel} не состоялась: {Reason}", channel, ChatEnqueueResults.DescribeRefusal(refusal));
                    }
                    else
                    {
                        logger.LogDebug("Запланированное сообщение в канал {Channel} не отправлено: {Reason}", channel, ChatEnqueueResults.DescribeRefusal(refusal));
                    }
                }

                return refusal;
            }

            if (manual)
            {
                logger.LogInformation("Ручная рассылка для канала {Channel} принята в очередь. Номер рассылки: {BroadcastNumber}", channel, number);
            }
            else
            {
                logger.LogInformation("Плановая рассылка для канала {Channel} принята в очередь. Номер рассылки: {BroadcastNumber}", channel, number);
            }

            return ChatEnqueueResult.Queued;
        }
    }

    private async Task ObserveDeliveryAsync(long version, StreamingPlatform platform, Task<ChatAccountSendResult> delivery)
    {
        ChatAccountSendResult outcome;
        try
        {
            outcome = await delivery.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Не получен результат рассылки для {Platform}", platform);
            outcome = new(ChatEnqueueResult.Queued, ChatDeliveryStatus.Unconfirmed);
        }

        lock (_stateLock)
        {
            if (_dispatchVersion != version || !_lastDispatch.TryGetValue(platform, out var queued))
            {
                return;
            }

            _lastDispatch[platform] = queued with { AwaitingConfirmation = false, Delivery = outcome };
        }

        PublishStateChanged();
    }

    private string GetMessage(int counter, IChatPlatform platform)
    {
        var template = settingsManager.Current.Twitch.AutoBroadcast.BroadcastMessageTemplate;
        var info = platform.StreamStatus.GetSnapshot().Stream;

        return MessageTemplate.For(template)
            .With("counter", counter.ToString())
            .With("title", info?.Title)
            .With("game", info?.GameName)
            .With("viewers", info?.ViewerCount.ToString())
            .Render();
    }

    private async Task RunLoopAsync(Task? previousTask, CancellationToken cancellationToken)
    {
        if (previousTask != null)
        {
            try
            {
                logger.LogDebug("Ожидание завершения предыдущего цикла рассылки перед запуском нового");
                await previousTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                logger.LogDebug(ex, "Предыдущий цикл рассылки был успешно отменен");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Предыдущий цикл рассылки завершился с ошибкой перед запуском нового");
            }
        }

        string? loopChannel;
        lock (_stateLock)
        {
            loopChannel = _channel;
        }

        logger.LogDebug("Фоновый цикл рассылки запущен для канала {Channel}", loopChannel);

        try
        {
            while (await _timer!.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                string? currentChannel;
                lock (_stateLock)
                {
                    currentChannel = _channel;
                }

                if (string.IsNullOrWhiteSpace(currentChannel))
                {
                    logger.LogWarning("Прерывание фонового цикла: имя канала сброшено или пустое");
                    break;
                }

                var attempt = TrySend(currentChannel, false);

                if (attempt is ChatEnqueueResult.EmptyMessage)
                {
                    PublishStateChanged();
                    continue;
                }

                lock (_stateLock)
                {
                    NextBroadcastTime = timeProvider.GetLocalNow().DateTime + _interval;

                }

                PublishStateChanged();
            }
        }
        catch (OperationCanceledException ex)
        {
            logger.LogDebug(ex, "Фоновый цикл рассылки штатно остановлен (сработал CancellationToken)");
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _lastError = ex.Message;
            }

            logger.LogError(ex, "Критическая ошибка в фоновом цикле рассылки для канала {Channel}", loopChannel);
        }

        logger.LogDebug("Фоновый цикл рассылки завершен для канала {Channel}", loopChannel);
    }

    private void PublishStateChanged()
    {
        string? channel;
        DateTime? nextBroadcast;
        var sentMessages = Volatile.Read(ref _sentMessagesCount);

        lock (_stateLock)
        {
            channel = _channel;
            nextBroadcast = NextBroadcastTime;
        }

        _ = eventBus.PublishAsync(new BroadcastSchedulerStateChanged(IsActive, channel, sentMessages, nextBroadcast), CancellationToken.None);
    }
}
