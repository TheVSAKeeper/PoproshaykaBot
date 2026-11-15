using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class YouTubeAccountsStore : RoleAccountsStoreBase<YouTubeAccountsStore.YouTubeAccountsFileDto, YouTubeAccountSettings>
{
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
                "YouTubeAccountsStore инициализирован из {FilePath} (bot.channel={BotChannel}, broadcaster.channel={BroadcasterChannel})",
                FilePath,
                string.IsNullOrEmpty(state.BotAccount?.ChannelId) ? "–" : state.BotAccount.ChannelId,
                string.IsNullOrEmpty(state.BroadcasterAccount?.ChannelId) ? "–" : state.BroadcasterAccount.ChannelId);
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

    public bool SaveAll(string clientSecret, YouTubeAccountSettings bot, YouTubeAccountSettings broadcaster)
    {
        ArgumentNullException.ThrowIfNull(clientSecret);
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(broadcaster);

        var written = SaveFile(new()
        {
            ClientSecret = clientSecret,
            BotAccount = bot,
            BroadcasterAccount = broadcaster,
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

    private static string DescribeAccounts(YouTubeAccountsFileDto state)
    {
        return Describe(
            state.ClientSecret,
            state.BotAccount ?? new(),
            state.BroadcasterAccount ?? new());
    }

    public sealed class YouTubeAccountsFileDto
    {
        public string ClientSecret { get; set; } = string.Empty;

        public YouTubeAccountSettings? BotAccount { get; set; }

        public YouTubeAccountSettings? BroadcasterAccount { get; set; }
    }
}
