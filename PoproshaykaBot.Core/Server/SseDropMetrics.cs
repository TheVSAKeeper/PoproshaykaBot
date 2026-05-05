namespace PoproshaykaBot.Core.Server;

public sealed class SseDropMetrics
{
    private long _globalDrops;
    private long _clientDrops;

    public long GlobalDropCount => Interlocked.Read(ref _globalDrops);

    public long ClientDropCount => Interlocked.Read(ref _clientDrops);

    public long IncrementGlobalDrops()
    {
        return Interlocked.Increment(ref _globalDrops);
    }

    public long IncrementClientDrops()
    {
        return Interlocked.Increment(ref _clientDrops);
    }
}
