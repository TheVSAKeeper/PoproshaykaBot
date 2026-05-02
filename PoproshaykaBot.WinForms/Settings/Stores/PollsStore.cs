using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure;
using PoproshaykaBot.WinForms.Infrastructure.Persistence;
using PoproshaykaBot.WinForms.Polls;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Settings.Stores;

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
    }

    public virtual PollsSettings Load()
    {
        return _state;
    }

    public virtual void Save()
    {
        lock (_syncLock)
        {
            var json = JsonSerializer.Serialize(_state, JsonStoreOptions.Default);
            AtomicFile.Save(_filePath, json, _logger);
        }
    }

    public virtual void Save(PollsSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_syncLock)
        {
            _state = value;
            var json = JsonSerializer.Serialize(value, JsonStoreOptions.Default);
            AtomicFile.Save(_filePath, json, _logger);
        }
    }

    private PollsSettings ReadFile()
    {
        if (!File.Exists(_filePath))
        {
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
