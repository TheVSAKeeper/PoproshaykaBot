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
        return Load(role, null);
    }

    public TAccount Load(PlatformAuthRole role, string? contextId)
    {
        return TakeAccount(_store.Load(), role, contextId);
    }

    public bool TryLoadContext(PlatformAuthRole role, string? contextId, out TAccount? account)
    {
        PlatformAuthRoles.EnsureActive(role);
        var normalized = AuthContextIds.Normalize(contextId);
        var state = _store.Load();
        if (AuthContextIds.IsDefault(normalized))
        {
            account = TakeAccount(state, role, null);
            return true;
        }

        var named = GetContexts(state).FirstOrDefault(c =>
            c.Role == role && string.Equals(c.ContextId, normalized, StringComparison.Ordinal));
        if (named is null)
        {
            account = null;
            return false;
        }

        EnsureDefaultScopes(named.Account, role);
        account = named.Account;
        return true;
    }

    public IReadOnlyList<AuthContextSummary> ListContexts(PlatformAuthRole role)
    {
        PlatformAuthRoles.EnsureActive(role);
        return ListContexts(_store.Load(), role);
    }

    public IReadOnlyList<PlatformAccount> ListAccounts()
    {
        var state = _store.Load();
        return ListContexts(state, PlatformAuthRole.Broadcaster)
            .Concat(ListContexts(state, PlatformAuthRole.Bot))
            .Where(context => !string.IsNullOrWhiteSpace(context.SubjectId))
            .GroupBy(context => context.SubjectId!.Trim(), StringComparer.Ordinal)
            .Select(group => new PlatformAccount(
                new(Platform, group.Key),
                group.Select(context => context.DisplayLabel).FirstOrDefault(label => !string.IsNullOrWhiteSpace(label))
                    ?? group.Key,
                group.ToArray()))
            .ToArray();
    }

    protected abstract StreamingPlatform Platform { get; }

    private IReadOnlyList<AuthContextSummary> ListContexts(TFile state, PlatformAuthRole role)
    {
        var defaultAccount = TakeAccount(state, role, null);
        var result = new List<AuthContextSummary>
        {
            BuildSummary(
                AuthContextIds.Default,
                role,
                "Основной",
                defaultAccount,
                isDefault: true),
        };

        foreach (var item in GetContexts(state))
        {
            if (item.Role != role || string.IsNullOrWhiteSpace(item.ContextId) || AuthContextIds.IsDefault(item.ContextId))
            {
                continue;
            }

            result.Add(BuildSummary(
                item.ContextId.Trim(),
                role,
                string.IsNullOrWhiteSpace(item.Label) ? item.ContextId.Trim() : item.Label.Trim(),
                item.Account,
                isDefault: false));
        }

        return result;
    }

    public IReadOnlyList<TAccount> GetAllAccounts(PlatformAuthRole role)
    {
        PlatformAuthRoles.EnsureActive(role);
        var state = _store.Load();
        var list = new List<TAccount> { TakeAccount(state, role, null) };
        foreach (var item in GetContexts(state))
        {
            if (item.Role == role && item.Account is not null)
            {
                list.Add(item.Account);
            }
        }

        return list;
    }

    public bool Mutate(PlatformAuthRole role, Action<TAccount> mutator)
    {
        return Mutate(role, null, mutator);
    }

    public bool Mutate(PlatformAuthRole role, string? contextId, Action<TAccount> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        var written = _store.Mutate(state => mutator(TakeAccount(state, role, contextId)));
        OnRoleMutated(role, written);
        return written;
    }

    public bool UpsertContext(PlatformAuthRole role, string contextId, string label, TAccount account)
    {
        PlatformAuthRoles.EnsureActive(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);
        ArgumentNullException.ThrowIfNull(account);

        var normalized = AuthContextIds.Normalize(contextId);
        if (AuthContextIds.IsDefault(normalized))
        {
            return Mutate(role, target =>
            {
                CopyAccountFields(account, target);
            });
        }

        var written = _store.Mutate(state =>
        {
            var contexts = GetContexts(state);
            var existing = contexts.FirstOrDefault(c =>
                string.Equals(c.ContextId, normalized, StringComparison.Ordinal));
            EnsureDefaultScopes(account, role);

            if (existing is not null)
            {
                existing.Role = role;
                existing.Label = string.IsNullOrWhiteSpace(label) ? normalized : label.Trim();
                existing.Account = account;
            }
            else
            {
                contexts.Add(new()
                {
                    ContextId = normalized,
                    Role = role,
                    Label = string.IsNullOrWhiteSpace(label) ? normalized : label.Trim(),
                    Account = account,
                });
            }
        });

        OnRoleMutated(role, written);
        return written;
    }

    public bool RemoveContext(string contextId)
    {
        if (string.IsNullOrWhiteSpace(contextId) || AuthContextIds.IsDefault(contextId))
        {
            return false;
        }

        var normalized = contextId.Trim();
        var removed = false;
        var written = _store.Mutate(state =>
        {
            var contexts = GetContexts(state);
            var count = contexts.RemoveAll(c => string.Equals(c.ContextId, normalized, StringComparison.Ordinal));
            removed = count > 0;
            ClearContextsIfEmpty(state);
        });

        return removed && written;
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken)
    {
        return TryClearAccessToken(role, null, expectedToken);
    }

    public bool TryClearAccessToken(PlatformAuthRole role, string? contextId, string expectedToken)
    {
        ArgumentNullException.ThrowIfNull(expectedToken);

        var cleared = _store.MutateIf(state =>
        {
            var account = TakeAccount(state, role, contextId);

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
        return TakeAccount(state, role, null);
    }

    protected TAccount TakeAccount(TFile state, PlatformAuthRole role, string? contextId)
    {
        PlatformAuthRoles.EnsureActive(role);
        var normalized = AuthContextIds.Normalize(contextId);
        if (AuthContextIds.IsDefault(normalized))
        {
            var bot = EnsureAccount(state, PlatformAuthRole.Bot, BotDefaultScopes);
            var broadcaster = EnsureAccount(state, PlatformAuthRole.Broadcaster, BroadcasterDefaultScopes);
            return role == PlatformAuthRole.Bot ? bot : broadcaster;
        }

        var contexts = GetContexts(state);
        var entry = contexts.FirstOrDefault(c =>
            c.Role == role && string.Equals(c.ContextId, normalized, StringComparison.Ordinal));
        if (entry is null)
        {
            entry = new()
            {
                ContextId = normalized,
                Role = role,
                Label = normalized,
                Account = new(),
            };
            contexts.Add(entry);
        }

        EnsureDefaultScopes(entry.Account, role);
        return entry.Account;
    }

    protected abstract TAccount? GetAccount(TFile state, PlatformAuthRole role);

    protected abstract void SetAccount(TFile state, PlatformAuthRole role, TAccount account);

    protected virtual List<NamedRoleAccount<TAccount>> GetContexts(TFile state) => [];

    protected virtual void ClearContextsIfEmpty(TFile state)
    {
    }

    protected virtual string? GetSubjectId(TAccount account) => null;

    protected virtual string? GetDisplayLabel(TAccount account) => null;

    protected virtual void CopyAccountFields(TAccount source, TAccount target)
    {
        target.AccessToken = source.AccessToken;
        target.RefreshToken = source.RefreshToken;
        target.Scopes = [.. source.Scopes];
        target.StoredScopes = [.. source.StoredScopes];
        target.AccessTokenExpiresAt = source.AccessTokenExpiresAt;
    }

    protected abstract IReadOnlyList<string> BotDefaultScopes { get; }

    protected abstract IReadOnlyList<string> BroadcasterDefaultScopes { get; }

    protected virtual void OnRoleMutated(PlatformAuthRole role, bool written)
    {
    }

    protected virtual void OnAccessTokenCleared(PlatformAuthRole role, bool cleared)
    {
    }

    private AuthContextSummary BuildSummary(
        string contextId,
        PlatformAuthRole role,
        string label,
        TAccount account,
        bool isDefault)
    {
        var subjectId = GetSubjectId(account);
        var displayLabel = GetDisplayLabel(account);
        var hasToken = !string.IsNullOrWhiteSpace(account.AccessToken) || !string.IsNullOrWhiteSpace(account.RefreshToken);
        return new(
            contextId,
            role,
            label,
            string.IsNullOrWhiteSpace(subjectId) ? null : subjectId,
            string.IsNullOrWhiteSpace(displayLabel) ? null : displayLabel,
            hasToken,
            isDefault);
    }

    private void EnsureDefaultScopes(TAccount account, PlatformAuthRole role)
    {
        var defaultScopes = role == PlatformAuthRole.Bot ? BotDefaultScopes : BroadcasterDefaultScopes;
        if (account.Scopes.Length == 0)
        {
            account.Scopes = [.. defaultScopes];
        }
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
