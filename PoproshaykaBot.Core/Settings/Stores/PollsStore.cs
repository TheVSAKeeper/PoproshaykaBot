using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Polls;
using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

public class PollsStore
{
    private readonly ILogger<PollsStore>? _logger;
    private readonly string _filePath;
    private readonly object _syncLock = new();

    private PollsSettings _state;

    public PollsStore(ILogger<PollsStore>? logger = null, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? AppPaths.SettingsFile("polls.json");
        _state = ReadFile();

        _logger?.LogDebug("PollsStore инициализирован из {FilePath} (профилей: {ProfileCount})",
            _filePath,
            _state.Profiles.Count);
    }

    public virtual PollsSettings Load()
    {
        lock (_syncLock)
        {
            return JsonStoreClone.DeepClone(_state);
        }
    }

    public virtual void Mutate(Action<PollsSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        lock (_syncLock)
        {
            mutator(_state);
            PersistInternal();

            _logger?.LogDebug("PollsStore: применена мутация, состояние сохранено (профилей: {ProfileCount})",
                _state.Profiles.Count);
        }
    }

    public virtual void Save(PollsSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_syncLock)
        {
            _state = JsonStoreClone.DeepClone(value);
            PersistInternal();

            _logger?.LogInformation("PollsStore: состояние заменено целиком (профилей: {ProfileCount})",
                _state.Profiles.Count);
        }
    }

    private void PersistInternal()
    {
        var json = JsonSerializer.Serialize(_state, JsonStoreOptions.Default);
        AtomicFile.Save(_filePath, json, _logger);
    }

    private PollsSettings ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger?.LogDebug("PollsStore: файл {FilePath} не найден, используются дефолты", _filePath);
            return new();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<PollsSettings>(json, JsonStoreOptions.Default) ?? new();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Ошибка чтения {FilePath}, применяются дефолты", _filePath);
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger);
            return new();
        }
    }
}
