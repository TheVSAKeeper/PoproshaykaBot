using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed class OnboardingContext(
    AppSettings settings,
    TwitchAccountSettings botAccount,
    TwitchAccountSettings broadcasterAccount,
    YouTubeSettings? youTubeSettings = null,
    string youTubeClientSecret = "",
    YouTubeAccountSettings? youTubeBotAccount = null,
    YouTubeAccountSettings? youTubeBroadcasterAccount = null)
{
    public event EventHandler? CredentialsChanged;

    public AppSettings Settings { get; } = settings;

    public TwitchAccountSettings BotAccount { get; set; } = botAccount;

    public TwitchAccountSettings BroadcasterAccount { get; set; } = broadcasterAccount;

    public YouTubeSettings YouTubeSettings { get; } = youTubeSettings ?? new();

    public string YouTubeClientSecret { get; set; } = youTubeClientSecret;

    public YouTubeAccountSettings YouTubeBotAccount { get; set; } = youTubeBotAccount ?? new();

    public YouTubeAccountSettings YouTubeBroadcasterAccount { get; set; } = youTubeBroadcasterAccount ?? new();

    public bool AutoDetectChannel { get; set; } = true;

    internal bool TwitchSettingsWritten { get; set; }

    internal bool TwitchSettingsApplied { get; set; }

    internal ChannelCatalogStore? TwitchCatalog { get; init; }

    internal ChannelCatalogEntry? TwitchChannel { get; init; }

    private readonly HashSet<PlatformAuthRole> _reauthorizedTwitchRoles = [];
    private readonly HashSet<PlatformAuthRole> _pendingTwitchRoles = [];
    private readonly Dictionary<PlatformAuthRole, TwitchAccountSettings?> _expectedTwitchAccounts = [];
    private readonly Dictionary<PlatformAuthRole, TwitchAccountChange> _appliedTwitchAccounts = [];
    private readonly Dictionary<PlatformAuthRole, TwitchAssignment> _appliedTwitchAssignments = [];

    private const string ChannelChanged = "Канал или назначенный аккаунт Twitch изменились. Закройте мастер и откройте его снова.";
    private const string AccountChanged = "Аккаунт Twitch изменился во время настройки. Закройте мастер и откройте его снова.";
    private const string AccountNotWritten = "Не удалось сохранить вход в Twitch. Если вы только что перенесли настройки, перезапустите приложение.";
    private const string AssignmentNotWritten = "Не удалось сохранить назначение аккаунта Twitch. Если вы только что перенесли настройки, перезапустите приложение.";
    internal const string TwitchSettingsNotWritten = "Не удалось сохранить настройки приложения. Если вы только что перенесли данные, перезапустите приложение.";

    internal bool TwitchAccountsApplied => _appliedTwitchAccounts.Count > 0 || _appliedTwitchAssignments.Count > 0;

    internal string TwitchContextId(PlatformAuthRole role) => AuthContextIds.Normalize(
        role == PlatformAuthRole.Bot ? TwitchChannel?.BotContextId : TwitchChannel?.BroadcasterContextId);

    internal bool HasConfiguredTwitchChannel =>
        TwitchChannel is not null || !string.IsNullOrWhiteSpace(Settings.Twitch.Channel);

    internal bool CanUseTwitchRole(PlatformAuthRole role, TwitchAccountSettings account)
    {
        if (!MatchesBroadcaster(role, account.UserId, account.Login))
        {
            return false;
        }

        if (TwitchChannel is null || (_pendingTwitchRoles.Contains(role) && _reauthorizedTwitchRoles.Contains(role)))
        {
            return true;
        }

        var assignment = ExpectedTwitchAssignment(role);
        return !string.IsNullOrWhiteSpace(account.UserId)
            && string.Equals(assignment.VerifiedSubject, account.UserId, StringComparison.Ordinal)
            && string.Equals(assignment.AccountId ?? assignment.VerifiedSubject, account.UserId, StringComparison.Ordinal);
    }

    internal void CaptureTwitchAccounts(TwitchAccountSettings? bot, TwitchAccountSettings? broadcaster)
    {
        _expectedTwitchAccounts[PlatformAuthRole.Bot] = bot;
        _expectedTwitchAccounts[PlatformAuthRole.Broadcaster] = broadcaster;
    }

    internal TwitchAccountSettings CurrentTwitchAccount(AccountsStore accounts, PlatformAuthRole role) =>
        _pendingTwitchRoles.Contains(role)
            ? DraftTwitchAccount(role)
            : accounts.Load(role, TwitchContextId(role));

    private TwitchAccountSettings DraftTwitchAccount(PlatformAuthRole role) =>
        role == PlatformAuthRole.Bot ? BotAccount : BroadcasterAccount;

    private bool MatchesBroadcaster(PlatformAuthRole role, string userId, string login)
    {
        if (role != PlatformAuthRole.Broadcaster)
        {
            return true;
        }

        if (TwitchChannel is { } channel)
        {
            return string.Equals(channel.ChannelId, userId, StringComparison.Ordinal);
        }

        var handle = Settings.Twitch.Channel?.Trim().TrimStart('@');
        return AutoDetectChannel || string.IsNullOrEmpty(handle) || string.Equals(handle, login, StringComparison.OrdinalIgnoreCase);
    }

    internal bool AcceptTwitchLogin(PlatformAuthRole role, TwitchAccountSettings account, OAuthFlowResult login)
    {
        var currentSubject = TwitchChannel?.GetAccountAssignment(role)?.Account.AccountId;
        var contextId = TwitchContextId(role);
        var sharedContext = TwitchCatalog?.GetConfiguredChannels(StreamingPlatform.Twitch)
            .Any(channel => channel.Key != TwitchChannel?.Key
                && AuthContextIds.Normalize(role == PlatformAuthRole.Bot ? channel.BotContextId : channel.BroadcasterContextId) == contextId) == true;
        if (string.IsNullOrWhiteSpace(currentSubject) && (!AuthContextIds.IsDefault(contextId) || sharedContext))
        {
            currentSubject = account.UserId;
        }

        if (string.IsNullOrWhiteSpace(login.UserId) || !MatchesBroadcaster(role, login.UserId, login.Login)
            || (!string.IsNullOrWhiteSpace(currentSubject) && !string.Equals(currentSubject, login.UserId, StringComparison.Ordinal)))
        {
            return false;
        }

        login.ApplyTo(account, DateTimeOffset.UtcNow);
        _reauthorizedTwitchRoles.Add(role);
        _pendingTwitchRoles.Add(role);
        return true;
    }

    internal void ClearTwitchLogin(PlatformAuthRole role)
    {
        if (_reauthorizedTwitchRoles.Remove(role)
            || _expectedTwitchAccounts.GetValueOrDefault(role) is not null)
        {
            _pendingTwitchRoles.Add(role);
        }
    }

    internal string? SaveTwitchSetup(AccountsStore accounts, Func<bool> saveSettings)
    {
        if (accounts.IsWriteRevoked)
        {
            return AccountNotWritten;
        }

        if (TwitchCatalog?.IsWriteRevoked == true)
        {
            return AssignmentNotWritten;
        }

        if (TwitchCatalog is not null && !MatchesTwitchChannel(TwitchCatalog.GetActiveChannel(StreamingPlatform.Twitch)))
        {
            return ChannelChanged;
        }

        var accountsWritten = new Dictionary<PlatformAuthRole, TwitchAccountChange>();
        var assignmentsWritten = new Dictionary<PlatformAuthRole, TwitchAssignment>();
        var assignmentsApplied = false;
        var succeeded = false;
        try
        {
            foreach (var role in _pendingTwitchRoles)
            {
                var contextId = TwitchContextId(role);
                var draft = DraftTwitchAccount(role);
                if (!_expectedTwitchAccounts.TryGetValue(role, out var previous))
                {
                    accounts.TryLoadContext(role, contextId, out previous);
                }

                var result = accounts.TryReplaceContext(role, contextId, current => SameAccount(current, previous), draft);
                if (!result.Applied)
                {
                    return AccountChanged;
                }

                accountsWritten.Add(role, new(previous, result.Snapshot!));
                if (!result.Written)
                {
                    return AccountNotWritten;
                }

                if (TwitchChannel is not null && _reauthorizedTwitchRoles.Contains(role))
                {
                    if (!accounts.VerifyPersistedAuthorization(role, contextId, draft.UserId))
                    {
                        return AccountNotWritten;
                    }

                    assignmentsWritten.Add(role, new(draft.UserId, contextId, draft.UserId));
                }
            }

            if (TwitchCatalog is not null && TwitchChannel is { } channel && assignmentsWritten.Count > 0)
            {
                var (applied, written) = TwitchCatalog.TryMutateChannel(channel.Key, requireActive: true, entry =>
                {
                    if (!MatchesTwitchChannel(entry))
                    {
                        return false;
                    }

                    foreach (var (role, binding) in assignmentsWritten)
                    {
                        binding.Apply(entry, role);
                    }

                    return true;
                });
                assignmentsApplied = applied;
                if (!applied || !written)
                {
                    return applied ? AssignmentNotWritten : ChannelChanged;
                }
            }

            if (!saveSettings())
            {
                return TwitchSettingsNotWritten;
            }

            foreach (var (role, change) in accountsWritten)
            {
                _expectedTwitchAccounts[role] = change.Written;
                _appliedTwitchAccounts[role] = _appliedTwitchAccounts.TryGetValue(role, out var earlier)
                    ? change with { Original = earlier.Original }
                    : change;
            }

            foreach (var (role, binding) in assignmentsWritten)
            {
                _appliedTwitchAssignments[role] = binding;
            }

            _pendingTwitchRoles.Clear();
            succeeded = true;
            return null;
        }
        finally
        {
            if (!succeeded)
            {
                try
                {
                    if (assignmentsApplied)
                    {
                        RestoreTwitchAssignments(assignmentsWritten, useOriginal: false);
                    }
                }
                finally
                {
                    RestoreTwitchAccounts(accounts, accountsWritten);
                }
            }
        }
    }

    internal bool SaveSettings(SettingsManager settingsManager)
    {
        var written = settingsManager.SaveSettings(Settings);
        TwitchSettingsApplied = true;
        TwitchSettingsWritten |= written;
        return written;
    }

    private bool MatchesTwitchChannel(ChannelCatalogEntry? entry) =>
        entry?.Key == TwitchChannel?.Key && (entry is null || PlatformAuthRoles.Active.All(role =>
            TwitchAssignment.From(entry, role) == ExpectedTwitchAssignment(role)));

    private TwitchAssignment ExpectedTwitchAssignment(PlatformAuthRole role) =>
        _appliedTwitchAssignments.TryGetValue(role, out var applied) ? applied : TwitchAssignment.From(TwitchChannel!, role);

    internal bool RestoreTwitchChanges(AccountsStore accounts)
    {
        var bindingsWritten = RestoreTwitchAssignments(_appliedTwitchAssignments, useOriginal: true);
        return RestoreTwitchAccounts(accounts, _appliedTwitchAccounts) && bindingsWritten;
    }

    private bool RestoreTwitchAssignments(Dictionary<PlatformAuthRole, TwitchAssignment> changes, bool useOriginal)
    {
        if (TwitchCatalog is null || TwitchChannel is not { } original || changes.Count == 0)
        {
            return true;
        }

        var (applied, written) = TwitchCatalog.TryMutateChannel(original.Key, requireActive: false, entry =>
        {
            var changed = false;
            foreach (var (role, binding) in changes)
            {
                if (TwitchAssignment.From(entry, role) == binding)
                {
                    var restore = useOriginal ? TwitchAssignment.From(original, role) : ExpectedTwitchAssignment(role);
                    if (restore != binding)
                    {
                        restore.Apply(entry, role);
                        changed = true;
                    }
                }
            }

            return changed;
        });
        return !applied || written;
    }

    private bool RestoreTwitchAccounts(AccountsStore accounts, Dictionary<PlatformAuthRole, TwitchAccountChange> changes)
    {
        var written = true;
        foreach (var (role, change) in changes)
        {
            var result = accounts.TryReplaceContext(role, TwitchContextId(role), current => SameAccount(current, change.Written), change.Original);
            written &= !result.Applied || result.Written;
        }

        return written;
    }

    private static bool SameAccount(TwitchAccountSettings? left, TwitchAccountSettings? right) =>
        left is null || right is null ? left is null && right is null
            : string.Equals(left.AccessToken, right.AccessToken, StringComparison.Ordinal)
            && string.Equals(left.RefreshToken, right.RefreshToken, StringComparison.Ordinal)
            && string.Equals(left.Login, right.Login, StringComparison.Ordinal)
            && string.Equals(left.UserId, right.UserId, StringComparison.Ordinal)
            && left.AccessTokenExpiresAt == right.AccessTokenExpiresAt
            && left.Scopes.SequenceEqual(right.Scopes, StringComparer.Ordinal)
            && left.StoredScopes.SequenceEqual(right.StoredScopes, StringComparer.Ordinal)
            && string.Equals(left.BrowserProfileName, right.BrowserProfileName, StringComparison.Ordinal);

    private sealed record TwitchAccountChange(TwitchAccountSettings? Original, TwitchAccountSettings Written);

    private readonly record struct TwitchAssignment(string? AccountId, string? ContextId, string? VerifiedSubject)
    {
        internal static TwitchAssignment From(ChannelCatalogEntry entry, PlatformAuthRole role) =>
            role == PlatformAuthRole.Bot
                ? new(entry.BotAccountId, AuthContextIds.Normalize(entry.BotContextId), entry.BotVerifiedSubjectId)
                : new(entry.BroadcasterAccountId, AuthContextIds.Normalize(entry.BroadcasterContextId), entry.BroadcasterVerifiedSubjectId);

        internal void Apply(ChannelCatalogEntry entry, PlatformAuthRole role)
        {
            if (role == PlatformAuthRole.Bot)
            {
                (entry.BotAccountId, entry.BotContextId, entry.BotVerifiedSubjectId) = (AccountId, ContextId, VerifiedSubject);
            }
            else
            {
                (entry.BroadcasterAccountId, entry.BroadcasterContextId, entry.BroadcasterVerifiedSubjectId) = (AccountId, ContextId, VerifiedSubject);
            }
        }
    }

    public void RaiseCredentialsChanged()
    {
        CredentialsChanged?.Invoke(this, EventArgs.Empty);
    }
}
