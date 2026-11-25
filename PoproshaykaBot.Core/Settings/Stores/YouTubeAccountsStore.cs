using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class YouTubeAccountsStore : RoleAccountsStoreBase<YouTubeAccountsStore.YouTubeAccountsFileDto, YouTubeAccountSettings>
{
    protected override StreamingPlatform Platform => StreamingPlatform.YouTube;

    private static readonly PlatformAccountOperation[] BotPermissions =
    [
        new("Чтение чата", true, YouTubeScopes.ForceSsl),
        new("Отправка в чат", true, YouTubeScopes.ForceSsl),
    ];

    private static readonly PlatformAccountOperation[] BroadcasterPermissions =
    [
        new("Чтение данных эфира", true, YouTubeScopes.ReadOnly),
        new("Обновление названия и описания эфира", true, YouTubeScopes.ForceSsl),
    ];

    public override IReadOnlyList<PlatformAccountOperation> GetPermissions(PlatformAuthRole role) =>
        role == PlatformAuthRole.Bot ? BotPermissions : BroadcasterPermissions;

    public YouTubeAccountsStore(
        ILogger<YouTubeAccountsStore>? logger = null,
        string? filePath = null,
        SettingsWriteGate? gate = null)
        : base(
            filePath ?? AppPaths.SettingsFile(LegacyDataCatalog.YouTubeAccountsFileName),
            logger,
            AccountsTokenRedactor.Redact,
            DescribeAccounts,
            gate)
    {
        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            var state = LoadFile();

            logger.LogDebug(
                "YouTubeAccountsStore инициализирован из {FilePath} (bot.channel={BotChannel}, broadcaster.channel={BroadcasterChannel}, contexts={ContextCount})",
                FilePath,
                string.IsNullOrEmpty(state.BotAccount?.ChannelId) ? "–" : state.BotAccount.ChannelId,
                string.IsNullOrEmpty(state.BroadcasterAccount?.ChannelId) ? "–" : state.BroadcasterAccount.ChannelId,
                state.Contexts?.Count ?? 0);
        }
    }

    protected override IReadOnlyList<string> BotDefaultScopes => YouTubeScopes.BotRequired;

    protected override IReadOnlyList<string> BroadcasterDefaultScopes => YouTubeScopes.BroadcasterRequired;

    public string LoadClientSecret()
    {
        return LoadFile().ClientSecret ?? string.Empty;
    }

    public bool SaveClientSecret(string clientSecret)
    {
        ArgumentNullException.ThrowIfNull(clientSecret);

        return MutateFile(state => { state.ClientSecret = clientSecret; });
    }

    public (bool Matched, bool Written) TryReplaceDefaultAccounts(
        (string Secret, YouTubeAccountSettings Bot, YouTubeAccountSettings Broadcaster) expected,
        (string Secret, YouTubeAccountSettings Bot, YouTubeAccountSettings Broadcaster) replacement)
    {
        var expectedBot = JsonStoreClone.DeepClone(expected.Bot);
        var expectedBroadcaster = JsonStoreClone.DeepClone(expected.Broadcaster);
        EnsureDefaultScopes(expectedBot, PlatformAuthRole.Bot);
        EnsureDefaultScopes(expectedBroadcaster, PlatformAuthRole.Broadcaster);
        return TryMutateFile(state =>
        {
            if (!string.Equals(state.ClientSecret ?? string.Empty, expected.Secret, StringComparison.Ordinal)
                || !JsonStoreClone.SameValues(TakeAccount(state, PlatformAuthRole.Bot), expectedBot)
                || !JsonStoreClone.SameValues(TakeAccount(state, PlatformAuthRole.Broadcaster), expectedBroadcaster))
            {
                return false;
            }

            state.ClientSecret = replacement.Secret;
            state.BotAccount = JsonStoreClone.DeepClone(replacement.Bot);
            state.BroadcasterAccount = JsonStoreClone.DeepClone(replacement.Broadcaster);
            return true;
        });
    }

    public bool SaveAll(string clientSecret, YouTubeAccountSettings bot, YouTubeAccountSettings broadcaster)
    {
        ArgumentNullException.ThrowIfNull(clientSecret);
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(broadcaster);

        var written = MutateFile(state =>
        {
            state.ClientSecret = clientSecret;
            state.BotAccount = bot;
            state.BroadcasterAccount = broadcaster;
        });

        if (written)
        {
            Logger?.LogInformation("YouTubeAccountsStore: реквизиты и оба YouTube-аккаунта сохранены в {FilePath}", FilePath);
        }
        else
        {
            Logger?.LogInformation(
                "YouTubeAccountsStore: реквизиты и оба YouTube-аккаунта приняты в памяти до перезапуска, файл {FilePath} переписан снаружи",
                FilePath);
        }

        return written;
    }

    public (bool Changed, bool Written) RemoveRoleContext(PlatformAuthRole role, string? contextId) =>
        RemoveRoleContextCore(role, contextId, YouTubeOAuthRoleHelpers.ClearAccountInPlace);

    public override (IReadOnlyList<string> RemovedContextIds, bool Written) RemoveAccountRole(string accountId, PlatformAuthRole role) =>
        RemoveAccountRoleCore(accountId, role, YouTubeOAuthRoleHelpers.ClearAccountInPlace);

    public override (IReadOnlyList<(PlatformAuthRole Role, string ContextId)> Removed, bool Written) RemoveAccount(string accountId) =>
        RemoveAccountCore(accountId, YouTubeOAuthRoleHelpers.ClearAccountInPlace);

    public override bool SupportsChatBrowserProfile => true;

    public bool SetBrowserProfile(string accountId, string? profileName) =>
        SetChatBrowserProfile(accountId, profileName);

    protected override YouTubeAccountSettings? GetAccount(YouTubeAccountsFileDto state, PlatformAuthRole role)
    {
        return role == PlatformAuthRole.Bot ? state.BotAccount : state.BroadcasterAccount;
    }

    protected override void SetAccount(YouTubeAccountsFileDto state, PlatformAuthRole role, YouTubeAccountSettings account)
    {
        if (role == PlatformAuthRole.Bot)
        {
            state.BotAccount = account;
        }
        else
        {
            state.BroadcasterAccount = account;
        }
    }

    protected override List<NamedRoleAccount<YouTubeAccountSettings>> GetContexts(YouTubeAccountsFileDto state)
    {
        state.Contexts ??= [];
        return state.Contexts;
    }

    protected override void ClearContextsIfEmpty(YouTubeAccountsFileDto state)
    {
        if (state.Contexts is { Count: 0 })
        {
            state.Contexts = null;
        }
    }

    protected override string? GetSubjectId(YouTubeAccountSettings account) => account.ChannelId;

    protected override string? GetDisplayLabel(YouTubeAccountSettings account) => account.ChannelTitle;

    protected override PlatformChatBrowserProfile? GetChatBrowserProfile(YouTubeAccountSettings account) =>
        Guid.TryParseExact(account.BrowserProfileName, "N", out var profileId)
            ? new(WebView2UserDataFolders.ResolveYouTubeAccounts(), profileId.ToString("N"))
            : null;

    protected override void SetChatBrowserProfileName(YouTubeAccountSettings account, string? profileName) =>
        account.BrowserProfileName = profileName;

    protected override void CopyAccountFields(YouTubeAccountSettings source, YouTubeAccountSettings target)
    {
        var previousChannelId = target.ChannelId;
        base.CopyAccountFields(source, target);
        target.ChannelId = source.ChannelId;
        target.ChannelTitle = source.ChannelTitle;
        target.BrowserProfileName = source.BrowserProfileName
            ?? (string.Equals(source.ChannelId, previousChannelId, StringComparison.Ordinal) ? target.BrowserProfileName : null);
    }

    protected override void OnContextsUpserted(
        YouTubeAccountsFileDto state,
        IReadOnlyList<(PlatformAuthRole Role, string ContextId, string Label, YouTubeAccountSettings Account)> entries)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Account.ChannelId)
                || string.IsNullOrWhiteSpace(entry.Account.BrowserProfileName))
            {
                continue;
            }

            SyncBrowserProfileForChannel(state, entry.Account.ChannelId, entry.Account.BrowserProfileName);
        }
    }

    private void SyncBrowserProfileForChannel(YouTubeAccountsFileDto state, string channelId, string profileName)
    {
        foreach (var role in PlatformAuthRoles.Active)
        {
            if (GetAccount(state, role) is { } defaultAccount
                && string.Equals(defaultAccount.ChannelId, channelId, StringComparison.Ordinal))
            {
                defaultAccount.BrowserProfileName = profileName;
            }
        }

        foreach (var context in GetContexts(state))
        {
            if (context.Account is not null
                && string.Equals(context.Account.ChannelId, channelId, StringComparison.Ordinal))
            {
                context.Account.BrowserProfileName = profileName;
            }
        }
    }

    protected override void OnRoleMutated(PlatformAuthRole role, bool written)
    {
        Logger?.LogDebug(
            "YouTubeAccountsStore: применена мутация для роли {Role}, {Outcome}",
            role,
            written ? $"состояние сохранено в {FilePath}" : "состояние принято только в памяти до перезапуска");
    }

    protected override void OnAccessTokenCleared(PlatformAuthRole role, bool cleared)
    {
        if (cleared)
        {
            Logger?.LogInformation("YouTubeAccountsStore: access-токен роли {Role} очищён", role);
        }
    }

    public static string Describe(YouTubeAccountSettings account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return $"канал {SettingsDescriber.Value(account.ChannelTitle)}, channelId {SettingsDescriber.Value(account.ChannelId)}, "
            + $"access-токен {SettingsDescriber.Present(account.AccessToken)}, refresh-токен {SettingsDescriber.Present(account.RefreshToken)}, "
            + $"действует до {SettingsDescriber.Moment(account.AccessTokenExpiresAt)}, скоупов {account.Scopes.Length} (сохранено {account.StoredScopes.Length})";
    }

    public static string Describe(string? clientSecret, YouTubeAccountSettings bot, YouTubeAccountSettings broadcaster)
    {
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(broadcaster);

        return $"clientSecret {SettingsDescriber.Present(clientSecret)}; бот – {Describe(bot)}; вещатель – {Describe(broadcaster)}";
    }

    public string DescribeSnapshot()
    {
        return DescribeAccounts(LoadFile());
    }

    public string DescribeContextsSnapshot() => DescribeContextsSnapshot(Describe);

    private static string DescribeAccounts(YouTubeAccountsFileDto state)
    {
        return $"{Describe(state.ClientSecret, state.BotAccount ?? new(), state.BroadcasterAccount ?? new())}; доп. контекстов {state.Contexts?.Count ?? 0}";
    }

    public sealed class YouTubeAccountsFileDto
    {
        public string ClientSecret { get; set; } = string.Empty;

        public YouTubeAccountSettings? BotAccount { get; set; }

        public YouTubeAccountSettings? BroadcasterAccount { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<NamedRoleAccount<YouTubeAccountSettings>>? Contexts { get; set; }
    }
}
