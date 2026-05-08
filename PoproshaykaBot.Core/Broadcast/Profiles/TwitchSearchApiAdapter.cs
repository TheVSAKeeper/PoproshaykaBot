using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

public sealed class TwitchSearchApiAdapter(
    [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
    ITwitchHelixClient helix)
    : ITwitchSearchApi
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
        var games = await helix.SearchCategoriesAsync(query, first, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return games
            .Select(g => new GameSuggestion(g.Id, g.Name, g.BoxArtUrl ?? string.Empty))
            .ToList();
    }
}
