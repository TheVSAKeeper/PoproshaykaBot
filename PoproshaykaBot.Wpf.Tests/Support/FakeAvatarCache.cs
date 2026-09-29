using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.Tests.Support;

internal sealed class FakeAvatarCache : IUserAvatarCache
{
    private readonly Lock _sync = new();

    public Dictionary<string, string> Cached { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Downloadable { get; } = new(StringComparer.Ordinal);

    public List<string> Changed { get; } = [];

    public HashSet<string> Unavailable { get; } = new(StringComparer.Ordinal);

    public List<string[]> CachedRequests { get; } = [];

    public List<string[]> ResolveRequests { get; } = [];

    public List<string[]> NetworkRequests { get; } = [];

    public List<string> Invalidated { get; } = [];

    public static UserAvatarProvider CreateProvider(FakeAvatarCache? cache = null)
    {
        return new(cache ?? new FakeAvatarCache(), NullLogger<UserAvatarProvider>.Instance);
    }

    public Task<string?> GetUrlAsync(string userId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<IReadOnlyCollection<string>> ResolveAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            ResolveRequests.Add([.. userIds]);

            foreach (var userId in Changed)
            {
                Cached.Remove(userId);
            }

            return Task.FromResult<IReadOnlyCollection<string>>([.. Changed]);
        }
    }

    public IReadOnlyDictionary<string, string> GetCachedPaths(IReadOnlyCollection<string> userIds)
    {
        lock (_sync)
        {
            CachedRequests.Add([.. userIds]);

            return Pick(userIds, Cached);
        }
    }

    public Task<IReadOnlyDictionary<string, string>> GetPathsAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            NetworkRequests.Add([.. userIds]);

            var found = new Dictionary<string, string>(Pick(userIds, Cached), StringComparer.Ordinal);

            foreach (var (userId, path) in Pick(userIds, Downloadable))
            {
                found[userId] = path;
                Cached[userId] = path;
            }

            return Task.FromResult<IReadOnlyDictionary<string, string>>(found);
        }
    }

    public IReadOnlyCollection<string> GetUnavailable(IReadOnlyCollection<string> userIds)
    {
        lock (_sync)
        {
            return [.. userIds.Where(Unavailable.Contains)];
        }
    }

    public void Invalidate(string userId)
    {
        lock (_sync)
        {
            Invalidated.Add(userId);
            Cached.Remove(userId);
        }
    }

    private static Dictionary<string, string> Pick(IReadOnlyCollection<string> userIds, Dictionary<string, string> source)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var userId in userIds)
        {
            if (source.TryGetValue(userId, out var path))
            {
                result[userId] = path;
            }
        }

        return result;
    }
}
