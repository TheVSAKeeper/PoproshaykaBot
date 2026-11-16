namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

internal sealed class ManualTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
{
    private readonly Lock _sync = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow = initialUtcNow;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _utcNow;
        }
    }

    public void SetUtcNow(DateTimeOffset utcNow)
    {
        lock (_sync)
        {
            _utcNow = utcNow;
        }

        FireDueTimers();
    }

    public void Advance(TimeSpan delta)
    {
        lock (_sync)
        {
            _utcNow = _utcNow.Add(delta);
        }

        FireDueTimers();
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    private void Register(ManualTimer timer)
    {
        lock (_sync)
        {
            if (!_timers.Contains(timer))
            {
                _timers.Add(timer);
            }
        }
    }

    private void Unregister(ManualTimer timer)
    {
        lock (_sync)
        {
            _timers.Remove(timer);
        }
    }

    private void FireDueTimers()
    {
        while (true)
        {
            List<(TimerCallback Callback, object? State)> due = [];

            lock (_sync)
            {
                for (var i = _timers.Count - 1; i >= 0; i--)
                {
                    var timer = _timers[i];

                    if (timer.IsDisposed || timer.NextDueUtc is not { } nextDue || nextDue > _utcNow)
                    {
                        continue;
                    }

                    due.Add((timer.Callback, timer.State));

                    if (timer.Period == Timeout.InfiniteTimeSpan || timer.Period <= TimeSpan.Zero)
                    {
                        timer.NextDueUtc = null;
                    }
                    else
                    {
                        timer.NextDueUtc = nextDue + timer.Period;
                    }
                }
            }

            if (due.Count == 0)
            {
                return;
            }

            foreach (var item in due)
            {
                item.Callback(item.State);
            }
        }
    }

    private sealed class ManualTimer(
        ManualTimeProvider owner,
        TimerCallback callback,
        object? state)
        : ITimer
    {
        public TimerCallback Callback { get; } = callback;

        public object? State { get; } = state;

        public DateTimeOffset? NextDueUtc { get; set; }

        public TimeSpan Period { get; private set; } = Timeout.InfiniteTimeSpan;

        public bool IsDisposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._sync)
            {
                if (IsDisposed)
                {
                    return false;
                }

                Period = period;

                if (dueTime == Timeout.InfiniteTimeSpan)
                {
                    NextDueUtc = null;
                    return true;
                }

                owner.Register(this);
                NextDueUtc = dueTime <= TimeSpan.Zero
                    ? owner._utcNow
                    : owner._utcNow.Add(dueTime);
            }

            if (dueTime <= TimeSpan.Zero)
            {
                owner.FireDueTimers();
            }

            return true;
        }

        public void Dispose()
        {
            IsDisposed = true;
            owner.Unregister(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
