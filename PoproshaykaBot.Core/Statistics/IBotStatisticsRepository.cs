namespace PoproshaykaBot.Core.Statistics;

public interface IBotStatisticsRepository
{
    bool HasChanges { get; }

    BotStatistics GetSnapshot();

    void IncrementMessagesProcessed();

    void ResetStartTime();

    void Replace(BotStatistics statistics);

    BotStatistics CreateSnapshotAndMarkSaved();

    void MarkChanged();
}
