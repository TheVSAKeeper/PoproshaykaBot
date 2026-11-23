using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class AccountsStore : RoleAccountsStoreBase<AccountsStore.AccountsFileDto, TwitchAccountSettings>
{
    protected override StreamingPlatform Platform => StreamingPlatform.Twitch;

    public AccountsStore(
        ILogger<AccountsStore>? logger = null,
        string? filePath = null,
        SettingsWriteGate? gate = null)
        : base(
            filePath ?? AppPaths.SettingsFile("accounts.json"),
            logger,
            AccountsTokenRedactor.Redact,
            DescribeAccounts,
            gate)
    {
        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            var state = LoadFile();

            logger.LogDebug("AccountsStore инициализирован из {FilePath} (bot.login={BotLogin}, broadcaster.login={BroadcasterLogin}, contexts={ContextCount})",
                FilePath,
                string.IsNullOrEmpty(state.BotAccount?.Login) ? "–" : state.BotAccount.Login,
                string.IsNullOrEmpty(state.BroadcasterAccount?.Login) ? "–" : state.BroadcasterAccount.Login,
                state.Contexts?.Count ?? 0);
        }
    }

    protected override IReadOnlyList<string> BotDefaultScopes => TwitchScopes.BotRequired;

    protected override IReadOnlyList<string> BroadcasterDefaultScopes => TwitchScopes.BroadcasterRequired;

    private static readonly PlatformAccountOperation[] BotPermissions =
    [
        new("Чтение чата", true, TwitchScopes.UserReadChat),
        new("Отправка в чат", true, TwitchScopes.UserWriteChat),
        new("Шёпот", false, TwitchScopes.UserManageWhispers),
    ];

    private static readonly PlatformAccountOperation[] BroadcasterPermissions =
    [
        new("Доступ бота к чату", true, TwitchScopes.ChannelBot),
        new("Чтение опросов", true, TwitchScopes.ChannelReadPolls),
        new("Управление опросами", true, TwitchScopes.ChannelManagePolls),
        new("Изменение данных эфира", true, TwitchScopes.ChannelManageBroadcast),
    ];

    public override IReadOnlyList<PlatformAccountOperation> GetPermissions(PlatformAuthRole role) =>
        role == PlatformAuthRole.Bot ? BotPermissions : BroadcasterPermissions;

    public bool SaveAll(TwitchAccountSettings bot, TwitchAccountSettings broadcaster)
    {
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(broadcaster);

        var written = MutateFile(state =>
        {
            state.BotAccount = bot;
            state.BroadcasterAccount = broadcaster;
        });

        if (written)
        {
            Logger?.LogInformation("AccountsStore: оба аккаунта заменены целиком и сохранены в {FilePath}", FilePath);
            return true;
        }

        Logger?.LogInformation("AccountsStore: оба аккаунта заменены в памяти до перезапуска, файл {FilePath} переписан снаружи", FilePath);
        return false;
    }

    public (bool Changed, bool Written) RemoveRoleContext(PlatformAuthRole role, string? contextId) =>
        RemoveRoleContextCore(role, contextId, OAuthRoleHelpers.ClearAccountInPlace);

    public override (IReadOnlyList<string> RemovedContextIds, bool Written) RemoveAccountRole(string accountId, PlatformAuthRole role) =>
        RemoveAccountRoleCore(accountId, role, OAuthRoleHelpers.ClearAccountInPlace);

    public override (IReadOnlyList<(PlatformAuthRole Role, string ContextId)> Removed, bool Written) RemoveAccount(string accountId) =>
        RemoveAccountCore(accountId, OAuthRoleHelpers.ClearAccountInPlace);

    protected override TwitchAccountSettings? GetAccount(AccountsFileDto state, PlatformAuthRole role)
    {
        return role == PlatformAuthRole.Bot ? state.BotAccount : state.BroadcasterAccount;
    }

    protected override void SetAccount(AccountsFileDto state, PlatformAuthRole role, TwitchAccountSettings account)
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

    protected override List<NamedRoleAccount<TwitchAccountSettings>> GetContexts(AccountsFileDto state)
    {
        state.Contexts ??= [];
        return state.Contexts;
    }

    protected override void ClearContextsIfEmpty(AccountsFileDto state)
    {
        if (state.Contexts is { Count: 0 })
        {
            state.Contexts = null;
        }
    }

    protected override string? GetSubjectId(TwitchAccountSettings account) => account.UserId;

    protected override string? GetDisplayLabel(TwitchAccountSettings account) => account.Login;

    protected override PlatformChatBrowserProfile? GetChatBrowserProfile(TwitchAccountSettings account) =>
        Guid.TryParseExact(account.BrowserProfileName, "N", out var profileId)
            ? new(WebView2UserDataFolders.ResolveTwitchAccounts(), profileId.ToString("N"))
            : null;

    protected override void CopyAccountFields(TwitchAccountSettings source, TwitchAccountSettings target)
    {
        base.CopyAccountFields(source, target);
        target.Login = source.Login;
        target.BrowserProfileName = source.BrowserProfileName
            ?? (string.Equals(source.UserId, target.UserId, StringComparison.Ordinal) ? target.BrowserProfileName : null);
        target.UserId = source.UserId;
    }

    protected override void OnContextsUpserted(
        AccountsFileDto state,
        IReadOnlyList<(PlatformAuthRole Role, string ContextId, string Label, TwitchAccountSettings Account)> entries)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Account.UserId)
                || string.IsNullOrWhiteSpace(entry.Account.BrowserProfileName))
            {
                continue;
            }

            SyncBrowserProfileForUser(state, entry.Account.UserId, entry.Account.BrowserProfileName);
        }
    }

    private void SyncBrowserProfileForUser(AccountsFileDto state, string userId, string profileName)
    {
        foreach (var role in PlatformAuthRoles.Active)
        {
            if (GetAccount(state, role) is { } defaultAccount
                && string.Equals(defaultAccount.UserId, userId, StringComparison.Ordinal))
            {
                defaultAccount.BrowserProfileName = profileName;
            }
        }

        foreach (var context in GetContexts(state))
        {
            if (context.Account is not null
                && string.Equals(context.Account.UserId, userId, StringComparison.Ordinal))
            {
                context.Account.BrowserProfileName = profileName;
            }
        }
    }

    protected override void OnRoleMutated(PlatformAuthRole role, bool written)
    {
        Logger?.LogDebug("AccountsStore: применена мутация для роли {Role}, {Outcome}",
            role,
            written ? $"состояние сохранено в {FilePath}" : "состояние принято только в памяти до перезапуска");
    }

    protected override void OnAccessTokenCleared(PlatformAuthRole role, bool cleared)
    {
        if (cleared)
        {
            Logger?.LogInformation("AccountsStore: access-токен роли {Role} очищён по запросу 401-обработчика", role);
        }
        else
        {
            Logger?.LogDebug("AccountsStore.TryClearAccessToken: токен роли {Role} уже изменился – очистка пропущена", role);
        }
    }

    public string DescribeSnapshot()
    {
        return DescribeAccounts(LoadFile());
    }

    public string DescribeContextsSnapshot()
    {
        var contexts = LoadFile().Contexts;

        if (contexts is null || contexts.Count == 0)
        {
            return "нет";
        }

        return string.Join("; ", contexts.Select(static entry =>
            $"{SettingsDescriber.Value(entry.ContextId)}/{entry.Role}: {Describe(entry.Account ?? new())}"));
    }

    public static string Describe(TwitchAccountSettings account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return $"логин {SettingsDescriber.Value(account.Login)}, userId {SettingsDescriber.Value(account.UserId)}, "
            + $"access-токен {SettingsDescriber.Present(account.AccessToken)}, refresh-токен {SettingsDescriber.Present(account.RefreshToken)}, "
            + $"действует до {SettingsDescriber.Moment(account.AccessTokenExpiresAt)}, скоупов {account.Scopes.Length} (сохранено {account.StoredScopes.Length})";
    }

    private static string DescribeAccounts(AccountsFileDto state)
    {
        return $"бот – {Describe(state.BotAccount ?? new())}; "
            + $"вещатель – {Describe(state.BroadcasterAccount ?? new())}; доп. контекстов {state.Contexts?.Count ?? 0}";
    }

    public sealed class AccountsFileDto
    {
        public TwitchAccountSettings? BotAccount { get; set; }

        public TwitchAccountSettings? BroadcasterAccount { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<NamedRoleAccount<TwitchAccountSettings>>? Contexts { get; set; }
    }
}
