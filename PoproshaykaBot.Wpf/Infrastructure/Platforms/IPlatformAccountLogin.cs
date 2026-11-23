using KeepShell.ViewModels;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Wpf.Infrastructure.Platforms;

public interface IPlatformAccountLogin
{
    StreamingPlatform Platform { get; }

    bool CanAuthorize { get; }

    bool SupportsEmbeddedBrowser { get; }

    Task<PlatformAccountLoginResult> AuthorizeAsync(PlatformAccountLoginRequest request);
}

public sealed record PlatformAccountLoginRequest(
    IReadOnlyList<PlatformAuthRole> Roles, string? AccountId, string? ContextId,
    IReadOnlyList<string> Scopes, bool UseSystemBrowser);

public sealed record PlatformAccountLoginResult(
    string Message, StatusSeverity Severity, IReadOnlyList<AuthContextSummary> ChangedAuthorizations);
