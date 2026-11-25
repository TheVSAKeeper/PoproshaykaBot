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

    internal bool TwitchAccountsApplied { get; private set; }

    private readonly HashSet<PlatformAuthRole> _reauthorizedTwitchRoles = [];
    private readonly HashSet<PlatformAuthRole> _changedTwitchAssignments = [];

    internal string TwitchContextId(PlatformAuthRole role) => AuthContextIds.Normalize(
        role == PlatformAuthRole.Bot ? TwitchChannel?.BotContextId : TwitchChannel?.BroadcasterContextId);

    internal bool HasConfiguredTwitchChannel =>
        TwitchChannel is not null || !string.IsNullOrWhiteSpace(Settings.Twitch.Channel);

    internal bool CanUseTwitchRole(PlatformAuthRole role, TwitchAccountSettings account) =>
        TwitchChannel is null || _reauthorizedTwitchRoles.Contains(role)
        || (TwitchChannel.GetAccountAssignment(role) is { IsVerified: true } assignment
            && string.Equals(assignment.Account.AccountId, account.UserId, StringComparison.Ordinal));

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

        if (string.IsNullOrWhiteSpace(login.UserId)
            || (!string.IsNullOrWhiteSpace(currentSubject) && !string.Equals(currentSubject, login.UserId, StringComparison.Ordinal)))
        {
            return false;
        }

        login.ApplyTo(account, DateTimeOffset.UtcNow);
        _reauthorizedTwitchRoles.Add(role);
        return true;
    }

    internal void ClearTwitchLogin(PlatformAuthRole role) => _reauthorizedTwitchRoles.Remove(role);

    internal bool SaveTwitchAccounts(AccountsStore accounts)
    {
        if (TwitchCatalog is not null && TwitchChannel is { } channel
            && TwitchCatalog.GetActiveChannel(StreamingPlatform.Twitch)?.Key != channel.Key)
        {
            return false;
        }

        var botContextId = TwitchContextId(PlatformAuthRole.Bot);
        var broadcasterContextId = TwitchContextId(PlatformAuthRole.Broadcaster);
        if ((!AuthContextIds.IsDefault(botContextId) && accounts.ListContexts(PlatformAuthRole.Broadcaster).Any(c => c.ContextId == botContextId))
            || (!AuthContextIds.IsDefault(broadcasterContextId)
                && (broadcasterContextId == botContextId || accounts.ListContexts(PlatformAuthRole.Bot).Any(c => c.ContextId == broadcasterContextId))))
        {
            return false;
        }

        var changes = new List<(PlatformAuthRole Role, string ContextId, string Label, TwitchAccountSettings Account)>();
        AddAccount(PlatformAuthRole.Bot, botContextId, BotAccount);
        AddAccount(PlatformAuthRole.Broadcaster, broadcasterContextId, BroadcasterAccount);
        TwitchAccountsApplied |= changes.Count > 0;
        var written = accounts.UpsertContexts(changes);
        return written && SaveTwitchAssignments(accounts);

        void AddAccount(PlatformAuthRole role, string contextId, TwitchAccountSettings account)
        {
            if (_reauthorizedTwitchRoles.Contains(role) || accounts.TryLoadContext(role, contextId, out _))
            {
                changes.Add((role, contextId, GetContextLabel(accounts, role, contextId), account));
            }
        }
    }

    internal static string GetContextLabel(AccountsStore accounts, PlatformAuthRole role, string contextId) =>
        accounts.ListContexts(role).FirstOrDefault(context => context.ContextId == contextId)?.Label ?? contextId;

    private bool SaveTwitchAssignments(AccountsStore accounts)
    {
        if (TwitchCatalog is null || TwitchChannel is not { } original || _reauthorizedTwitchRoles.Count == 0)
        {
            return true;
        }

        foreach (var role in _reauthorizedTwitchRoles)
        {
            var account = role == PlatformAuthRole.Bot ? BotAccount : BroadcasterAccount;
            if (!accounts.VerifyPersistedAuthorization(role, TwitchContextId(role), account.UserId))
            {
                return false;
            }
        }

        var found = false;
        var written = TwitchCatalog.Mutate(settings =>
        {
            var entry = settings.Channels.FirstOrDefault(channel => channel.Key == original.Key);
            if (entry is null)
            {
                return;
            }

            found = true;
            if (_reauthorizedTwitchRoles.Contains(PlatformAuthRole.Bot))
            {
                entry.BotAccountId = BotAccount.UserId;
                entry.BotContextId = TwitchContextId(PlatformAuthRole.Bot);
                entry.BotVerifiedSubjectId = BotAccount.UserId;
                _changedTwitchAssignments.Add(PlatformAuthRole.Bot);
            }

            if (_reauthorizedTwitchRoles.Contains(PlatformAuthRole.Broadcaster))
            {
                entry.BroadcasterAccountId = BroadcasterAccount.UserId;
                entry.BroadcasterContextId = TwitchContextId(PlatformAuthRole.Broadcaster);
                entry.BroadcasterVerifiedSubjectId = BroadcasterAccount.UserId;
                _changedTwitchAssignments.Add(PlatformAuthRole.Broadcaster);
            }
        });
        return written && found && TwitchCatalog.VerifyPersisted();
    }

    internal void RestoreTwitchAssignments()
    {
        if (TwitchCatalog is null || TwitchChannel is not { } original || _changedTwitchAssignments.Count == 0)
        {
            return;
        }

        TwitchCatalog.Mutate(settings =>
        {
            var entry = settings.Channels.FirstOrDefault(channel => channel.Key == original.Key);
            if (entry is null)
            {
                return;
            }

            if (_changedTwitchAssignments.Contains(PlatformAuthRole.Bot))
            {
                entry.BotAccountId = original.BotAccountId;
                entry.BotContextId = original.BotContextId;
                entry.BotVerifiedSubjectId = original.BotVerifiedSubjectId;
            }

            if (_changedTwitchAssignments.Contains(PlatformAuthRole.Broadcaster))
            {
                entry.BroadcasterAccountId = original.BroadcasterAccountId;
                entry.BroadcasterContextId = original.BroadcasterContextId;
                entry.BroadcasterVerifiedSubjectId = original.BroadcasterVerifiedSubjectId;
            }
        });
    }

    public void RaiseCredentialsChanged()
    {
        CredentialsChanged?.Invoke(this, EventArgs.Empty);
    }
}
