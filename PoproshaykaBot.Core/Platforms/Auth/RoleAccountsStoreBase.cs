using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Platforms.Auth;

public readonly record struct SharedRefreshSyncResult(
    bool Applied,
    bool Written,
    IReadOnlyList<(PlatformAuthRole Role, string ContextId)> SyncedSiblings);

public abstract class RoleAccountsStoreBase<TFile, TAccount> : IPlatformAccountDirectory
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

    StreamingPlatform IPlatformAccountDirectory.Platform => Platform;

    IRoleAccountSettings? IPlatformAccountDirectory.LoadAuthorization(PlatformAuthRole role, string contextId) =>
        TryLoadContext(role, contextId, out var account) ? account : null;

    public virtual IReadOnlyList<PlatformAccountOperation> GetPermissions(PlatformAuthRole role) => [];

    public bool VerifyPersistedAuthorization(PlatformAuthRole role, string contextId, string accountId)
    {
        var normalized = AuthContextIds.Normalize(contextId);
        return _store.VerifyPersisted((current, persisted) =>
        {
            var live = TryFindExistingAccount(current, role, normalized);
            var disk = TryFindExistingAccount(persisted, role, normalized);
            return live is not null && disk is not null
                && string.Equals(GetSubjectId(live)?.Trim(), accountId, StringComparison.Ordinal)
                && string.Equals(GetSubjectId(disk)?.Trim(), accountId, StringComparison.Ordinal)
                && string.Equals(live.AccessToken, disk.AccessToken, StringComparison.Ordinal)
                && string.Equals(live.RefreshToken, disk.RefreshToken, StringComparison.Ordinal)
                && live.AccessTokenExpiresAt == disk.AccessTokenExpiresAt
                && live.Scopes.SequenceEqual(disk.Scopes, StringComparer.Ordinal)
                && live.StoredScopes.SequenceEqual(disk.StoredScopes, StringComparer.Ordinal)
                && GetChatBrowserProfile(live) == GetChatBrowserProfile(disk);
        });
    }

    bool IPlatformAccountDirectory.CanRemove => true;

    public abstract (IReadOnlyList<(PlatformAuthRole Role, string ContextId)> Removed, bool Written) RemoveAccount(string accountId);

    public abstract (IReadOnlyList<string> RemovedContextIds, bool Written) RemoveAccountRole(string accountId, PlatformAuthRole role);

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
        return UpsertContexts([(role, contextId, label, account)]);
    }

    public bool UpsertContexts(IReadOnlyList<(PlatformAuthRole Role, string ContextId, string Label, TAccount Account)> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
        {
            return true;
        }

        foreach (var entry in entries)
        {
            PlatformAuthRoles.EnsureActive(entry.Role);
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.ContextId);
            ArgumentNullException.ThrowIfNull(entry.Account);
        }

        var written = _store.Mutate(state =>
        {
            foreach (var entry in entries)
            {
                var normalized = AuthContextIds.Normalize(entry.ContextId);
                if (AuthContextIds.IsDefault(normalized))
                {
                    var target = TakeAccount(state, entry.Role, null);
                    CopyAccountFields(entry.Account, target);
                    EnsureDefaultScopes(target, entry.Role);
                    continue;
                }

                var contexts = GetContexts(state);
                var existing = contexts.FirstOrDefault(c =>
                    string.Equals(c.ContextId, normalized, StringComparison.Ordinal));
                if (existing is not null)
                {
                    CopyAccountFields(entry.Account, existing.Account);
                    EnsureDefaultScopes(existing.Account, entry.Role);
                    existing.Role = entry.Role;
                    existing.Label = string.IsNullOrWhiteSpace(entry.Label) ? normalized : entry.Label.Trim();
                }
                else
                {
                    var account = new TAccount();
                    CopyAccountFields(entry.Account, account);
                    EnsureDefaultScopes(account, entry.Role);
                    contexts.Add(new()
                    {
                        ContextId = normalized,
                        Role = entry.Role,
                        Label = string.IsNullOrWhiteSpace(entry.Label) ? normalized : entry.Label.Trim(),
                        Account = account,
                    });
                }
            }

            OnContextsUpserted(state, entries);
        });

        foreach (var role in entries.Select(static e => e.Role).Distinct())
        {
            OnRoleMutated(role, written);
        }

        return written;
    }

    protected virtual void OnContextsUpserted(
        TFile state,
        IReadOnlyList<(PlatformAuthRole Role, string ContextId, string Label, TAccount Account)> entries)
    {
    }

    public (bool Applied, bool Written) TryMutateExisting(PlatformAuthRole role, string? contextId, Func<TAccount, bool> mutator)
    {
        PlatformAuthRoles.EnsureActive(role);
        ArgumentNullException.ThrowIfNull(mutator);

        var normalized = AuthContextIds.Normalize(contextId);
        var (applied, written) = _store.MutateIfWithResult(state =>
        {
            var account = TryFindExistingAccount(state, role, normalized);
            if (account is null)
            {
                return false;
            }

            EnsureDefaultScopes(account, role);
            return mutator(account);
        });

        if (applied)
        {
            OnRoleMutated(role, written);
        }

        return (applied, written);
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

    protected (bool Changed, bool Written) RemoveRoleContextCore(
        PlatformAuthRole role,
        string? contextId,
        Action<TAccount> clearDefault)
    {
        PlatformAuthRoles.EnsureActive(role);
        ArgumentNullException.ThrowIfNull(clearDefault);

        var normalized = AuthContextIds.Normalize(contextId);
        var changed = false;
        var written = _store.Mutate(state =>
        {
            if (AuthContextIds.IsDefault(normalized))
            {
                var account = TakeAccount(state, role, null);
                changed = !string.IsNullOrWhiteSpace(account.AccessToken)
                    || !string.IsNullOrWhiteSpace(account.RefreshToken)
                    || !string.IsNullOrWhiteSpace(GetSubjectId(account))
                    || account.StoredScopes.Length > 0;
                clearDefault(account);
                return;
            }

            var contexts = GetContexts(state);
            var count = contexts.RemoveAll(c =>
                c.Role == role && string.Equals(c.ContextId, normalized, StringComparison.Ordinal));
            changed = count > 0;
            ClearContextsIfEmpty(state);
        });

        if (changed)
        {
            OnRoleMutated(role, written);
        }

        return (changed, written);
    }

    protected (IReadOnlyList<(PlatformAuthRole Role, string ContextId)> Removed, bool Written) RemoveAccountCore(
        string subjectId,
        Action<TAccount> clearDefault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentNullException.ThrowIfNull(clearDefault);

        var targetId = subjectId.Trim();
        var removed = new List<(PlatformAuthRole Role, string ContextId)>();
        var written = _store.Mutate(state =>
        {
            foreach (var role in new[] { PlatformAuthRole.Bot, PlatformAuthRole.Broadcaster })
            {
                var account = GetAccount(state, role);
                if (account is not null && string.Equals(GetSubjectId(account)?.Trim(), targetId, StringComparison.Ordinal))
                {
                    clearDefault(account);
                    removed.Add((role, AuthContextIds.Default));
                }
            }

            var contexts = GetContexts(state);
            for (var i = contexts.Count - 1; i >= 0; i--)
            {
                var item = contexts[i];
                if (string.Equals(GetSubjectId(item.Account)?.Trim(), targetId, StringComparison.Ordinal))
                {
                    removed.Add((item.Role, AuthContextIds.Normalize(item.ContextId)));
                    contexts.RemoveAt(i);
                }
            }

            ClearContextsIfEmpty(state);
        });

        foreach (var role in removed.Select(static r => r.Role).Distinct())
        {
            OnRoleMutated(role, written);
        }

        return (removed, written);
    }

    protected (IReadOnlyList<string> RemovedContextIds, bool Written) RemoveAccountRoleCore(
        string subjectId,
        PlatformAuthRole role,
        Action<TAccount> clearDefault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        PlatformAuthRoles.EnsureActive(role);
        ArgumentNullException.ThrowIfNull(clearDefault);

        var targetId = subjectId.Trim();
        var removed = new List<string>();
        var written = _store.Mutate(state =>
        {
            var defaultAccount = GetAccount(state, role);
            if (defaultAccount is not null && string.Equals(GetSubjectId(defaultAccount)?.Trim(), targetId, StringComparison.Ordinal))
            {
                clearDefault(defaultAccount);
                removed.Add(AuthContextIds.Default);
            }

            var contexts = GetContexts(state);
            for (var i = contexts.Count - 1; i >= 0; i--)
            {
                var item = contexts[i];
                if (item.Role == role && string.Equals(GetSubjectId(item.Account)?.Trim(), targetId, StringComparison.Ordinal))
                {
                    removed.Add(AuthContextIds.Normalize(item.ContextId));
                    contexts.RemoveAt(i);
                }
            }

            ClearContextsIfEmpty(state);
        });

        if (removed.Count > 0)
        {
            OnRoleMutated(role, written);
        }

        return (removed, written);
    }

    public SharedRefreshSyncResult MutateAndSyncSharedRefreshToken(
        PlatformAuthRole primaryRole,
        string? primaryContextId,
        string? previousRefreshToken,
        Action<TAccount> updatePrimary,
        Action<TAccount> updateSibling,
        string? expectedSubjectId = null)
    {
        PlatformAuthRoles.EnsureActive(primaryRole);
        ArgumentNullException.ThrowIfNull(updatePrimary);
        ArgumentNullException.ThrowIfNull(updateSibling);

        var normalized = AuthContextIds.Normalize(primaryContextId);
        var updatedSiblings = new List<(PlatformAuthRole Role, string ContextId)>();
        var primaryUpdated = false;

        var (applied, written) = _store.MutateIfWithResult(state =>
        {
            var primary = TryFindExistingAccount(state, primaryRole, normalized);
            string? primarySubject = null;

            if (primary is not null
                && !string.IsNullOrWhiteSpace(previousRefreshToken)
                && string.Equals(primary.RefreshToken, previousRefreshToken, StringComparison.Ordinal))
            {
                primarySubject = GetSubjectId(primary)?.Trim();
                updatePrimary(primary);
                primaryUpdated = true;
                var resolvedSubject = GetSubjectId(primary)?.Trim();
                if (!string.IsNullOrWhiteSpace(resolvedSubject))
                {
                    primarySubject = resolvedSubject;
                }
            }
            else if (!string.IsNullOrWhiteSpace(expectedSubjectId))
            {
                primarySubject = expectedSubjectId.Trim();
            }

            if (!string.IsNullOrWhiteSpace(previousRefreshToken) && !string.IsNullOrWhiteSpace(primarySubject))
            {
                foreach (var role in new[] { PlatformAuthRole.Bot, PlatformAuthRole.Broadcaster })
                {
                    if (role == primaryRole && AuthContextIds.IsDefault(normalized))
                    {
                        continue;
                    }

                    var candidate = GetAccount(state, role);
                    if (candidate is not null
                        && string.Equals(GetSubjectId(candidate)?.Trim(), primarySubject, StringComparison.Ordinal)
                        && string.Equals(candidate.RefreshToken, previousRefreshToken, StringComparison.Ordinal))
                    {
                        updateSibling(candidate);
                        updatedSiblings.Add((role, AuthContextIds.Default));
                    }
                }

                foreach (var item in GetContexts(state))
                {
                    var itemContext = AuthContextIds.Normalize(item.ContextId);
                    if (item.Role == primaryRole && string.Equals(itemContext, normalized, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (string.Equals(GetSubjectId(item.Account)?.Trim(), primarySubject, StringComparison.Ordinal)
                        && string.Equals(item.Account.RefreshToken, previousRefreshToken, StringComparison.Ordinal))
                    {
                        updateSibling(item.Account);
                        updatedSiblings.Add((item.Role, itemContext));
                    }
                }
            }

            return primaryUpdated || updatedSiblings.Count > 0;
        });

        if (primaryUpdated)
        {
            OnRoleMutated(primaryRole, written);
        }

        foreach (var siblingRole in updatedSiblings.Select(static s => s.Role).Distinct())
        {
            if (!primaryUpdated || siblingRole != primaryRole)
            {
                OnRoleMutated(siblingRole, written);
            }
        }

        return new(applied, written, updatedSiblings);
    }

    private TAccount? TryFindExistingAccount(TFile state, PlatformAuthRole role, string normalizedContextId)
    {
        if (AuthContextIds.IsDefault(normalizedContextId))
        {
            return GetAccount(state, role);
        }

        return GetContexts(state).FirstOrDefault(c =>
            c.Role == role && string.Equals(c.ContextId, normalizedContextId, StringComparison.Ordinal))?.Account;
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

    protected virtual PlatformChatBrowserProfile? GetChatBrowserProfile(TAccount account) => null;

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
            isDefault)
        {
            ChatBrowserProfile = GetChatBrowserProfile(account),
        };
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
