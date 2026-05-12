using Microsoft.Extensions.Logging;
using System.Net;

namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed class ClientCredentialsValidator(
    IHttpClientFactory httpClientFactory,
    ILogger<ClientCredentialsValidator> logger)
    : IClientCredentialsValidator
{
    public async Task<ClientCredentialsValidationResult> ValidateAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return ClientCredentialsValidationResult.Empty;
        }

        try
        {
            using var http = httpClientFactory.CreateClient();
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "client_credentials",
            });

            using var response = await http.PostAsync(TwitchEndpoints.OAuthToken, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return ClientCredentialsValidationResult.Valid;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden
                or HttpStatusCode.BadRequest)
            {
                return ClientCredentialsValidationResult.Invalid;
            }

            logger.LogDebug("Twitch вернул неожиданный статус {Status} при проверке client_credentials", response.StatusCode);
            return ClientCredentialsValidationResult.NetworkError;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Сетевая ошибка при проверке client_credentials");
            return ClientCredentialsValidationResult.NetworkError;
        }
    }
}
