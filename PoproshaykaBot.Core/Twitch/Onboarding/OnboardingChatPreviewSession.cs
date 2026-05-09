using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.EventSub;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PoproshaykaBot.Core.Twitch.Onboarding;

internal sealed class OnboardingChatPreviewSession(
    IHttpClientFactory httpClientFactory,
    ITwitchEventSubClient eventSubClient,
    EventSubChatMessageMapper chatMessageMapper,
    IEventBus eventBus,
    string broadcasterUserId,
    string botUserId,
    string clientId,
    string botAccessToken,
    ILogger logger)
    : IAsyncDisposable
{
    private readonly object _stateLock = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private string? _subscriptionId;
    private bool _disposed;
    private bool _handlersAttached;

    public async Task<OnboardingChatPreviewStatus> InitializeAsync(string sessionId, CancellationToken cancellationToken)
    {
        var status = await CreateSubscriptionAsync(sessionId, cancellationToken);
        if (status != OnboardingChatPreviewStatus.Started)
        {
            return status;
        }

        AttachHandlers();
        return OnboardingChatPreviewStatus.Started;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        DetachHandlers();

        try
        {
            _disposeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        var subscriptionId = _subscriptionId;
        if (!string.IsNullOrEmpty(subscriptionId))
        {
            await TryDeleteSubscriptionAsync(subscriptionId);
        }

        _disposeCts.Dispose();
    }

    private async Task OnSessionWelcomeAsync(EventSubSessionWelcomeArgs args, CancellationToken ct)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var status = await CreateSubscriptionAsync(args.SessionId, ct);
            if (status != OnboardingChatPreviewStatus.Started)
            {
                logger.LogDebug("Onboarding chat preview: повторная подписка channel.chat.message не удалась со статусом {Status}", status);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Onboarding chat preview: ошибка повторной подписки channel.chat.message");
        }
    }

    private async Task OnNotificationAsync(EventSubNotificationArgs args, CancellationToken ct)
    {
        if (_disposed)
        {
            return;
        }

        if (!string.Equals(args.SubscriptionType, "channel.chat.message", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var chatMessage = chatMessageMapper.Map(args.Payload);
            var timestamp = new DateTimeOffset(args.MessageTimestamp, TimeSpan.Zero);
            await eventBus.PublishAsync(new RawChatMessageReceived(chatMessage, timestamp), ct);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Onboarding chat preview: ошибка маппинга channel.chat.message");
        }
    }

    private void AttachHandlers()
    {
        lock (_stateLock)
        {
            if (_handlersAttached || _disposed)
            {
                return;
            }

            eventSubClient.OnNotification += OnNotificationAsync;
            eventSubClient.OnSessionWelcome += OnSessionWelcomeAsync;
            _handlersAttached = true;
        }
    }

    private void DetachHandlers()
    {
        lock (_stateLock)
        {
            if (!_handlersAttached)
            {
                return;
            }

            eventSubClient.OnNotification -= OnNotificationAsync;
            eventSubClient.OnSessionWelcome -= OnSessionWelcomeAsync;
            _handlersAttached = false;
        }
    }

    private async Task<OnboardingChatPreviewStatus> CreateSubscriptionAsync(string sessionId, CancellationToken cancellationToken)
    {
        try
        {
            using var http = httpClientFactory.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{TwitchEndpoints.HelixBaseUrl}{TwitchEndpoints.HelixEventSubSubscriptions}")
            {
                Content = JsonContent.Create(new
                {
                    type = "channel.chat.message",
                    version = "1",
                    condition = new
                    {
                        broadcaster_user_id = broadcasterUserId,
                        user_id = botUserId,
                    },
                    transport = new
                    {
                        method = "websocket",
                        session_id = sessionId,
                    },
                }),
            };

            request.Headers.Authorization = new("Bearer", botAccessToken);
            request.Headers.TryAddWithoutValidation("Client-Id", clientId);

            using var response = await http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                logger.LogDebug("Onboarding chat preview: подписка channel.chat.message уже существует — переиспользуем");
                return OnboardingChatPreviewStatus.Started;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                logger.LogDebug("Onboarding chat preview: Helix вернул {StatusCode} при создании подписки", response.StatusCode);
                return OnboardingChatPreviewStatus.Forbidden;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Onboarding chat preview: Helix вернул {StatusCode} при создании подписки", response.StatusCode);
                return OnboardingChatPreviewStatus.Error;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (doc.RootElement.TryGetProperty("data", out var data)
                && data.ValueKind == JsonValueKind.Array
                && data.GetArrayLength() > 0
                && data[0].TryGetProperty("id", out var idEl))
            {
                _subscriptionId = idEl.GetString();
                logger.LogInformation("Onboarding chat preview: подписка channel.chat.message создана (id={Id}, broadcaster={BroadcasterId}, bot={BotId})",
                    _subscriptionId,
                    broadcasterUserId,
                    botUserId);
            }

            return OnboardingChatPreviewStatus.Started;
        }
        catch (OperationCanceledException)
        {
            return OnboardingChatPreviewStatus.Error;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Onboarding chat preview: исключение при создании подписки");
            return OnboardingChatPreviewStatus.Error;
        }
    }

    private async Task TryDeleteSubscriptionAsync(string subscriptionId)
    {
        try
        {
            using var deleteCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var http = httpClientFactory.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Delete,
                $"{TwitchEndpoints.HelixBaseUrl}{TwitchEndpoints.HelixEventSubSubscriptions}?id={Uri.EscapeDataString(subscriptionId)}");

            request.Headers.Authorization = new("Bearer", botAccessToken);
            request.Headers.TryAddWithoutValidation("Client-Id", clientId);

            using var response = await http.SendAsync(request, deleteCts.Token);
            logger.LogDebug("Onboarding chat preview: DELETE подписки {Id} вернул {StatusCode}", subscriptionId, response.StatusCode);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Onboarding chat preview: не удалось удалить подписку {Id}", subscriptionId);
        }
    }
}
