using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Broadcast;

// TODO: string.IsNullOrWhiteSpace(currentChannel) - рассмотреть обязательность Channel с выкидыванием ошибки
public sealed class BroadcastScheduler(
    TwitchChatMessenger messenger,
    SettingsManager settingsManager,
    IStreamStatus streamStatusManager,
    IEventBus eventBus,
    ILogger<BroadcastScheduler> logger)
    : IBroadcastScheduler, IAsyncDisposable
{
    private readonly object _stateLock = new();
    private int _sentMessagesCount;

    private TimeSpan _interval = TimeSpan.FromMinutes(15);
    private PeriodicTimer? _timer;
    private string? _channel;
    private bool _disposed;
    private Task? _runnerTask;
    private CancellationTokenSource? _stopCts;

    public bool IsActive => _runnerTask is { IsCompleted: false } && _stopCts is { IsCancellationRequested: false };

    public int SentMessagesCount => _sentMessagesCount;
    public DateTime? NextBroadcastTime { get; private set; }

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
            Interlocked.Exchange(ref _sentMessagesCount, 0);

            var minutes = Math.Max(1, settingsManager.Current.Twitch.AutoBroadcast.BroadcastIntervalMinutes);
            _interval = TimeSpan.FromMinutes(minutes);

            logger.LogInformation("Рассылка запущена для канала {Channel}. Интервал: {IntervalMinutes} мин", _channel, minutes);

            _stopCts?.Cancel();
            _stopCts?.Dispose();
            _stopCts = new();

            _timer?.Dispose();
            _timer = new(_interval);

            NextBroadcastTime = DateTime.Now + _interval;

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

        var newCount = Interlocked.Increment(ref _sentMessagesCount);
        var message = GetMessage(newCount);

        if (string.IsNullOrWhiteSpace(message))
        {
            logger.LogWarning("Ручная отправка отменена: провайдер вернул пустое сообщение");
            return Task.CompletedTask;
        }

        messenger.Send(message);
        logger.LogInformation("Сообщение отправлено вручную в канал {Channel}. Всего отправлено: {SentMessagesCount}", currentChannel, newCount);

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
                await _runnerTask;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Произошла ошибка при ожидании завершения фоновой задачи во время Dispose");
            }
        }

        _disposed = true;

        logger.LogDebug("Освобождение ресурсов планировщика успешно завершено");
    }

    private string GetMessage(int counter)
    {
        var template = settingsManager.Current.Twitch.AutoBroadcast.BroadcastMessageTemplate;
        var info = streamStatusManager.CurrentStream;

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
                await previousTask;
            }
            catch (OperationCanceledException)
            {
                logger.LogDebug("Предыдущий цикл рассылки был успешно отменен");
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
            while (await _timer!.WaitForNextTickAsync(cancellationToken))
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

                var newCount = Interlocked.Increment(ref _sentMessagesCount);
                var message = GetMessage(newCount);

                if (string.IsNullOrWhiteSpace(message))
                {
                    logger.LogWarning("Запланированная отправка пропущена: провайдер вернул пустое сообщение");
                    continue;
                }

                messenger.Send(message);
                logger.LogInformation("Запланированное сообщение отправлено в канал {Channel}. Всего отправлено: {SentMessagesCount}", currentChannel, newCount);

                lock (_stateLock)
                {
                    NextBroadcastTime = DateTime.Now + _interval;
                }

                PublishStateChanged();
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Фоновый цикл рассылки штатно остановлен (сработал CancellationToken)");
        }
        catch (Exception ex)
        {
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

        _ = eventBus.PublishAsync(new BroadcastSchedulerStateChanged(IsActive, channel, sentMessages, nextBroadcast));
    }
}
