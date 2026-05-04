using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

public class BroadcastProfilesStore
{
    private readonly ILogger<BroadcastProfilesStore>? _logger;
    private readonly string _filePath;
    private readonly object _syncLock = new();

    private BroadcastProfilesSettings _state;

    public BroadcastProfilesStore(ILogger<BroadcastProfilesStore>? logger = null, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? AppPaths.SettingsFile("broadcast-profiles.json");
        _state = ReadFile();

        _logger?.LogDebug("BroadcastProfilesStore инициализирован из {FilePath} (профилей: {ProfileCount})",
            _filePath,
            _state.Profiles.Count);
    }

    public virtual BroadcastProfilesSettings Load()
    {
        lock (_syncLock)
        {
            return JsonStoreClone.DeepClone(_state);
        }
    }

    public virtual void Mutate(Action<BroadcastProfilesSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        lock (_syncLock)
        {
            mutator(_state);
            PersistInternal();

            _logger?.LogDebug("BroadcastProfilesStore: применена мутация, состояние сохранено (профилей: {ProfileCount})",
                _state.Profiles.Count);
        }
    }

    public virtual void Save(BroadcastProfilesSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_syncLock)
        {
            _state = JsonStoreClone.DeepClone(value);
            PersistInternal();

            _logger?.LogInformation("BroadcastProfilesStore: состояние заменено целиком (профилей: {ProfileCount})",
                _state.Profiles.Count);
        }
    }

    private void PersistInternal()
    {
        var json = JsonSerializer.Serialize(_state, JsonStoreOptions.Default);
        AtomicFile.Save(_filePath, json, _logger);
    }

    private BroadcastProfilesSettings ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger?.LogDebug("BroadcastProfilesStore: файл {FilePath} не найден, используются дефолты", _filePath);
            return new();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<BroadcastProfilesSettings>(json, JsonStoreOptions.Default) ?? new();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Ошибка чтения {FilePath}, применяются дефолты", _filePath);
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger);
            return new();
        }
    }
}
