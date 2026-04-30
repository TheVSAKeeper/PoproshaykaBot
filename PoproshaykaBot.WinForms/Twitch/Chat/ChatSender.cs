using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Hosting;
using PoproshaykaBot.WinForms.Twitch.Helix;
using System.Net;
using System.Threading.Channels;

namespace PoproshaykaBot.WinForms.Twitch.Chat;

public sealed class ChatSender(
    [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
    ITwitchHelixClient helix,
    IBroadcasterIdProvider broadcasterIdProvider,
    IBotUserIdProvider botUserIdProvider,
    ILogger<ChatSender> logger)
    : IHostedComponent
{
    private const int MaxMessageLength = 500;
    private const int MaxSendAttempts = 5;
    private const double RateLimitMaxDelaySeconds = 30.0;
    private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan StopDrainTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RateLimitInitialDelay = TimeSpan.FromSeconds(2);

    private Channel<ChatSendItem> _channel = CreateChannel();

    private Task? _backgroundTask;
    private CancellationTokenSource? _cts;

    public string Name => "Отправитель сообщений чата (Helix)";

    public int StartOrder => 50;

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

    private static Channel<ChatSendItem> CreateChannel()
    {
        return Channel.CreateBounded<ChatSendItem>(new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
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
            drainCts.CancelAfter(StopDrainTimeout);

            await task.WaitAsync(drainCts.Token);
            logger.LogInformation("ChatSender: все сообщения отправлены");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("ChatSender: не все сообщения успели отправиться (timeout), принудительная остановка");
            _cts?.Cancel();

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
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

    public async Task EnqueueAsync(
        string message,
        string? replyParentMessageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var channel = _channel;

        foreach (var chunk in SplitByLength(message, MaxMessageLength))
        {
            var item = new ChatSendItem(chunk, replyParentMessageId);

            if (channel.Writer.TryWrite(item))
            {
                continue;
            }

            if (channel.Reader.Completion.IsCompleted)
            {
                logger.LogWarning("ChatSender: канал закрыт, сообщение отброшено (длина сообщения {Length} символов)", chunk.Length);
            }
            else
            {
                logger.LogWarning("ChatSender: очередь переполнена, сообщение отброшено (длина сообщения {Length} символов)", chunk.Length);
            }
        }
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
        }
    }

    private async Task SendWithRetryAsync(ChatSendItem item, CancellationToken ct)
    {
        var rateLimitDelay = RateLimitInitialDelay;

        for (var attempt = 1; attempt <= MaxSendAttempts; attempt++)
        {
            try
            {
                var broadcasterId = await broadcasterIdProvider.GetAsync(ct);

                if (string.IsNullOrEmpty(broadcasterId))
                {
                    logger.LogWarning("ChatSender: broadcaster id не получен, сообщение пропущено");
                    return;
                }

                var senderId = await botUserIdProvider.GetAsync(ct);

                if (string.IsNullOrEmpty(senderId))
                {
                    logger.LogWarning("ChatSender: sender id не получен, сообщение пропущено");
                    return;
                }

                await helix.SendChatMessageAsync(broadcasterId, senderId, item.Message, item.ReplyParentMessageId, ct);
                return;
            }
            catch (HelixMessageDroppedException ex)
            {
                logger.LogWarning("Сообщение отклонено Twitch: {Code} {Reason}", ex.ReasonCode, ex.ReasonMessage);
                return;
            }
            catch (HelixRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                if (attempt >= MaxSendAttempts)
                {
                    logger.LogError("ChatSender: rate limit 429, исчерпаны все {MaxAttempts} попыток — сообщение отброшено", MaxSendAttempts);
                    return;
                }

                logger.LogWarning("ChatSender: rate limit 429, ожидание {Delay}с (попытка {Attempt}/{Max})",
                    rateLimitDelay.TotalSeconds, attempt, MaxSendAttempts);

                try
                {
                    await Task.Delay(rateLimitDelay, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                rateLimitDelay = TimeSpan.FromSeconds(Math.Min(rateLimitDelay.TotalSeconds * 2, RateLimitMaxDelaySeconds));
            }
            catch (HelixRequestException ex)
            {
                logger.LogError("ChatSender: ошибка Helix {Status} при отправке сообщения", (int)ex.StatusCode);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ChatSender: неожиданная ошибка при отправке сообщения");
                return;
            }
        }
    }

    private sealed record ChatSendItem(string Message, string? ReplyParentMessageId);
}
