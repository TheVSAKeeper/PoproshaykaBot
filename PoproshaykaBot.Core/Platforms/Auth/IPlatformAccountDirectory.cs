namespace PoproshaykaBot.Core.Platforms.Auth;

public interface IPlatformAccountDirectory
{
    StreamingPlatform Platform { get; }

    IReadOnlyList<PlatformAccount> ListAccounts();

    IRoleAccountSettings? LoadAuthorization(PlatformAuthRole role, string contextId);

    IReadOnlyList<PlatformAccountOperation> GetPermissions(PlatformAuthRole role);

    bool VerifyPersistedAuthorization(PlatformAuthRole role, string contextId, string accountId);

    bool CanRemove => false;

    (IReadOnlyList<(PlatformAuthRole Role, string ContextId)> Removed, bool Written) RemoveAccount(string accountId) =>
        ([], false);

    (IReadOnlyList<string> RemovedContextIds, bool Written) RemoveAccountRole(string accountId, PlatformAuthRole role) =>
        ([], false);
}

public sealed record PlatformAccountOperation(string Name, bool IsChannelScoped, params string[] RequiredScopes);

public sealed record PlatformChatBrowserProfile(string UserDataFolder, string ProfileName);
