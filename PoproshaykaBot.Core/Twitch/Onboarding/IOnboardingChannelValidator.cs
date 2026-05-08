namespace PoproshaykaBot.Core.Twitch.Onboarding;

public interface IOnboardingChannelValidator
{
    Task<ChannelValidationResult> ValidateAsync(
        string login,
        string clientId,
        string accessToken,
        CancellationToken cancellationToken);

    Task<ChannelValidationResult> ValidateWithAppTokenAsync(
        string login,
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken);
}

public enum ChannelValidationResult
{
    Skipped = 0,
    Found = 1,
    NotFound = 2,
}
