using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Broadcast;

public sealed class BroadcastScheduler(
    IBroadcastMessenger messenger,
    IBroadcastPlatformTargetsProvider platformTargetsProvider,
    SettingsManager settingsManager,
    IEventBus eventBus,
    TimeProvider timeProvider,
    ILogger<BroadcastScheduler> logger)
    : IBroadcastScheduler, IAsyncDisposable
{
    private readonly Lock _stateLock = new();
    private readonly Lock _sendLock = new();
    private int _sentMessagesCount;
    private int _runEpoch;
    private DateTimeOffset? _lastBroadcastAt;
    private string? _lastError;

    private TimeSpan _interval = TimeSpan.FromMinutes(15);
    private PeriodicTimer? _timer;
    private string? _channel;
    private bool _disposed;
    private Task? _runnerTask;
    private CancellationTokenSource? _stopCts;

    public bool IsActive => _runnerTask is { IsCompleted: false } && _stopCts is { IsCancellationRequested: false };

    public int SentMessagesCount => _sentMessagesCount;
    public DateTime? NextBroadcastTime { get; private set; }

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

            var minutes = Math.Max(1, settingsManager.Current.Twitch.AutoBroadcast.BroadcastIntervalMinutes);
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

        if (TrySend(currentChannel, true) is not ChatEnqueueResult.Queued)
        {
            return Task.CompletedTask;
        }

        lock (_stateLock)
        {
            _lastBroadcastAt = timeProvider.GetUtcNow();
        }

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
            var sendResult = messenger.Send(
                platform => GetMessage(number, platform),
                targetPlatforms,
                ChatSendCategory.ScheduledBroadcast);

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

            lock (_stateLock)
            {
                if (_runEpoch == epoch)
                {
                    Interlocked.Exchange(ref _sentMessagesCount, number);
                }
            }

            if (manual)
            {
                logger.LogInformation("Сообщение отправлено вручную в канал {Channel}. Всего отправлено: {SentMessagesCount}", channel, number);
            }
            else
            {
                logger.LogInformation("Запланированное сообщение отправлено в канал {Channel}. Всего отправлено: {SentMessagesCount}", channel, number);
            }

            return ChatEnqueueResult.Queued;
        }
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
                    continue;
                }

                lock (_stateLock)
                {
                    NextBroadcastTime = timeProvider.GetLocalNow().DateTime + _interval;

                    if (attempt is ChatEnqueueResult.Queued)
                    {
                        _lastBroadcastAt = timeProvider.GetUtcNow();
                    }
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
