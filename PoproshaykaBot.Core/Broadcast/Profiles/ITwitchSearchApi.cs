namespace PoproshaykaBot.Core.Broadcast.Profiles;

public interface ITwitchSearchApi
{
    Task<IReadOnlyList<GameSuggestion>> SearchCategoriesAsync(
        string query,
        int first,
        CancellationToken cancellationToken);
}
