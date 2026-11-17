using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public interface IUserStatisticsRepository
{
    IPlatformUserStatistics ForPlatform(StreamingPlatform platform);

    IReadOnlyList<UserStatistics> GetAll();

    void TrackMessage(PlatformUserKey key, string username);

    void TrackMessage(ChannelKey channelKey, PlatformUserKey key, string username);

    bool IncrementBonusPoints(PlatformUserKey key, ulong delta);

    bool IncrementPenaltyPoints(PlatformUserKey key, ulong delta);

    bool IncrementBonusPoints(StatisticsCounterTarget target, PlatformUserKey key, ulong delta, string? fallbackUserName = null);

    bool IncrementPenaltyPoints(StatisticsCounterTarget target, PlatformUserKey key, ulong delta, string? fallbackUserName = null);

    IReadOnlyList<ViewerAggregateStatistics> QuerySlice(StatisticsSlice slice);

    IReadOnlyList<ViewerAggregateStatistics> GetTop(StatisticsSlice slice, int count, UserTopMode mode = UserTopMode.Points);

    ViewerAggregateStatistics? FindViewerById(StatisticsSlice slice, PlatformUserKey key);

    ViewerAggregateStatistics? FindViewerByName(StatisticsSlice slice, string name, StreamingPlatform? preferredPlatform = null);

    IReadOnlySet<PlatformUserKey> GetLinkedKeys(PlatformUserKey key);

    IReadOnlyList<ViewerAccountRecord> GetKnownAccounts();

    IReadOnlyList<ChannelKey> GetKnownChannels();

    IReadOnlyList<StreamingPlatform> GetUnattributedPlatforms();

    AccountLinkGroupRecord? GetLinkGroup(PlatformUserKey key);

    AccountLinkOperationStatus LinkAccounts(
        IReadOnlyList<PlatformUserKey> keys,
        AccountLinkOrigin origin,
        AccountLinkAuthor author,
        bool allowMergeExistingGroups,
        out AccountLinkGroupRecord? resultGroup);

    AccountLinkOperationStatus UnlinkAccount(PlatformUserKey key, AccountLinkAuthor author);
}
