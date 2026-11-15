using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class YouTubeAccountsStore
{
    private readonly ILogger<YouTubeAccountsStore>? _logger;
    private readonly string _filePath;
    private readonly JsonStore<YouTubeAccountsFileDto> _store;

    public YouTubeAccountsStore(
        ILogger<YouTubeAccountsStore>? logger = null,
        string? filePath = null,
        SettingsWriteGate? gate = null)
    {
        _logger = logger;
        _filePath = filePath ?? AppPaths.SettingsFile(LegacyDataCatalog.YouTubeAccountsFileName);
        _store = new(_filePath, logger, AccountsTokenRedactor.Redact, describe: DescribeAccounts, gate: gate);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            var state = _store.Load();

            logger.LogDebug(
                "YouTubeAccountsStore инициализирован из {FilePath} (bot.channel={BotChannel}, broadcaster.channel={BroadcasterChannel})",
                _filePath,
                string.IsNullOrEmpty(state.BotAccount?.ChannelId) ? "–" : state.BotAccount.ChannelId,
                string.IsNullOrEmpty(state.BroadcasterAccount?.ChannelId) ? "–" : state.BroadcasterAccount.ChannelId);
        }
    }

    public string LoadClientSecret()
    {
        return _store.Load().ClientSecret ?? string.Empty;
    }

    public bool SaveClientSecret(string clientSecret)
    {
        ArgumentNullException.ThrowIfNull(clientSecret);

        return _store.Mutate(state => { state.ClientSecret = clientSecret; });
    }

    public YouTubeAccountSettings LoadBot()
    {
        return Load(YouTubeOAuthRole.Bot);
    }

    public YouTubeAccountSettings LoadBroadcaster()
    {
        return Load(YouTubeOAuthRole.Broadcaster);
    }

    public YouTubeAccountSettings Load(YouTubeOAuthRole role)
    {
        return TakeAccount(_store.Load(), role);
    }

    public bool Mutate(YouTubeOAuthRole role, Action<YouTubeAccountSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        var written = _store.Mutate(state => mutator(TakeAccount(state, role)));

        _logger?.LogDebug(
            "YouTubeAccountsStore: применена мутация для роли {Role}, {Outcome}",
            role,
            written ? $"состояние сохранено в {_filePath}" : "состояние принято только в памяти до перезапуска");

        return written;
    }

    public bool TryClearAccessToken(YouTubeOAuthRole role, string expectedToken)
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

        if (cleared)
        {
            _logger?.LogInformation("YouTubeAccountsStore: access-токен роли {Role} очищён", role);
        }

        return cleared;
    }

    public bool SaveAll(string clientSecret, YouTubeAccountSettings bot, YouTubeAccountSettings broadcaster)
    {
        ArgumentNullException.ThrowIfNull(clientSecret);
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(broadcaster);

        var written = _store.Save(new()
        {
            ClientSecret = clientSecret,
            BotAccount = bot,
            BroadcasterAccount = broadcaster,
        });

        if (written)
        {
            _logger?.LogInformation("YouTubeAccountsStore: реквизиты и оба YouTube-аккаунта сохранены в {FilePath}", _filePath);
        }
        else
        {
            _logger?.LogInformation(
                "YouTubeAccountsStore: реквизиты и оба YouTube-аккаунта приняты в памяти до перезапуска, файл {FilePath} переписан снаружи",
                _filePath);
        }

        return written;
    }

    private static string DescribeAccounts(YouTubeAccountsFileDto state)
    {
        return SettingsDescriber.DescribeYouTubeAccounts(
            state.ClientSecret,
            state.BotAccount ?? new(),
            state.BroadcasterAccount ?? new());
    }

    private static YouTubeAccountSettings TakeAccount(YouTubeAccountsFileDto state, YouTubeOAuthRole role)
    {
        if (role is not (YouTubeOAuthRole.Bot or YouTubeOAuthRole.Broadcaster))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, null);
        }

        var bot = state.BotAccount ??= new();
        var broadcaster = state.BroadcasterAccount ??= new();

        ApplyScopeDefaults(bot, YouTubeScopes.BotRequired);
        ApplyScopeDefaults(broadcaster, YouTubeScopes.BroadcasterRequired);

        return role == YouTubeOAuthRole.Bot ? bot : broadcaster;
    }

    private static void ApplyScopeDefaults(YouTubeAccountSettings account, IReadOnlyList<string> defaultScopes)
    {
        if (account.Scopes.Length == 0)
        {
            account.Scopes = [..defaultScopes];
        }
    }

    private sealed class YouTubeAccountsFileDto
    {
        public string ClientSecret { get; set; } = string.Empty;
        public YouTubeAccountSettings? BotAccount { get; set; }
        public YouTubeAccountSettings? BroadcasterAccount { get; set; }
    }
}
