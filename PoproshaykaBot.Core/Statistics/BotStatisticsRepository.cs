namespace PoproshaykaBot.Core.Statistics;

public sealed class BotStatisticsRepository : IBotStatisticsRepository
{
    private readonly object _lock = new();
    private BotStatistics _statistics = BotStatistics.Create();
    private bool _hasChanges;

    public bool HasChanges
    {
        get
        {
            lock (_lock)
            {
                return _hasChanges;
            }
        }
    }

    public BotStatistics GetSnapshot()
    {
        lock (_lock)
        {
            _statistics.UpdateUptime();
            return _statistics.Clone();
        }
    }

    public void IncrementMessagesProcessed()
    {
        lock (_lock)
        {
            _statistics.IncrementMessagesProcessed();
            _hasChanges = true;
        }
    }

    public void ResetStartTime()
    {
        lock (_lock)
        {
            _statistics.ResetStartTime();
            _hasChanges = true;
        }
    }

    public void Replace(BotStatistics statistics)
    {
        ArgumentNullException.ThrowIfNull(statistics);

        lock (_lock)
        {
            _statistics = statistics;
            _hasChanges = false;
        }
    }

    public BotStatistics CreateSnapshotAndMarkSaved()
    {
        lock (_lock)
        {
            _statistics.UpdateUptime();
            var snapshot = _statistics.Clone();
            _hasChanges = false;
            return snapshot;
        }
    }

    public void MarkChanged()
    {
        lock (_lock)
        {
            _hasChanges = true;
        }
    }
}
