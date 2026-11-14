using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public interface IUserStatisticsRepository
{
    IPlatformUserStatistics ForPlatform(StreamingPlatform platform);

    IReadOnlyList<UserStatistics> GetAll();

    void TrackMessage(PlatformUserKey key, string username);

    bool IncrementBonusPoints(PlatformUserKey key, ulong delta);

    bool IncrementPenaltyPoints(PlatformUserKey key, ulong delta);
}
