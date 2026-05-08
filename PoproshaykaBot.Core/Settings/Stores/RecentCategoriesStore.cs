using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class RecentCategoriesStore
{
    private const int MaxCachedCategories = 20;

    private readonly ILogger<RecentCategoriesStore>? _logger;
    private readonly string _filePath;
    private readonly object _syncLock = new();

    private readonly List<GameCategoryCacheEntry> _items;

    public RecentCategoriesStore(ILogger<RecentCategoriesStore>? logger = null, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? AppPaths.SettingsFile("recent-categories.json");
        _items = ReadFile();
    }

    public IReadOnlyList<GameCategoryCacheEntry> Load()
    {
        lock (_syncLock)
        {
            return _items.ToList();
        }
    }

    public void Remember(GameSuggestion suggestion)
    {
        ArgumentNullException.ThrowIfNull(suggestion);

        lock (_syncLock)
        {
            _items.RemoveAll(e => e.Id == suggestion.Id);
            _items.Insert(0, new()
            {
                Id = suggestion.Id,
                Name = suggestion.Name,
                LastUsedAt = DateTimeOffset.UtcNow,
            });

            while (_items.Count > MaxCachedCategories)
            {
                _items.RemoveAt(_items.Count - 1);
            }

            SaveInternal();
        }
    }

    public void Save()
    {
        lock (_syncLock)
        {
            SaveInternal();
        }
    }

    private void SaveInternal()
    {
        var dto = new RecentCategoriesFileDto { Items = _items };
        var json = JsonSerializer.Serialize(dto, JsonStoreOptions.Default);
        AtomicFile.Save(_filePath, json, _logger);
    }

    private List<GameCategoryCacheEntry> ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var dto = JsonSerializer.Deserialize<RecentCategoriesFileDto>(json, JsonStoreOptions.Default);
            return dto?.Items ?? [];
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Ошибка чтения {FilePath}, применяются дефолты", _filePath);
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger);
            return [];
        }
    }

    private sealed class RecentCategoriesFileDto
    {
        public List<GameCategoryCacheEntry> Items { get; init; } = [];
    }
}
