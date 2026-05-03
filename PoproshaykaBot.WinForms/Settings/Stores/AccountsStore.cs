using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Auth;
using PoproshaykaBot.WinForms.Infrastructure;
using PoproshaykaBot.WinForms.Infrastructure.Persistence;
using PoproshaykaBot.WinForms.Twitch.Chat;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Settings.Stores;

public sealed class AccountsStore
{
    private readonly ILogger<AccountsStore>? _logger;
    private readonly string _filePath;
    private readonly object _syncLock = new();

    private TwitchAccountSettings _bot;
    private TwitchAccountSettings _broadcaster;

    public AccountsStore(ILogger<AccountsStore>? logger = null, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? AppPaths.SettingsFile("accounts.json");

        var dto = ReadFile();
        _bot = dto.BotAccount ?? new();
        _broadcaster = dto.BroadcasterAccount ?? new();

        ApplyScopeDefaults(_bot, TwitchScopes.BotRequired);
        ApplyScopeDefaults(_broadcaster, TwitchScopes.BroadcasterRequired);

        _logger?.LogDebug("AccountsStore инициализирован из {FilePath} (bot.login={BotLogin}, broadcaster.login={BroadcasterLogin})",
            _filePath,
            string.IsNullOrEmpty(_bot.Login) ? "—" : _bot.Login,
            string.IsNullOrEmpty(_broadcaster.Login) ? "—" : _broadcaster.Login);
    }

    public TwitchAccountSettings LoadBot()
    {
        return Load(TwitchOAuthRole.Bot);
    }

    public TwitchAccountSettings LoadBroadcaster()
    {
        return Load(TwitchOAuthRole.Broadcaster);
    }

    public TwitchAccountSettings Load(TwitchOAuthRole role)
    {
        lock (_syncLock)
        {
            return JsonStoreClone.DeepClone(GetAccountUnsafe(role));
        }
    }

    public void Mutate(TwitchOAuthRole role, Action<TwitchAccountSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        lock (_syncLock)
        {
            var account = GetAccountUnsafe(role);
            mutator(account);
            PersistInternal();

            _logger?.LogDebug("AccountsStore: применена мутация для роли {Role}, состояние сохранено в {FilePath}",
                role,
                _filePath);
        }
    }

    public bool TryClearAccessToken(TwitchOAuthRole role, string expectedToken)
    {
        ArgumentNullException.ThrowIfNull(expectedToken);

        lock (_syncLock)
        {
            var account = GetAccountUnsafe(role);

            if (!string.Equals(account.AccessToken, expectedToken, StringComparison.Ordinal))
            {
                _logger?.LogDebug("AccountsStore.TryClearAccessToken: токен роли {Role} уже изменился — очистка пропущена",
                    role);

                return false;
            }

            account.AccessToken = string.Empty;
            account.AccessTokenExpiresAt = null;
            PersistInternal();

            _logger?.LogInformation("AccountsStore: access-токен роли {Role} очищён по запросу 401-обработчика",
                role);

            return true;
        }
    }

    public void SaveAll(TwitchAccountSettings bot, TwitchAccountSettings broadcaster)
    {
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(broadcaster);

        lock (_syncLock)
        {
            _bot = JsonStoreClone.DeepClone(bot);
            _broadcaster = JsonStoreClone.DeepClone(broadcaster);
            PersistInternal();

            _logger?.LogInformation("AccountsStore: оба аккаунта заменены целиком и сохранены в {FilePath}",
                _filePath);
        }
    }

    private static void ApplyScopeDefaults(TwitchAccountSettings account, IReadOnlyList<string> defaultScopes)
    {
        if (account.Scopes.Length == 0)
        {
            account.Scopes = [..defaultScopes];
        }
    }

    private TwitchAccountSettings GetAccountUnsafe(TwitchOAuthRole role)
    {
        return role switch
        {
            TwitchOAuthRole.Bot => _bot,
            TwitchOAuthRole.Broadcaster => _broadcaster,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private void PersistInternal()
    {
        var dto = new AccountsFileDto
        {
            BotAccount = _bot,
            BroadcasterAccount = _broadcaster,
        };

        var json = JsonSerializer.Serialize(dto, JsonStoreOptions.Default);
        AtomicFile.Save(_filePath, json, _logger);
    }

    private AccountsFileDto ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger?.LogDebug("AccountsStore: файл {FilePath} не найден, используются дефолты", _filePath);
            return new();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AccountsFileDto>(json, JsonStoreOptions.Default) ?? new();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Ошибка чтения {FilePath}, применяются дефолты", _filePath);
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger, AccountsTokenRedactor.Redact);
            return new();
        }
    }

    private sealed class AccountsFileDto
    {
        public TwitchAccountSettings? BotAccount { get; set; }
        public TwitchAccountSettings? BroadcasterAccount { get; set; }
    }
}
