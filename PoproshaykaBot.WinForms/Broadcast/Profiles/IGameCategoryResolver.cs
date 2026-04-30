namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public interface IGameCategoryResolver
{
    Task<IReadOnlyList<GameSuggestion>> SearchAsync(string query, CancellationToken cancellationToken);
    Task<GameSuggestion?> ResolveAsync(string query, CancellationToken cancellationToken);
    Task RememberAsync(GameSuggestion suggestion);
}
