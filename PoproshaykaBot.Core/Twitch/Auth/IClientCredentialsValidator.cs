namespace PoproshaykaBot.Core.Twitch.Auth;

public interface IClientCredentialsValidator
{
    Task<ClientCredentialsValidationResult> ValidateAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken);
}

public enum ClientCredentialsValidationResult
{
    Empty = 0,
    Valid = 1,
    Invalid = 2,
    NetworkError = 3,
}
