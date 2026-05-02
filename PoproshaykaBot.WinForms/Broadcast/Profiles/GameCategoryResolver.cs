using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Settings.Stores;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class GameCategoryResolver(
    ITwitchSearchApi searchApi,
    RecentCategoriesStore recentCategoriesStore,
    ILogger<GameCategoryResolver> logger) : IGameCategoryResolver
{
    private const int SearchPageSize = 10;

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

    public Task RememberAsync(GameSuggestion suggestion)
    {
        recentCategoriesStore.Remember(suggestion);
        return Task.CompletedTask;
    }
}
