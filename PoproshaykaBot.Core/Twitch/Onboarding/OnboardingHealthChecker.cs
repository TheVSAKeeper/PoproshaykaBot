using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.EventSub;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace PoproshaykaBot.Core.Twitch.Onboarding;

public sealed class OnboardingHealthChecker(
    IHttpClientFactory httpClientFactory,
    ITwitchEventSubClient eventSubClient,
    EventSubChatMessageMapper chatMessageMapper,
    IEventBus eventBus,
    ILogger<OnboardingHealthChecker> logger)
    : IOnboardingHealthChecker
{
    public async Task<ChatTestMessageOutcome> SendChatTestMessageAsync(
        string broadcasterUserId,
        string senderUserId,
        string clientId,
        string botAccessToken,
        string message,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(broadcasterUserId)
            || string.IsNullOrWhiteSpace(senderUserId)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(botAccessToken)
            || string.IsNullOrWhiteSpace(message))
        {
            return ChatTestMessageOutcome.Skipped;
        }

        try
        {
            using var http = httpClientFactory.CreateClient();

            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{TwitchEndpoints.HelixBaseUrl}{TwitchEndpoints.HelixChatMessages}")
            {
                Content = JsonContent.Create(new
                {
                    broadcaster_id = broadcasterUserId,
                    sender_id = senderUserId,
                    message,
                }),
            };

            request.Headers.Authorization = new("Bearer", botAccessToken);
            request.Headers.TryAddWithoutValidation("Client-Id", clientId);

            using var response = await http.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ChatTestMessageOutcome.Sent;
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                logger.LogDebug("Helix chat/messages вернул {StatusCode} в onboarding health-check", response.StatusCode);
                return ChatTestMessageOutcome.Forbidden;
            }

            logger.LogDebug("Helix chat/messages вернул {StatusCode} в onboarding health-check", response.StatusCode);
            return ChatTestMessageOutcome.Error;
        }
        catch (OperationCanceledException)
        {
            return ChatTestMessageOutcome.Skipped;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Не удалось отправить тестовое сообщение в onboarding health-check");
            return ChatTestMessageOutcome.Error;
        }
    }

    public async Task<OnboardingChatPreviewStartResult> StartChatPreviewAsync(
        string broadcasterUserId,
        string botUserId,
        string clientId,
        string botAccessToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(broadcasterUserId)
            || string.IsNullOrWhiteSpace(botUserId)
            || string.IsNullOrWhiteSpace(clientId)
            || string.IsNullOrWhiteSpace(botAccessToken))
        {
            return new(OnboardingChatPreviewStatus.Error, null);
        }

        var sessionId = eventSubClient.SessionId;
        if (string.IsNullOrEmpty(sessionId))
        {
            logger.LogDebug("StartChatPreviewAsync: EventSub WebSocket ещё не подключён");
            return new(OnboardingChatPreviewStatus.EventSubNotConnected, null);
        }

        var session = new OnboardingChatPreviewSession(httpClientFactory,
            eventSubClient,
            chatMessageMapper,
            eventBus,
            broadcasterUserId,
            botUserId,
            clientId,
            botAccessToken,
            logger);

        try
        {
            var status = await session.InitializeAsync(sessionId, cancellationToken);
            if (status != OnboardingChatPreviewStatus.Started)
            {
                await session.DisposeAsync();
                return new(status, null);
            }

            return new(OnboardingChatPreviewStatus.Started, session);
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
}
