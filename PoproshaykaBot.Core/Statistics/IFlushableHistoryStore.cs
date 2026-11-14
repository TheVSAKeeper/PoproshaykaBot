namespace PoproshaykaBot.Core.Statistics;

public interface IFlushableHistoryStore
{
    bool TryFlush();
}
