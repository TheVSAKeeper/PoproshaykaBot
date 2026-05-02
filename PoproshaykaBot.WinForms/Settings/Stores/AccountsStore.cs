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
    }

    public TwitchAccountSettings LoadBot()
    {
        return _bot;
    }

    public TwitchAccountSettings LoadBroadcaster()
    {
        return _broadcaster;
    }

    public TwitchAccountSettings Load(TwitchOAuthRole role)
    {
        return role switch
        {
            TwitchOAuthRole.Bot => _bot,
            TwitchOAuthRole.Broadcaster => _broadcaster,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    public void SaveAll()
    {
        lock (_syncLock)
        {
            PersistInternal();
        }
    }

    public void SaveAll(TwitchAccountSettings bot, TwitchAccountSettings broadcaster)
    {
        ArgumentNullException.ThrowIfNull(bot);
        ArgumentNullException.ThrowIfNull(broadcaster);

        lock (_syncLock)
        {
            _bot = bot;
            _broadcaster = broadcaster;
            PersistInternal();
        }
    }

    private static void ApplyScopeDefaults(TwitchAccountSettings account, IReadOnlyList<string> defaultScopes)
    {
        if (account.Scopes.Length == 0)
        {
            account.Scopes = [..defaultScopes];
        }
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
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger);
            return new();
        }
    }

    private sealed class AccountsFileDto
    {
        public TwitchAccountSettings? BotAccount { get; set; }
        public TwitchAccountSettings? BroadcasterAccount { get; set; }
    }
}
