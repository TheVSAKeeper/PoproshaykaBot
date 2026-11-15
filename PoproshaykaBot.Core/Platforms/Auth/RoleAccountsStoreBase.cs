using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Platforms.Auth;

public abstract class RoleAccountsStoreBase<TFile, TAccount>
    where TFile : class, new()
    where TAccount : class, IRoleAccountSettings, new()
{
    private readonly JsonStore<TFile> _store;

    protected RoleAccountsStoreBase(
        string filePath,
        ILogger? logger,
        Func<string, string>? redact,
        Func<TFile, string>? describe,
        SettingsWriteGate? gate)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        FilePath = filePath;
        Logger = logger;
        _store = new(filePath, logger, redact, describe: describe, gate: gate);
    }

    protected string FilePath { get; }

    protected ILogger? Logger { get; }

    public TAccount LoadBot()
    {
        return Load(PlatformAuthRole.Bot);
    }

    public TAccount LoadBroadcaster()
    {
        return Load(PlatformAuthRole.Broadcaster);
    }

    public TAccount Load(PlatformAuthRole role)
    {
        return TakeAccount(_store.Load(), role);
    }

    public bool Mutate(PlatformAuthRole role, Action<TAccount> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        var written = _store.Mutate(state => mutator(TakeAccount(state, role)));
        OnRoleMutated(role, written);
        return written;
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken)
    {
        ArgumentNullException.ThrowIfNull(expectedToken);

        var cleared = _store.MutateIf(state =>
        {
            var account = TakeAccount(state, role);

            if (!string.Equals(account.AccessToken, expectedToken, StringComparison.Ordinal))
            {
                return false;
            }

            account.AccessToken = string.Empty;
            account.AccessTokenExpiresAt = null;
            return true;
        });

        OnAccessTokenCleared(role, cleared);
        return cleared;
    }

    protected TFile LoadFile()
    {
        return _store.Load();
    }

    protected bool MutateFile(Action<TFile> mutator)
    {
        return _store.Mutate(mutator);
    }

    protected bool SaveFile(TFile state)
    {
        return _store.Save(state);
    }

    protected TAccount TakeAccount(TFile state, PlatformAuthRole role)
    {
        PlatformAuthRoles.EnsureActive(role);

        var bot = EnsureAccount(state, PlatformAuthRole.Bot, BotDefaultScopes);
        var broadcaster = EnsureAccount(state, PlatformAuthRole.Broadcaster, BroadcasterDefaultScopes);

        return role == PlatformAuthRole.Bot ? bot : broadcaster;
    }

    protected abstract TAccount? GetAccount(TFile state, PlatformAuthRole role);

    protected abstract void SetAccount(TFile state, PlatformAuthRole role, TAccount account);

    protected abstract IReadOnlyList<string> BotDefaultScopes { get; }

    protected abstract IReadOnlyList<string> BroadcasterDefaultScopes { get; }

    protected virtual void OnRoleMutated(PlatformAuthRole role, bool written)
    {
    }

    protected virtual void OnAccessTokenCleared(PlatformAuthRole role, bool cleared)
    {
    }

    private TAccount EnsureAccount(TFile state, PlatformAuthRole role, IReadOnlyList<string> defaultScopes)
    {
        var account = GetAccount(state, role);
        if (account is null)
        {
            account = new();
            SetAccount(state, role, account);
        }

        if (account.Scopes.Length == 0)
        {
            account.Scopes = [..defaultScopes];
        }

        return account;
    }
}
