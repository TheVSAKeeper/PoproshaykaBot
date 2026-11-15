using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class AccountsStore : RoleAccountsStoreBase<AccountsStore.AccountsFileDto, TwitchAccountSettings>
{
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

            logger.LogDebug("AccountsStore инициализирован из {FilePath} (bot.login={BotLogin}, broadcaster.login={BroadcasterLogin})",
                FilePath,
                string.IsNullOrEmpty(state.BotAccount?.Login) ? "–" : state.BotAccount.Login,
                string.IsNullOrEmpty(state.BroadcasterAccount?.Login) ? "–" : state.BroadcasterAccount.Login);
        }
    }

    protected override IReadOnlyList<string> BotDefaultScopes => TwitchScopes.BotRequired;

    protected override IReadOnlyList<string> BroadcasterDefaultScopes => TwitchScopes.BroadcasterRequired;

    public bool SaveAll(TwitchAccountSettings bot, TwitchAccountSettings broadcaster)
    {
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(broadcaster);

        var written = SaveFile(new()
        {
            BotAccount = bot,
            BroadcasterAccount = broadcaster,
        });

        if (written)
        {
            Logger?.LogInformation("AccountsStore: оба аккаунта заменены целиком и сохранены в {FilePath}", FilePath);
            return true;
        }

        Logger?.LogInformation("AccountsStore: оба аккаунта заменены в памяти до перезапуска, файл {FilePath} переписан снаружи", FilePath);
        return false;
    }

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
            + $"вещатель – {Describe(state.BroadcasterAccount ?? new())}";
    }

    public sealed class AccountsFileDto
    {
        public TwitchAccountSettings? BotAccount { get; set; }

        public TwitchAccountSettings? BroadcasterAccount { get; set; }
    }
}
