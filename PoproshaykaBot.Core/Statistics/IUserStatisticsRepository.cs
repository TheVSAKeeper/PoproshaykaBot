namespace PoproshaykaBot.Core.Statistics;

public interface IUserStatisticsRepository
{
    bool HasChanges { get; }

    void TrackMessage(string userId, string username);

    UserStatistics? GetById(string userId);

    UserStatistics? GetByName(string username);

    IReadOnlyList<UserStatistics> GetAll();

    IReadOnlyList<UserStatistics> GetTop(int count, UserTopMode mode = UserTopMode.Points);

    bool IncrementBonusMessages(string userId, ulong delta);

    bool IncrementShtrafMessages(string userId, ulong delta);

    void ReplaceAll(IEnumerable<UserStatistics> users);

    List<UserStatistics> CreateSnapshotAndMarkSaved();

    void MarkChanged();
}
