namespace PoproshaykaBot.WinForms.Infrastructure.Reconnection;

public sealed class ExponentialBackoffPolicy
{
    private static readonly TimeSpan DefaultMaxDelay = TimeSpan.FromMinutes(5);

    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    public ExponentialBackoffPolicy(int maxAttempts, TimeSpan? baseDelay = null, TimeSpan? maxDelay = null)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Must be at least 1.");
        }

        MaxAttempts = maxAttempts;
        _baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        _maxDelay = maxDelay ?? DefaultMaxDelay;
    }

    public int MaxAttempts { get; }

    public int CurrentAttempt { get; private set; }

    public bool HasAttemptsLeft => CurrentAttempt < MaxAttempts;

    public bool TryNextAttempt(out TimeSpan delay)
    {
        if (CurrentAttempt >= MaxAttempts)
        {
            delay = TimeSpan.Zero;
            return false;
        }

        CurrentAttempt++;
        var multiplier = Math.Pow(2, CurrentAttempt - 1);
        var ms = Math.Min(_baseDelay.TotalMilliseconds * multiplier, _maxDelay.TotalMilliseconds);
        delay = TimeSpan.FromMilliseconds(ms);
        return true;
    }

    public void Reset()
    {
        CurrentAttempt = 0;
    }
}
