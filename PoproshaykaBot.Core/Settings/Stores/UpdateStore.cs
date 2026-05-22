using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Update;
using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class UpdateStore
{
    private readonly ILogger<UpdateStore>? _logger;
    private readonly string _filePath;
    private readonly object _syncLock = new();

    private UpdateSettings _state;

    public UpdateStore(ILogger<UpdateStore>? logger = null, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? AppPaths.SettingsFile("update.json");
        _state = ReadFile();

        _logger?.LogDebug("UpdateStore инициализирован из {FilePath}", _filePath);
    }

    public UpdateSettings Load()
    {
        lock (_syncLock)
        {
            return JsonStoreClone.DeepClone(_state);
        }
    }

    public void Save(UpdateSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_syncLock)
        {
            _state = JsonStoreClone.DeepClone(value);
            var json = JsonSerializer.Serialize(_state, JsonStoreOptions.Default);
            AtomicFile.Save(_filePath, json, _logger);

            _logger?.LogInformation("UpdateStore: настройки обновления сохранены в {FilePath}", _filePath);
        }
    }

    private UpdateSettings ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger?.LogDebug("UpdateStore: файл {FilePath} не найден, используются дефолты", _filePath);
            return new();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<UpdateSettings>(json, JsonStoreOptions.Default) ?? new();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Ошибка чтения {FilePath}, применяются дефолты", _filePath);
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger);
            return new();
        }
    }
}
