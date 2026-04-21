using TwitchLib.Api;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class TwitchSearchApiAdapter(TwitchAPI twitchApi) : ITwitchSearchApi
{
    public async Task<IReadOnlyList<GameSuggestion>> SearchCategoriesAsync(
        string query,
        int first,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        var response = await twitchApi.Helix.Search.SearchCategoriesAsync(query, first: first);
        cancellationToken.ThrowIfCancellationRequested();

        if (response?.Games == null)
        {
            return [];
        }

        return response.Games
            .Select(g => new GameSuggestion(g.Id, g.Name, g.BoxArtUrl ?? string.Empty))
            .ToList();
    }
}
