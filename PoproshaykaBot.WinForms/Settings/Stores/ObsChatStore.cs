using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Settings;
using PoproshaykaBot.WinForms.Infrastructure.Persistence;
using PoproshaykaBot.WinForms.Settings.Obs;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Settings.Stores;

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
    }

    public ObsChatSettings Load()
    {
        return _state;
    }

    public void Save(ObsChatSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_syncLock)
        {
            _state = value;
            var json = JsonSerializer.Serialize(value, JsonStoreOptions.Default);
            AtomicFile.Save(_filePath, json, _logger);
        }

        _ = _eventBus.PublishAsync(new ChatSettingsChangedEvent(value));
    }

    private ObsChatSettings ReadFile()
    {
        if (!File.Exists(_filePath))
        {
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
