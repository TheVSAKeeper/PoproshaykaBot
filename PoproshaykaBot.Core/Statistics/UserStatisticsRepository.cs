using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public sealed class UserStatisticsRepository(ILogger<UserStatisticsRepository> logger) : IUserStatisticsRepository
{
    private readonly FrozenDictionary<StreamingPlatform, UserStatisticsPartition> _partitions =
        Enum.GetValues<StreamingPlatform>()
            .ToFrozenDictionary(platform => platform, platform => new UserStatisticsPartition(platform, logger));

    public bool HasChanges => _partitions.Values.Any(static partition => partition.HasChanges);

    public IPlatformUserStatistics ForPlatform(StreamingPlatform platform) => GetPartition(platform);

    public IReadOnlyList<UserStatistics> GetAll()
    {
        var result = new List<UserStatistics>();
        foreach (var partition in _partitions.Values)
        {
            result.AddRange(partition.GetAll());
        }

        return result;
    }

    public void TrackMessage(PlatformUserKey key, string username)
    {
        GetPartition(key.Platform).TrackMessage(key.UserId, username);
    }

    public bool IncrementBonusPoints(PlatformUserKey key, ulong delta)
    {
        return GetPartition(key.Platform).IncrementBonusPoints(key.UserId, delta);
    }

    public bool IncrementPenaltyPoints(PlatformUserKey key, ulong delta)
    {
        return GetPartition(key.Platform).IncrementPenaltyPoints(key.UserId, delta);
    }

    public void SeedPlatform(StreamingPlatform platform, IEnumerable<UserStatistics> users)
    {
        GetPartition(platform).Replace(users);
    }

    internal UserStatisticsPartition GetPartition(StreamingPlatform platform)
    {
        return _partitions.TryGetValue(platform, out var partition)
            ? partition
            : throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
    }
}
