using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;

namespace PoproshaykaBot.WinForms.Broadcast.Profiles;

public sealed class ChannelUpdateConfirmationService : IChannelUpdateConfirmation, IEventSubscriber, IDisposable
{
    private readonly object _expectationsLock = new();
    private readonly List<Expectation> _expectations = [];
    private readonly IDisposable _subscription;

    public ChannelUpdateConfirmationService(IEventBus eventBus)
    {
        _subscription = eventBus.Subscribe<ChannelUpdated>(OnChannelUpdated);
    }

    public void Dispose()
    {
        _subscription.Dispose();

        Expectation[] pending;

        lock (_expectationsLock)
        {
            pending = _expectations.ToArray();
            _expectations.Clear();
        }

        foreach (var expectation in pending)
        {
            expectation.Tcs.TrySetResult(false);
        }
    }

    public async Task<bool> AwaitAsync(string? expectedTitle, string? expectedGameId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var expectation = new Expectation(expectedTitle, expectedGameId, tcs);

        lock (_expectationsLock)
        {
            _expectations.Add(expectation);
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            await using (timeoutCts.Token.Register(() => tcs.TrySetResult(false)))
            {
                return await tcs.Task;
            }
        }
        finally
        {
            lock (_expectationsLock)
            {
                _expectations.Remove(expectation);
            }
        }
    }

    private static bool Matches(Expectation expectation, string actualTitle, string actualGameId)
    {
        if (expectation.ExpectedTitle is not null
            && !string.Equals(expectation.ExpectedTitle, actualTitle, StringComparison.Ordinal))
        {
            return false;
        }

        return expectation.ExpectedGameId is null
               || string.Equals(expectation.ExpectedGameId, actualGameId, StringComparison.Ordinal);
    }

    private void OnChannelUpdated(ChannelUpdated @event)
    {
        Expectation[] matches;

        lock (_expectationsLock)
        {
            matches = _expectations
                .Where(expectation => Matches(expectation, @event.Title, @event.GameId))
                .ToArray();
        }

        foreach (var match in matches)
        {
            match.Tcs.TrySetResult(true);
        }
    }

    private sealed record Expectation(string? ExpectedTitle, string? ExpectedGameId, TaskCompletionSource<bool> Tcs);
}
