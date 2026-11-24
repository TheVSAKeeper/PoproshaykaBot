namespace PoproshaykaBot.Core.Platforms.Auth;

public enum PlatformAuthMethodStatus
{
    None = 0,
    Ready = 1,
    Missing = 2,
    Attention = 3,
}

public sealed record PlatformAuthMethodDescriptor(
    string Id,
    string Title,
    PlatformAuthMethodStatus Status,
    string StatusText,
    string? Description = null);
