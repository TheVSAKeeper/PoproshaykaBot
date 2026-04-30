using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class GameCategoryResolver(
    ITwitchSearchApi searchApi,
    SettingsManager settingsManager,
    ILogger<GameCategoryResolver> logger) : IGameCategoryResolver
{
    private const int MaxCachedCategories = 20;
    private const int SearchPageSize = 10;

    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public async Task<IReadOnlyList<GameSuggestion>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            return await searchApi.SearchCategoriesAsync(query, SearchPageSize, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Поиск категорий '{Query}' не удался", query);
            return [];
        }
    }

    public async Task<GameSuggestion?> ResolveAsync(string query, CancellationToken cancellationToken)
    {
        var results = await SearchAsync(query, cancellationToken);

        if (results.Count == 0)
        {
            return null;
        }

        var first = results[0];
        await RememberAsync(first);
        return first;
    }

    public async Task RememberAsync(GameSuggestion suggestion)
    {
        await _cacheLock.WaitAsync();

        try
        {
            var settings = settingsManager.Current;
            var recents = settings.Twitch.Infrastructure.RecentCategories;

            recents.RemoveAll(e => e.Id == suggestion.Id);
            recents.Insert(0, new()
            {
                Id = suggestion.Id,
                Name = suggestion.Name,
                LastUsedAt = DateTimeOffset.UtcNow,
            });

            while (recents.Count > MaxCachedCategories)
            {
                recents.RemoveAt(recents.Count - 1);
            }

            settingsManager.SaveSettings(settings);
        }
        finally
        {
            _cacheLock.Release();
        }
    }
}
