using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.YouTube.Quota;

public sealed class YouTubeQuotaLease : IDisposable
{
    internal static readonly YouTubeQuotaLease Empty = new();

    private readonly YouTubeQuotaGovernor? _governor;
    private readonly string _projectKey;
    private readonly DateOnly _pacificDate;
    private readonly PlatformAuthRole _role;
    private int _state;

    private YouTubeQuotaLease()
    {
        _projectKey = string.Empty;
        _state = 2;
    }

    internal YouTubeQuotaLease(
        YouTubeQuotaGovernor governor,
        string projectKey,
        DateOnly pacificDate,
        PlatformAuthRole role,
        int cost)
    {
        _governor = governor;
        _projectKey = projectKey;
        _pacificDate = pacificDate;
        _role = role;
        Cost = cost;
    }

    public int Cost { get; }

    public bool IsCommitted => Volatile.Read(ref _state) == 1;

    public void Commit()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0 || _governor is null)
        {
            return;
        }

        _governor.Commit(_projectKey, _pacificDate, _role, Cost);
    }

    public void Refund()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) != 0 || _governor is null)
        {
            return;
        }

        _governor.Refund(_projectKey, _pacificDate, Cost);
    }

    public void MarkExhaustedFromServer()
    {
        _governor?.MarkExhaustedFromServer(_projectKey, _pacificDate, _role);
    }

    public void Dispose()
    {
        Refund();
    }
}
