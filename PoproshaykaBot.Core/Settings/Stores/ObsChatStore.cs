using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Obs;
using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class ObsChatStore
{
    private readonly IEventBus _eventBus;
    private readonly ILogger<ObsChatStore>? _logger;
    private readonly string _filePath;
    private readonly object _syncLock = new();

    private ObsChatSettings _state;

    public ObsChatStore(IEventBus eventBus, ILogger<ObsChatStore>? logger = null, string? filePath = null)
    {
        _eventBus = eventBus;
        _logger = logger;
        _filePath = filePath ?? AppPaths.SettingsFile("obs-chat.json");
        _state = ReadFile();

        _logger?.LogDebug("ObsChatStore инициализирован из {FilePath}", _filePath);
    }

    public ObsChatSettings Load()
    {
        lock (_syncLock)
        {
            return JsonStoreClone.DeepClone(_state);
        }
    }

    public void Save(ObsChatSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        ObsChatSettings broadcastCopy;

        lock (_syncLock)
        {
            _state = JsonStoreClone.DeepClone(value);
            var json = JsonSerializer.Serialize(_state, JsonStoreOptions.Default);
            AtomicFile.Save(_filePath, json, _logger);

            broadcastCopy = JsonStoreClone.DeepClone(_state);

            _logger?.LogInformation("ObsChatStore: настройки чата сохранены в {FilePath}", _filePath);
        }

        _ = _eventBus.PublishAsync(new ChatSettingsChangedEvent(broadcastCopy));
    }

    private ObsChatSettings ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger?.LogDebug("ObsChatStore: файл {FilePath} не найден, используются дефолты", _filePath);
            return new();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ObsChatSettings>(json, JsonStoreOptions.Default) ?? new();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Ошибка чтения {FilePath}, применяются дефолты", _filePath);
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger);
            return new();
        }
    }
}
