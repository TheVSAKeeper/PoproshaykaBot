using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Core.YouTube.Streaming;
using System.Threading.Channels;

namespace PoproshaykaBot.Core.YouTube.Chat;

internal sealed class YouTubeMessenger(
    IYouTubeLiveChatSendClient sendClient,
    IYouTubeActiveBroadcastSource activeBroadcastSource,
    ITargetChannelProvider targetChannelProvider,
    IYouTubeQuotaGovernor quotaGovernor,
    IYouTubeSentMessageTracker sentMessageTracker,
    CommandResponseTracker commandResponseTracker,
    TimeProvider timeProvider,
    ILogger<YouTubeMessenger> logger,
    YouTubeSessionContext? sessionContext = null,
    YouTubeAccountsStore? accountsStore = null)
    : IPlatformMessenger, IChatAccountMessenger, IHostedComponent, IDisposable
{
    public const int QueueCapacity = 256;

    private static readonly TimeSpan SendInterval = TimeSpan.FromMilliseconds(1_000);
    private static readonly TimeSpan MaxDrainTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CommandAndGreetingTtl = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ScheduledBroadcastTtl = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan[] RateLimitDelays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
    ];

    private readonly Lock _sync = new();
    private readonly HashSet<(DateOnly PacificDate, YouTubeQuotaPriority Priority)> _loggedQuotaSuppressions = [];

    private Channel<YouTubeSendItem> _channel = CreateClosedChannel();
    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private bool _isRunning;
    private bool _disposed;

    private long _sentCount;
    private long _failedCount;
    private long _lastSentAtUtcTicks;

    public string Name => "Отправитель сообщений чата YouTube";

    public int StartOrder => 50;

    public int QueueLength => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

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
        lock (_sync)
        {
            if (_disposed || _isRunning)
            {
                return Task.CompletedTask;
            }

            _channel = CreateChannel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            _isRunning = true;
            _backgroundTask = RunAsync(_channel, cts);
        }

        logger.LogInformation("YouTubeMessenger запущен");
        return Task.CompletedTask;
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        Channel<YouTubeSendItem> channel;
        CancellationTokenSource? cts;
        Task? backgroundTask;

        lock (_sync)
        {
            if (!_isRunning && _backgroundTask is null)
            {
                return;
            }

            _isRunning = false;
            channel = _channel;
            cts = _cts;
            _cts = null;
            backgroundTask = _backgroundTask;
            _backgroundTask = null;
        }

        logger.LogInformation("YouTubeMessenger: ожидание отправки оставшихся сообщений...");
        channel.Writer.TryComplete();

        if (backgroundTask is null)
        {
            cts?.Dispose();
            return;
        }

        try
        {
            using var drainCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            drainCts.CancelAfter(MaxDrainTimeout);

            await backgroundTask.WaitAsync(drainCts.Token).ConfigureAwait(false);
            logger.LogInformation("YouTubeMessenger: все сообщения отправлены");
        }
        catch (OperationCanceledException timeoutEx)
        {
            var remaining = channel.Reader.CanCount ? channel.Reader.Count : -1;
            logger.LogWarning(
                timeoutEx,
                "YouTubeMessenger: дренаж очереди прерван (в очереди осталось {Remaining}) – принудительная остановка",
                remaining);

            if (cts is not null)
            {
                await cts.CancelAsync().ConfigureAwait(false);
            }

            try
            {
                await backgroundTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "YouTubeMessenger: фоновая задача завершилась с ошибкой при остановке");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YouTubeMessenger: фоновая задача завершилась с ошибкой при остановке");
        }
        finally
        {
            cts?.Dispose();
        }
    }

    public ChatEnqueueResult Send(
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None,
        ChatOrigin? origin = null)
    {
        return Enqueue(text, commandResponse, category, origin, null, null);
    }

    public bool CanSendAs(ChatSenderAccount account)
    {
        return account.Role == PlatformAuthRole.Bot
            && accountsStore is not null
            && accountsStore.TryLoadContext(account.Role, account.ContextId, out var settings)
            && settings is not null
            && string.Equals(settings.ChannelId, account.AccountId, StringComparison.Ordinal)
            && YouTubeScopes.IsGranted(settings, YouTubeScopes.ForceSsl)
            && PlatformAuthRoles.HasCredentials(settings.AccessToken, settings.RefreshToken);
    }

    public TrackedChatSend SendTracked(string text, ChatSendCategory category)
    {
        var completion = new TaskCompletionSource<ChatAccountSendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = Enqueue(text, null, category, null, null, null, completion);
        return new(result, result == ChatEnqueueResult.Queued ? completion.Task : null);
    }

    public async Task<ChatAccountSendResult> SendAsAsync(ChatSenderAccount account, string streamId, string text)
    {
        if (!CanSendAs(account))
        {
            return new(ChatEnqueueResult.AuthorizationRequired);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        var completion = new TaskCompletionSource<ChatAccountSendResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = Enqueue(text, null, ChatSendCategory.None, null, account, streamId, completion);
        return result == ChatEnqueueResult.Queued
            ? await completion.Task.ConfigureAwait(false)
            : new(result);
    }

    private ChatEnqueueResult Enqueue(
        string text, CommandResponseMark? commandResponse, ChatSendCategory category, ChatOrigin? origin,
        ChatSenderAccount? sender, string? expectedStreamId, TaskCompletionSource<ChatAccountSendResult>? completion = null)
    {
        var chunks = YouTubeMessageSplitter.Split(text);

        if (chunks.Count == 0)
        {
            return ChatEnqueueResult.EmptyMessage;
        }

        if (!targetChannelProvider.Current.IsSendingAllowed)
        {
            logger.LogDebug("YouTubeMessenger: отправка пропущена – включён режим наблюдателя");
            return ChatEnqueueResult.ObserverMode;
        }

        var broadcast = activeBroadcastSource.CurrentBroadcast;
        var liveChatId = origin?.ChatId ?? broadcast?.LiveChatId;
        var channelKey = origin?.Channel ?? ChannelKey.FromId(StreamingPlatform.YouTube, broadcast?.ChannelId);

        if (string.IsNullOrWhiteSpace(liveChatId) || !IsAddressCurrent(origin, broadcast)
            || (expectedStreamId is not null && !string.Equals(broadcast?.VideoId, expectedStreamId, StringComparison.Ordinal)))
        {
            logger.LogDebug("YouTubeMessenger: отправка пропущена – активный чат эфира отсутствует");
            return ChatEnqueueResult.NotConnected;
        }

        Channel<YouTubeSendItem> channel;

        lock (_sync)
        {
            if (_disposed || !_isRunning)
            {
                return ChatEnqueueResult.QueueClosed;
            }

            channel = _channel;
        }

        var priority = ResolvePriority(category, commandResponse);
        var nowUtc = timeProvider.GetUtcNow();
        var pacificDate = PacificQuotaClock.GetWindow(nowUtc).PacificDate;

        if (!quotaGovernor.CanAfford(priority, YouTubeRestLiveChatSendClient.InsertQuotaCost))
        {
            LogQuotaSuppressedOnce(pacificDate, priority, "недостаточно локальной квоты на входе");
            return ChatEnqueueResult.QuotaBudgetReached;
        }

        var ttl = priority == YouTubeQuotaPriority.ScheduledBroadcast
            ? ScheduledBroadcastTtl
            : CommandAndGreetingTtl;
        var item = new YouTubeSendItem(chunks, priority, commandResponse, liveChatId, pacificDate, nowUtc + ttl, channelKey,
            origin, sender, completion);

        if (!channel.Writer.TryWrite(item))
        {
            lock (_sync)
            {
                if (_disposed || !_isRunning || channel.Reader.Completion.IsCompleted)
                {
                    return ChatEnqueueResult.QueueClosed;
                }
            }

            logger.LogWarning("YouTubeMessenger: очередь переполнена, сообщение отброшено");
            return ChatEnqueueResult.QueueFull;
        }

        return ChatEnqueueResult.Queued;
    }

    public void Reply(
        string replyToMessageId,
        string callerDisplayName,
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None,
        ChatOrigin? origin = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var outgoingText = ShouldPrependMention(text, callerDisplayName)
            ? CommandResponseRouting.PrependCallerMention(callerDisplayName, text)
            : text;

        var effectiveCategory = category == ChatSendCategory.None && commandResponse is null
            ? ChatSendCategory.Greeting
            : category;

        _ = Send(outgoingText, commandResponse, effectiveCategory, origin);
    }

    public void Whisper(
        string toUserId,
        string fallbackReplyToMessageId,
        string callerDisplayName,
        string text,
        CommandResponseMark? commandResponse = null,
        ChatSendCategory category = ChatSendCategory.None,
        ChatOrigin? origin = null)
    {
        logger.LogDebug("Личное сообщение на YouTube перенаправлено в публичный чат (площадка не поддерживает личные сообщения)");
        Reply(fallbackReplyToMessageId, callerDisplayName, text, commandResponse, category, origin);
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        Channel<YouTubeSendItem> channel;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _isRunning = false;
            cts = _cts;
            _cts = null;
            channel = _channel;
        }

        channel.Writer.TryComplete();

        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            cts.Dispose();
        }
    }

    internal static YouTubeQuotaPriority ResolvePriority(
        ChatSendCategory category,
        CommandResponseMark? commandResponse)
    {
        return category switch
        {
            ChatSendCategory.CommandReply => YouTubeQuotaPriority.CommandReply,
            ChatSendCategory.Greeting => YouTubeQuotaPriority.StandaloneGreeting,
            ChatSendCategory.EventNotification => YouTubeQuotaPriority.StandaloneGreeting,
            ChatSendCategory.ScheduledBroadcast => YouTubeQuotaPriority.ScheduledBroadcast,
            _ => commandResponse is not null
                ? YouTubeQuotaPriority.CommandReply
                : YouTubeQuotaPriority.StandaloneGreeting,
        };
    }

    private static bool ShouldPrependMention(string text, string? callerDisplayName)
    {
        if (string.IsNullOrWhiteSpace(callerDisplayName))
        {
            return false;
        }

        var normalized = callerDisplayName.Trim().TrimStart('@').Trim();

        if (string.IsNullOrEmpty(normalized))
        {
            return false;
        }

        return !ContainsStandaloneMention(text, normalized);
    }

    private static bool ContainsStandaloneMention(string text, string normalizedName)
    {
        var startIndex = 0;

        while (startIndex <= text.Length - normalizedName.Length)
        {
            var matchIndex = text.IndexOf(normalizedName, startIndex, StringComparison.OrdinalIgnoreCase);

            if (matchIndex < 0)
            {
                return false;
            }

            var leftOk = matchIndex == 0 || !IsIdentifierChar(text[matchIndex - 1]);
            var rightIndex = matchIndex + normalizedName.Length;
            var rightOk = rightIndex >= text.Length || !IsIdentifierChar(text[rightIndex]);

            if (leftOk && rightOk)
            {
                return true;
            }

            startIndex = matchIndex + 1;
        }

        return false;
    }

    private static bool IsIdentifierChar(char ch) => char.IsLetterOrDigit(ch) || ch == '_';

    private static Channel<YouTubeSendItem> CreateChannel()
    {
        return Channel.CreateBounded<YouTubeSendItem>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    private static Channel<YouTubeSendItem> CreateClosedChannel()
    {
        var channel = CreateChannel();
        channel.Writer.TryComplete();
        return channel;
    }

    private async Task RunAsync(Channel<YouTubeSendItem> channel, CancellationTokenSource runCts)
    {
        var ct = runCts.Token;
        var lastSendStartedAt = DateTimeOffset.MinValue;

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                var status = ChatDeliveryStatus.Unconfirmed;
                var sentParts = 0;
                var sendAttempted = false;
                try
                {
                    foreach (var chunk in item.Chunks)
                    {
                        status = CheckSendChunk(item, out var rejectReason);
                        if (status != ChatDeliveryStatus.None)
                        {
                            logger.LogDebug("YouTubeMessenger: сообщение отброшено до отправки ({Reason})", rejectReason);
                            break;
                        }

                        var nowUtc = timeProvider.GetUtcNow();

                        if (lastSendStartedAt != DateTimeOffset.MinValue)
                        {
                            var elapsed = nowUtc - lastSendStartedAt;

                            if (elapsed < SendInterval)
                            {
                                await Task.Delay(SendInterval - elapsed, timeProvider, ct).ConfigureAwait(false);

                                status = CheckSendChunk(item, out var postDelayReason);
                                if (status != ChatDeliveryStatus.None)
                                {
                                    logger.LogDebug("YouTubeMessenger: сообщение отброшено после ожидания интервала ({Reason})", postDelayReason);
                                    break;
                                }
                            }
                        }

                        lastSendStartedAt = timeProvider.GetUtcNow();
                        sendAttempted = true;
                        status = await SendChunkWithRetryAsync(item, chunk, ct).ConfigureAwait(false);

                        if (status != ChatDeliveryStatus.Sent)
                        {
                            break;
                        }

                        sentParts++;
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    var cancelStatus = sendAttempted || sentParts > 0
                        ? ChatDeliveryStatus.Unconfirmed
                        : ChatDeliveryStatus.QueueClosed;
                    item.Completion?.TrySetResult(new(ChatEnqueueResult.Queued, cancelStatus, sentParts));
                    throw;
                }
                catch (Exception ex)
                {
                    status = ChatDeliveryStatus.Unconfirmed;
                    Interlocked.Increment(ref _failedCount);
                    logger.LogWarning(ex, "YouTubeMessenger: неожиданная ошибка при обработке сообщения из очереди");
                }

                item.Completion?.TrySetResult(new(ChatEnqueueResult.Queued, status, sentParts));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "YouTubeMessenger: фоновая задача очереди остановлена из-за неожиданной ошибки");
        }
        finally
        {
            channel.Writer.TryComplete();
            while (channel.Reader.TryRead(out var pending))
            {
                pending.Completion?.TrySetResult(new(ChatEnqueueResult.Queued, ChatDeliveryStatus.QueueClosed));
            }

            lock (_sync)
            {
                if (ReferenceEquals(_cts, runCts))
                {
                    _isRunning = false;
                }
            }
        }
    }

    private async Task<ChatDeliveryStatus> SendChunkWithRetryAsync(YouTubeSendItem item, string chunk, CancellationToken ct)
    {
        var unauthorizedRetries = 0;
        var rateLimitRetries = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var status = CheckSendChunk(item, out var rejectReason);
            if (status != ChatDeliveryStatus.None)
            {
                logger.LogDebug("YouTubeMessenger: повтор отменён ({Reason})", rejectReason);
                return status;
            }

            using var inFlightScope = sentMessageTracker.BeginInFlightSend(
                item.TargetLiveChatId,
                YouTubeChatMessageMapper.NormalizeMessageText(chunk));

            try
            {
                using var _ = PlatformAuthRequestOptions.UseContext(
                    item.Sender?.ContextId ?? sessionContext?.Current?.BotContextId, item.Sender?.AccountId);
                var result = await sendClient.InsertMessageAsync(
                    item.TargetLiveChatId,
                    chunk,
                    item.Priority,
                    ct).ConfigureAwait(false);

                Interlocked.Increment(ref _sentCount);
                Interlocked.Exchange(ref _lastSentAtUtcTicks, timeProvider.GetUtcNow().UtcTicks);

                if (item.CommandResponse is { } mark && !string.IsNullOrEmpty(result.MessageId))
                {
                    commandResponseTracker.Expect(StreamingPlatform.YouTube, result.MessageId, mark, item.Channel, item.TargetLiveChatId);
                }

                inFlightScope.CompleteConfirmed(result.MessageId);

                logger.LogInformation(
                    "YouTubeMessenger: сообщение отправлено в чат {LiveChatId} ({Length} симв.)",
                    item.TargetLiveChatId,
                    chunk.Length);

                return ChatDeliveryStatus.Sent;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                inFlightScope.CompleteUncertain();
                throw;
            }
            catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized)
            {
                inFlightScope.CompleteRejected();

                if (unauthorizedRetries < 1)
                {
                    unauthorizedRetries++;
                    logger.LogDebug("YouTubeMessenger: 401 при отправке, повторная попытка после сброса токена");
                    continue;
                }

                Interlocked.Increment(ref _failedCount);
                logger.LogWarning(ex, "YouTubeMessenger: отправка отклонена из-за ошибки авторизации");
                return ChatDeliveryStatus.AuthorizationRequired;
            }
            catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.RateLimitExceeded)
            {
                inFlightScope.CompleteRejected();

                if (rateLimitRetries < RateLimitDelays.Length)
                {
                    var delay = RateLimitDelays[rateLimitRetries++];
                    logger.LogWarning(
                        "YouTubeMessenger: ограничение частоты 429, повтор через {DelaySeconds} с (попытка {Attempt}/{Max})",
                        (int)delay.TotalSeconds,
                        rateLimitRetries,
                        RateLimitDelays.Length);

                    await Task.Delay(delay, timeProvider, ct).ConfigureAwait(false);
                    continue;
                }

                Interlocked.Increment(ref _failedCount);
                logger.LogWarning(ex, "YouTubeMessenger: исчерпаны повторы 429, сообщение отброшено");
                return ChatDeliveryStatus.Rejected;
            }
            catch (YouTubeApiException ex) when (ex.Kind is YouTubeApiErrorKind.LocalQuotaBudgetReached or YouTubeApiErrorKind.QuotaExceeded)
            {
                inFlightScope.CompleteRejected();

                if (ex.Kind == YouTubeApiErrorKind.QuotaExceeded)
                {
                    quotaGovernor.MarkExhaustedFromServer(PlatformAuthRole.Bot);
                }

                Interlocked.Increment(ref _failedCount);
                var currentPacificDate = PacificQuotaClock.GetWindow(timeProvider.GetUtcNow()).PacificDate;
                LogQuotaSuppressedOnce(currentPacificDate, item.Priority, ex.Message);
                return ChatDeliveryStatus.QuotaExhausted;
            }
            catch (YouTubeApiException ex) when (ex.Kind is YouTubeApiErrorKind.LiveChatEnded
                or YouTubeApiErrorKind.Forbidden
                or YouTubeApiErrorKind.BadRequest
                or YouTubeApiErrorKind.NotFound)
            {
                inFlightScope.CompleteRejected();
                Interlocked.Increment(ref _failedCount);
                logger.LogWarning(ex, "YouTubeMessenger: отправка отклонена YouTube API ({Kind})", ex.Kind);
                return ex.Kind is YouTubeApiErrorKind.LiveChatEnded or YouTubeApiErrorKind.NotFound
                    ? ChatDeliveryStatus.ChatUnavailable
                    : ChatDeliveryStatus.Rejected;
            }
            catch (Exception ex)
            {
                inFlightScope.CompleteUncertain();
                Interlocked.Increment(ref _failedCount);
                logger.LogWarning(ex, "YouTubeMessenger: отправка завершилась с неопределённым исходом – повтор запрещён во избежание дубля");
                return ChatDeliveryStatus.Unconfirmed;
            }
        }
    }

    private ChatDeliveryStatus CheckSendChunk(YouTubeSendItem item, out string reason)
    {
        if (!targetChannelProvider.Current.IsSendingAllowed)
        {
            reason = "режим наблюдателя";
            return ChatDeliveryStatus.ObserverMode;
        }

        if (item.Sender is { } sender && !CanSendAs(sender))
        {
            reason = "выбранная авторизация изменилась";
            return ChatDeliveryStatus.AuthorizationRequired;
        }

        var nowUtc = timeProvider.GetUtcNow();

        if (nowUtc >= item.ExpiresAtUtc)
        {
            reason = "истёк срок ожидания в очереди";
            return ChatDeliveryStatus.Rejected;
        }

        var currentPacificDate = PacificQuotaClock.GetWindow(nowUtc).PacificDate;

        if (currentPacificDate != item.EnqueuedPacificDate)
        {
            reason = "наступили новые тихоокеанские сутки квоты";
            return ChatDeliveryStatus.QuotaExhausted;
        }

        var broadcast = activeBroadcastSource.CurrentBroadcast;

        if (!string.Equals(broadcast?.LiveChatId, item.TargetLiveChatId, StringComparison.Ordinal)
            || item.Channel != ChannelKey.FromId(StreamingPlatform.YouTube, broadcast?.ChannelId)
            || item.Origin is { IsCurrent: false })
        {
            reason = "сменился или завершился активный чат";
            return ChatDeliveryStatus.ChatUnavailable;
        }

        reason = string.Empty;
        return ChatDeliveryStatus.None;
    }

    private static bool IsAddressCurrent(ChatOrigin? origin, YouTubeBroadcastSummary? broadcast)
    {
        return origin is null || (origin.IsCurrent
            && origin.Channel == ChannelKey.FromId(StreamingPlatform.YouTube, broadcast?.ChannelId)
            && string.Equals(origin.ChatId, broadcast?.LiveChatId, StringComparison.Ordinal));
    }

    private void LogQuotaSuppressedOnce(DateOnly pacificDate, YouTubeQuotaPriority priority, string detail)
    {
        bool firstForDayAndPriority;

        lock (_sync)
        {
            _loggedQuotaSuppressions.RemoveWhere(entry => entry.PacificDate < pacificDate);
            firstForDayAndPriority = _loggedQuotaSuppressions.Add((pacificDate, priority));
        }

        var resetAtUtc = PacificQuotaClock.GetWindow(timeProvider.GetUtcNow()).NextResetAtUtc;

        if (firstForDayAndPriority)
        {
            logger.LogWarning(
                "YouTubeMessenger: отправка приоритета {Priority} приостановлена до сброса квоты {ResetAt:O} ({Detail})",
                priority,
                resetAtUtc,
                detail);
        }
        else
        {
            logger.LogDebug(
                "YouTubeMessenger: отправка приоритета {Priority} пропущена из-за квоты ({Detail})",
                priority,
                detail);
        }
    }

    private sealed record YouTubeSendItem(
        IReadOnlyList<string> Chunks,
        YouTubeQuotaPriority Priority,
        CommandResponseMark? CommandResponse,
        string TargetLiveChatId,
        DateOnly EnqueuedPacificDate,
        DateTimeOffset ExpiresAtUtc,
        ChannelKey? Channel,
        ChatOrigin? Origin,
        ChatSenderAccount? Sender,
        TaskCompletionSource<ChatAccountSendResult>? Completion);
}
