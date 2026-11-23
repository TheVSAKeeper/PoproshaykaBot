namespace PoproshaykaBot.Core.Platforms.Auth;

public static class AuthContextIds
{
    public const string Default = "default";

    public static string Normalize(string? contextId)
    {
        if (string.IsNullOrWhiteSpace(contextId)
            || string.Equals(contextId.Trim(), Default, StringComparison.OrdinalIgnoreCase))
        {
            return Default;
        }

        return contextId.Trim();
    }

    public static bool IsDefault(string? contextId)
    {
        return string.IsNullOrWhiteSpace(contextId)
            || string.Equals(contextId.Trim(), Default, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class NamedRoleAccount<TAccount>
    where TAccount : class, IRoleAccountSettings, new()
{
    public string ContextId { get; set; } = string.Empty;
    public PlatformAuthRole Role { get; set; }
    public string Label { get; set; } = string.Empty;
    public TAccount Account { get; set; } = new();
}

public sealed record AuthContextSummary(
    string ContextId,
    PlatformAuthRole Role,
    string Label,
    string? SubjectId,
    string? DisplayLabel,
    bool HasToken,
    bool IsDefault)
{
    public PlatformChatBrowserProfile? ChatBrowserProfile { get; init; }
}
