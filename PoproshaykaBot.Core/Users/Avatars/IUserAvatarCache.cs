namespace PoproshaykaBot.Core.Users.Avatars;

public interface IUserAvatarCache
{
    Task<string?> GetUrlAsync(string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<string>> ResolveAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);

    IReadOnlyDictionary<string, string> GetCachedPaths(IReadOnlyCollection<string> userIds);

    Task<IReadOnlyDictionary<string, string>> GetPathsAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default);

    IReadOnlyCollection<string> GetUnavailable(IReadOnlyCollection<string> userIds);

    void Invalidate(string userId);
}
