using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Server.Obs;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Settings.Obs;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace PoproshaykaBot.WinForms.Server;

public sealed class SseService(SettingsManager settingsManager, ILogger<SseService> logger) : IAsyncDisposable
{
    private const int GlobalChannelCapacity = 512;
    private const int ClientChannelCapacity = 256;
    private const int DropLogThrottle = 50;
    private const int DropNotifyThreshold = 10;

    private readonly Dictionary<HttpResponse, ClientPipeline> _clients = new();
    private readonly object _clientsLock = new();

    private readonly Channel<SseEnvelope> _messageChannel = Channel.CreateBounded<SseEnvelope>(new BoundedChannelOptions(GlobalChannelCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.DropOldest,
    });

    private readonly object _lifecycleLock = new();
    private readonly object _enqueueLock = new();

    private CancellationTokenSource? _cts;
    private Task? _broadcastTask;
    private Task? _keepAliveTask;
    private bool _isRunning;
    private long _droppedMessageCount;
    private long _clientDroppedMessageCount;

    public long DroppedMessageCount => Interlocked.Read(ref _droppedMessageCount);

    public long ClientDroppedMessageCount => Interlocked.Read(ref _clientDroppedMessageCount);

    public void Start()
    {
        logger.LogDebug("Инициализация запуска сервиса SSE");

        lock (_lifecycleLock)
        {
            if (_isRunning)
            {
                logger.LogWarning("Сервис SSE уже запущен. Повторный запрос на запуск проигнорирован");
                return;
            }

            _cts = new();
            var keepAliveSeconds = Math.Max(5, settingsManager.Current.Twitch.Infrastructure.SseKeepAliveSeconds);

            _keepAliveTask = Task.Run(() => KeepAliveLoopAsync(keepAliveSeconds, _cts.Token));
            _broadcastTask = Task.Run(() => BroadcastLoopAsync(_cts.Token));

            _isRunning = true;
            logger.LogInformation("Сервис SSE успешно запущен. Интервал keep-alive: {KeepAliveSeconds} сек", keepAliveSeconds);
        }
    }

