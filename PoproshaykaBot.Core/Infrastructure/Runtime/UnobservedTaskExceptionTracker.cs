namespace PoproshaykaBot.Core.Infrastructure.Runtime;

public sealed record UnobservedTaskExceptionEntry(
    DateTimeOffset ObservedAtUtc,
    string ExceptionType,
    string? TopFrame);

public sealed record UnobservedTaskExceptionRecordResult(
    bool IsCancellationOnly,
    int InnerCount,
    int TotalCount,
    UnobservedTaskExceptionEntry? Entry);

public sealed record UnobservedTaskExceptionSnapshot(
    int FailureCount,
    int CanceledCount,
    IReadOnlyList<UnobservedTaskExceptionEntry> RecentFailures);

public sealed class UnobservedTaskExceptionTracker(TimeProvider? timeProvider = null, int maxEntries = 10)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly int _maxEntries = Math.Max(1, maxEntries);
    private readonly Lock _sync = new();
    private readonly Queue<UnobservedTaskExceptionEntry> _recent = new();

    private int _failureCount;
    private int _canceledCount;

    public UnobservedTaskExceptionRecordResult Record(AggregateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var flattened = exception.Flatten().InnerExceptions;

        if (flattened.Count > 0 && flattened.All(static item => item is OperationCanceledException))
        {
            int canceledCount;
            lock (_sync)
            {
                _canceledCount++;
                canceledCount = _canceledCount;
            }

            return new(IsCancellationOnly: true, flattened.Count, canceledCount, Entry: null);
        }

        var primary = flattened.FirstOrDefault(static item => item is not OperationCanceledException) ?? exception;
        var entry = new UnobservedTaskExceptionEntry(
            _timeProvider.GetUtcNow(),
            primary.GetType().FullName ?? primary.GetType().Name,
            RunStateStore.ExtractTopFrame(primary));

        int failureCount;
        lock (_sync)
        {
            _failureCount++;
            failureCount = _failureCount;

            if (_recent.Count == _maxEntries)
            {
                _recent.Dequeue();
            }

            _recent.Enqueue(entry);
        }

        return new(IsCancellationOnly: false, flattened.Count, failureCount, entry);
    }

    public UnobservedTaskExceptionSnapshot Capture()
    {
        lock (_sync)
        {
            return new(_failureCount, _canceledCount, [.. _recent]);
        }
    }
}
