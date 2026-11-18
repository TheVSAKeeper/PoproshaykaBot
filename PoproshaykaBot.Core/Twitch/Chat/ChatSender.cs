using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Twitch.Helix;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Threading.Channels;

namespace PoproshaykaBot.Core.Twitch.Chat;

public sealed class ChatSender(
    [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
    ITwitchHelixClient helix,
    IBroadcasterIdProvider broadcasterIdProvider,
    IBotUserIdProvider botUserIdProvider,
    ITargetChannelProvider targetChannelProvider,
    CommandResponseTracker commandResponseTracker,
    ILogger<ChatSender> logger,
    PlatformChannelRegistry? channelRegistry = null)
    : IHostedComponent
{
    private const int MaxMessageLength = 500;
    private const int MaxSendAttempts = 5;
    private const int QueueCapacity = 1000;
    private const double RateLimitMaxDelaySeconds = 30.0;
    private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan MaxDrainTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan RateLimitInitialDelay = TimeSpan.FromSeconds(2);

    private Channel<ChatSendItem> _channel = CreateChannel();

    private Task? _backgroundTask;
    private CancellationTokenSource? _cts;

    private long _sentCount;
    private long _failedCount;
    private long _lastSentAtUtcTicks;

    private enum SendOutcome
    {
        Done = 0,
        RateLimited = 1,
    }

    public string Name => "Отправитель сообщений чата (Helix)";

    public int StartOrder => 50;

    public int QueueLength => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    public int QueueMaxLength => QueueCapacity;

    public long SentCount => Interlocked.Read(ref _sentCount);

    public long FailedCount => Interlocked.Read(ref _failedCount);

    public DateTimeOffset? LastSentAt
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastSentAtUtcTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_channel.Reader.Completion.IsCompleted)
        {
            _channel = CreateChannel();
        }

        _cts = new();
        _backgroundTask = RunAsync(_cts.Token);
        _ = WarmupAsync(_cts.Token);
        logger.LogInformation("ChatSender запущен");
        return Task.CompletedTask;
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        logger.LogInformation("ChatSender: ожидание отправки оставшихся сообщений...");

        _channel.Writer.TryComplete();

        if (_backgroundTask == null)
        {
            return;
        }

        var task = _backgroundTask;

        try
        {
            using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            drainCts.CancelAfter(MaxDrainTimeout);

            await task.WaitAsync(drainCts.Token);
            logger.LogInformation("ChatSender: все сообщения отправлены");
        }
        catch (OperationCanceledException timeoutEx)
        {
            var remaining = _channel.Reader.CanCount ? _channel.Reader.Count : -1;
            logger.LogWarning(timeoutEx,
                "ChatSender: дренаж очереди прерван (в очереди осталось {Remaining}) – не все сообщения отправлены, принудительная остановка",
                remaining);

            if (_cts != null)
            {
                await _cts.CancelAsync();
            }

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // expected on forced stop
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "ChatSender: фоновая задача завершилась с ошибкой при остановке");
            }
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
        }
    }

    public Task EnqueueAsync(
        string message,
        string? replyParentMessageId,
        CommandResponseMark? commandResponse,
        CancellationToken cancellationToken)
    {
        Enqueue(message, replyParentMessageId, commandResponse);
        return Task.CompletedTask;
    }

    public ChatEnqueueResult Enqueue(string message, string? replyParentMessageId, CommandResponseMark? commandResponse, ChatOrigin? origin = null)
    {
        return Enqueue(message, replyParentMessageId, commandResponse, null, origin);
    }

    public Task EnqueueWhisperAsync(
        string message,
        string toUserId,
        string fallbackReplyParentMessageId,
        CommandResponseMark? commandResponse,
        CancellationToken cancellationToken,
        ChatOrigin? origin = null)
    {
        WhisperRoute? whisper = null;

        if (string.IsNullOrWhiteSpace(toUserId))
        {
            logger.LogWarning("ChatSender: шёпот не отправлен – неизвестен id вызвавшего, ответ уходит реплаем на его сообщение");
        }
        else
        {
            whisper = new(toUserId);
        }

        Enqueue(message, fallbackReplyParentMessageId, commandResponse, whisper, origin);
        return Task.CompletedTask;
    }

    private ChatEnqueueResult Enqueue(
        string message,
        string? replyParentMessageId,
        CommandResponseMark? commandResponse,
        WhisperRoute? whisper,
        ChatOrigin? origin)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return ChatEnqueueResult.EmptyMessage;
        }

        if (origin is not null && (!origin.IsCurrent || origin.Channel.Platform != StreamingPlatform.Twitch))
        {
            return ChatEnqueueResult.NotConnected;
        }

        var target = targetChannelProvider.Current;

        if (!target.IsSendingAllowed)
        {
            logger.LogInformation("Режим наблюдателя ({Channel}): сообщение не отправлено – {Message}",
                target.Login,
                message);

            return ChatEnqueueResult.ObserverMode;
        }

        var channel = _channel;
        var anyQueued = false;
        var refusal = ChatEnqueueResult.QueueFull;

        foreach (var chunk in SplitByLength(message, MaxMessageLength))
        {
            var item = new ChatSendItem(chunk, replyParentMessageId, target.Login, commandResponse, whisper, origin);

            if (channel.Writer.TryWrite(item))
            {
                anyQueued = true;
                continue;
            }

            Interlocked.Increment(ref _failedCount);

            if (channel.Reader.Completion.IsCompleted)
            {
                logger.LogWarning("ChatSender: канал закрыт, сообщение отброшено (длина сообщения {Length} символов)", chunk.Length);
                refusal = ChatEnqueueResult.QueueClosed;
            }
            else
            {
                logger.LogWarning("ChatSender: очередь переполнена, сообщение отброшено (длина сообщения {Length} символов)", chunk.Length);
                refusal = ChatEnqueueResult.QueueFull;
            }
        }

        return anyQueued ? ChatEnqueueResult.Queued : refusal;
    }

    private static Channel<ChatSendItem> CreateChannel()
    {
        return Channel.CreateBounded<ChatSendItem>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    private static IEnumerable<string> SplitByLength(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            yield return text;
            yield break;
        }

        var index = 0;
        while (index < text.Length)
        {
            var remaining = text.Length - index;
            var take = Math.Min(maxLength, remaining);

            if (take < remaining)
            {
                var space = text.LastIndexOf(' ', index + take - 1, take);
                if (space > index)
                {
                    take = space - index;
                }
            }

            yield return text.Substring(index, take).Trim();
            index += take;
        }
    }

    private async Task WarmupAsync(CancellationToken ct)
    {
        try
        {
            await Task.WhenAll(broadcasterIdProvider.GetAsync(ct),
                botUserIdProvider.GetAsync(ct));
        }
        catch (OperationCanceledException)
        {
            // warmup is best-effort
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "ChatSender: прогрев broadcaster/bot id не удался, будет повторён при первой отправке");
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var lastSendStartedAt = DateTimeOffset.MinValue;

        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(ct))
            {
                var elapsed = DateTimeOffset.UtcNow - lastSendStartedAt;

                if (elapsed < SendInterval)
                {
                    try
                    {
                        await Task.Delay(SendInterval - elapsed, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                lastSendStartedAt = DateTimeOffset.UtcNow;
                await SendWithRetryAsync(item, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on stop
        }
    }

    private async Task SendWithRetryAsync(ChatSendItem item, CancellationToken ct)
    {
        var rateLimitDelay = RateLimitInitialDelay;

        for (var attempt = 1; attempt <= MaxSendAttempts; attempt++)
        {
            var outcome = await TrySendOnceAsync(item, ct);

            if (outcome != SendOutcome.RateLimited)
            {
                return;
            }

            if (!await WaitForRateLimitAsync(attempt, rateLimitDelay, ct))
            {
                return;
            }

            rateLimitDelay = TimeSpan.FromSeconds(Math.Min(rateLimitDelay.TotalSeconds * 2, RateLimitMaxDelaySeconds));
        }
    }

    private async Task<SendOutcome> TrySendOnceAsync(ChatSendItem item, CancellationToken ct)
    {
        try
        {
            if (!CanSend(item))
            {
                return SendOutcome.Done;
            }

            var botContextId = channelRegistry?.GetBinding(StreamingPlatform.Twitch)?.BotContextId;
            using var _ = PlatformAuthRequestOptions.UseContext(botContextId);

            var broadcasterId = item.Origin?.Channel.ChannelId ?? await broadcasterIdProvider.GetAsync(ct);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                Interlocked.Increment(ref _failedCount);
                logger.LogWarning("ChatSender: broadcaster id не получен, сообщение пропущено");
                return SendOutcome.Done;
            }

            var senderId = await botUserIdProvider.GetAsync(ct);

            if (string.IsNullOrEmpty(senderId))
            {
                Interlocked.Increment(ref _failedCount);
                logger.LogWarning("ChatSender: sender id не получен, сообщение пропущено");
                return SendOutcome.Done;
            }

            if (!CanSend(item))
            {
                return SendOutcome.Done;
            }

            if (item.Whisper is { Refused: false } whisper
                && await TryWhisperAsync(item.Message, whisper, senderId, ct))
            {
                return SendOutcome.Done;
            }

            if (!CanSend(item))
            {
                return SendOutcome.Done;
            }

            var messageId = await helix.SendChatMessageAsync(broadcasterId, senderId, item.Message, item.ReplyParentMessageId, ct);

            Interlocked.Increment(ref _sentCount);
            Interlocked.Exchange(ref _lastSentAtUtcTicks, DateTimeOffset.UtcNow.UtcTicks);

            if (item.CommandResponse is { } commandResponse && !string.IsNullOrEmpty(messageId))
            {
                commandResponseTracker.Expect(StreamingPlatform.Twitch, messageId, commandResponse, new ChannelKey(StreamingPlatform.Twitch, broadcasterId));
            }

            if (string.IsNullOrEmpty(item.ReplyParentMessageId))
            {
                logger.LogInformation("ChatSender: сообщение отправлено в канал {Channel} – {Message}",
                    item.TargetLogin,
                    item.Message);
            }
            else
            {
                logger.LogInformation("ChatSender: ответ отправлен в канал {Channel} на сообщение {ReplyParentMessageId} – {Message}",
                    item.TargetLogin,
                    item.ReplyParentMessageId,
                    item.Message);
            }

            return SendOutcome.Done;
        }
        catch (HelixMessageDroppedException ex)
        {
            Interlocked.Increment(ref _failedCount);
            logger.LogWarning(ex, "Сообщение отклонено Twitch: {Code} {Reason}", ex.ReasonCode, ex.ReasonMessage);
            return SendOutcome.Done;
        }
        catch (HelixRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return SendOutcome.RateLimited;
        }
        catch (HelixRequestException ex)
        {
            Interlocked.Increment(ref _failedCount);
            logger.LogError(ex, "ChatSender: ошибка Helix {Status} при отправке сообщения", (int)ex.StatusCode);
            return SendOutcome.Done;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return SendOutcome.Done;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _failedCount);
            logger.LogError(ex, "ChatSender: неожиданная ошибка при отправке сообщения");
            return SendOutcome.Done;
        }
    }

    [SuppressMessage("Minor Code Smell", "S6667", Justification = "Отказ Helix по шёпоту (HelixRequestException) пишется одной строкой предупреждения: причину несут статус и текст ответа Twitch, стек тут пуст по смыслу. Прочие исключения того же метода пишутся со стеком.")]
    private async Task<bool> TryWhisperAsync(string message, WhisperRoute whisper, string senderId, CancellationToken ct)
    {
        if (string.Equals(senderId, whisper.ToUserId, StringComparison.Ordinal))
        {
            whisper.Refused = true;
            logger.LogWarning("ChatSender: шёпот не отправлен – команду вызвал сам аккаунт бота, ответ уходит реплаем на его сообщение");
            return false;
        }

        try
        {
            await helix.SendWhisperAsync(senderId, whisper.ToUserId, message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (HelixRequestException ex)
        {
            whisper.Refused = true;
            logger.LogWarning("ChatSender: Twitch не принял шёпот пользователю {UserId} ({Status}: {Reason}) – ответ уходит реплаем на его сообщение",
                whisper.ToUserId,
                (int)ex.StatusCode,
                ex.TwitchErrorMessage ?? "причина не названа");

            return false;
        }
        catch (Exception ex)
        {
            whisper.Refused = true;
            logger.LogWarning(ex, "ChatSender: шёпот пользователю {UserId} не отправлен ({Error}) – ответ уходит реплаем на его сообщение",
                whisper.ToUserId,
                ex.Message);

            return false;
        }

        Interlocked.Increment(ref _sentCount);
        Interlocked.Exchange(ref _lastSentAtUtcTicks, DateTimeOffset.UtcNow.UtcTicks);

        logger.LogInformation("ChatSender: ответ отправлен шёпотом пользователю {UserId} ({Length} симв.)",
            whisper.ToUserId,
            message.Length);

        return true;
    }

    private async Task<bool> WaitForRateLimitAsync(int attempt, TimeSpan delay, CancellationToken ct)
    {
        if (attempt >= MaxSendAttempts)
        {
            Interlocked.Increment(ref _failedCount);
            logger.LogError("ChatSender: rate limit 429, исчерпаны все {MaxAttempts} попыток – сообщение отброшено", MaxSendAttempts);
            return false;
        }

        logger.LogWarning("ChatSender: rate limit 429, ожидание {Delay}с (попытка {Attempt}/{Max})",
            delay.TotalSeconds, attempt, MaxSendAttempts);

        try
        {
            await Task.Delay(delay, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private bool CanSend(ChatSendItem item)
    {
        var target = targetChannelProvider.Current;
        if (target.IsSendingAllowed
            && (item.Origin?.IsCurrent ?? string.Equals(target.Login, item.TargetLogin, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        Interlocked.Increment(ref _failedCount);
        logger.LogDebug("ChatSender: сообщение пропущено – адрес исходного чата недоступен или отправка отключена");
        return false;
    }

    private sealed record ChatSendItem(
        string Message,
        string? ReplyParentMessageId,
        string TargetLogin,
        CommandResponseMark? CommandResponse,
        WhisperRoute? Whisper,
        ChatOrigin? Origin);

    private sealed class WhisperRoute(string toUserId)
    {
        public string ToUserId { get; } = toUserId;

        public bool Refused { get; set; }
    }
}
