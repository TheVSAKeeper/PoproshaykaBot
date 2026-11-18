namespace PoproshaykaBot.Core.Platforms.Auth;

public readonly record struct PlatformAccountKey(StreamingPlatform Platform, string AccountId);

public sealed record PlatformAccount(
    PlatformAccountKey Key,
    string DisplayName,
    IReadOnlyList<AuthContextSummary> Authorizations);

public sealed record ChannelAccountAssignment(
    ChannelKey Channel,
    PlatformAuthRole Role,
    PlatformAccountKey Account,
    string ContextId,
    bool IsVerified);