    public async Task StopAsync()
    {
        logger.LogDebug("Остановка сервиса SSE");

        CancellationTokenSource? cts;
        Task? broadcastTask;
        Task? keepAliveTask;

        lock (_lifecycleLock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            cts = _cts;
            broadcastTask = _broadcastTask;
            keepAliveTask = _keepAliveTask;
        }

        await (cts?.CancelAsync() ?? Task.CompletedTask);

        try
        {
            await Task.WhenAll(broadcastTask ?? Task.CompletedTask, keepAliveTask ?? Task.CompletedTask);
        }
        catch (OperationCanceledException)
        {
        }

        List<ClientPipeline> pipelines;
        lock (_clientsLock)
        {
            logger.LogInformation("Отключено {ClientCount} SSE клиентов при остановке сервиса", _clients.Count);
            pipelines = _clients.Values.ToList();
            _clients.Clear();
        }

        foreach (var pipeline in pipelines)
        {
            pipeline.Channel.Writer.TryComplete();
        }

        try
        {
            await Task.WhenAll(pipelines.Select(p => p.WriterTask ?? Task.CompletedTask));
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void RemoveClient(HttpResponse response)
    {
        ClientPipeline? pipeline;

        lock (_clientsLock)
        {
            if (!_clients.Remove(response, out pipeline))
            {
                return;
            }
        }

        pipeline.Channel.Writer.TryComplete();
        logger.LogDebug("SSE клиент удалён по завершению соединения. Активных: {Count}", _clients.Count);
    }

    public void AddClient(HttpResponse response)
    {
        logger.LogDebug("Попытка регистрации нового SSE клиента");

        CancellationToken token;

        lock (_lifecycleLock)
        {
            if (!_isRunning || _cts is null)
            {
                logger.LogWarning("Попытка зарегистрировать SSE клиента до Start() сервиса. Подключение отклонено");
                return;
            }

            token = _cts.Token;
        }

        try
        {
            var pipeline = new ClientPipeline(response, ClientChannelCapacity);
            int clientCount;

            lock (_clientsLock)
            {
                _clients[response] = pipeline;
                clientCount = _clients.Count;
                pipeline.WriterTask = Task.Run(() => ClientWriterLoopAsync(pipeline, token));
            }

            logger.LogInformation("Установлено новое SSE подключение. Всего активных клиентов: {ClientCount}", clientCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка при установке нового SSE подключения");
        }
    }

    public void AddChatMessage(ChatMessageData chatMessage)
    {
        logger.LogDebug("Подготовка нового сообщения чата для отправки в SSE");

        try
        {
            var json = JsonSerializer.Serialize(DtoMapper.ToServerMessage(chatMessage), ServerJsonOptions.Default);
            Enqueue(new("message", json));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка сериализации или постановки в очередь сообщения чата");
        }
    }

    public void ClearChat()
    {
        logger.LogInformation("Инициация события очистки чата для всех SSE клиентов");

        try
        {
            Enqueue(new("clear", "{}"));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка постановки в очередь события очистки чата");
        }
    }

    public void NotifyChatSettingsChanged(ObsChatSettings settings)
    {
        logger.LogDebug("Подготовка уведомления об изменении настроек чата");

        try
        {
            var cssSettings = ObsChatCssSettings.FromObsChatSettings(settings);
            var json = JsonSerializer.Serialize(cssSettings, ServerJsonOptions.Default);

            Enqueue(new("chat_settings_changed", json));
            logger.LogInformation("Уведомление об изменении настроек чата поставлено в очередь на отправку");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ошибка подготовки уведомления о настройках чата");
        }
    }

    public async ValueTask DisposeAsync()
    {
        logger.LogDebug("Освобождение ресурсов сервиса SSE (DisposeAsync)");
        await StopAsync();
        _cts?.Dispose();
    }

    private void Enqueue(SseEnvelope envelope)
    {
        bool willDrop;
        lock (_enqueueLock)
        {
            willDrop = _messageChannel.Reader.Count >= GlobalChannelCapacity;

            if (!_messageChannel.Writer.TryWrite(envelope))
            {
                logger.LogWarning("Не удалось записать сообщение в канал SSE. Очередь недоступна");
                return;
            }
        }

        if (!willDrop)
        {
            return;
        }

        var total = Interlocked.Increment(ref _droppedMessageCount);

        if (total == 1 || total % DropLogThrottle == 0)
        {
            logger.LogWarning("SSE очередь переполнена ({Capacity}), теряем старые сообщения. Всего потерь: {Total}",
                GlobalChannelCapacity,
                total);
        }
    }

    private async Task KeepAliveLoopAsync(int intervalSeconds, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                Enqueue(SseEnvelope.Comment("keep-alive"));
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Цикл keep-alive остановлен штатно");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Неожиданная ошибка в цикле keep-alive SSE");
        }
    }

    private async Task BroadcastLoopAsync(CancellationToken token)
    {
        long lastNotifiedGlobalDrops = 0;
        long lastNotifiedClientDrops = 0;

        try
        {
            await foreach (var envelope in _messageChannel.Reader.ReadAllAsync(token))
            {
                var currentGlobalDrops = Interlocked.Read(ref _droppedMessageCount);
                var currentClientDrops = Interlocked.Read(ref _clientDroppedMessageCount);

                if (currentGlobalDrops - lastNotifiedGlobalDrops >= DropNotifyThreshold
                    || currentClientDrops - lastNotifiedClientDrops >= DropNotifyThreshold)
                {
                    var droppedJson = JsonSerializer.Serialize(new DroppedNotice(currentGlobalDrops, currentClientDrops),
                        ServerJsonOptions.Default);

                    var droppedBuffer = Encoding.UTF8.GetBytes(SseFormatter.Format(new("dropped", droppedJson)));
                    BroadcastBuffer(droppedBuffer);
                    lastNotifiedGlobalDrops = currentGlobalDrops;
                    lastNotifiedClientDrops = currentClientDrops;
                }

                var buffer = Encoding.UTF8.GetBytes(SseFormatter.Format(envelope));
                BroadcastBuffer(buffer);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Цикл рассылки SSE остановлен штатно");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Критическая ошибка в фоновом цикле рассылки SSE");
        }
    }

    private void BroadcastBuffer(byte[] buffer)
    {
        List<ClientPipeline> snapshot;
        lock (_clientsLock)
        {
            if (_clients.Count == 0)
            {
                return;
            }

            snapshot = _clients.Values.ToList();
        }

        foreach (var pipeline in snapshot)
        {
            var willDrop = pipeline.Channel.Reader.Count >= ClientChannelCapacity;

            if (!pipeline.Channel.Writer.TryWrite(buffer))
            {
                continue;
            }

            if (!willDrop)
            {
                continue;
            }

            var total = Interlocked.Increment(ref _clientDroppedMessageCount);

            if (total == 1 || total % DropLogThrottle == 0)
            {
                logger.LogWarning("Per-client SSE очередь переполнена ({Capacity}). Всего потерь по клиентам: {Total}",
                    ClientChannelCapacity,
                    total);
            }
        }
    }

    private async Task ClientWriterLoopAsync(ClientPipeline pipeline, CancellationToken token)
    {
        try
        {
            await foreach (var buffer in pipeline.Channel.Reader.ReadAllAsync(token))
            {
                try
                {
                    await pipeline.Response.Body.WriteAsync(buffer, token);
                    await pipeline.Response.Body.FlushAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Сбой записи в поток SSE. Клиент будет удалён");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Неожиданная ошибка в цикле записи SSE-клиента");
        }
        finally
        {
            RemoveClient(pipeline.Response);
        }
    }

    private sealed record DroppedNotice(long Count, long ClientCount);

    private sealed class ClientPipeline
    {
        public ClientPipeline(HttpResponse response, int capacity)
        {
            Response = response;
            Channel = System.Threading.Channels.Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });
        }

        public HttpResponse Response { get; }
        public Channel<byte[]> Channel { get; }
        public Task? WriterTask { get; set; }
    }
}
