using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Hosting;

namespace PoproshaykaBot.Core.Statistics;

public sealed class BroadcastHistoryLifetimeComponent(
    BroadcastHistoryStore historyStore,
    TimeProvider timeProvider,
    ILogger<BroadcastHistoryLifetimeComponent> logger) : IAppLifetimeComponent, IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);

    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CancellationTokenSource? _cts;
    private PeriodicTimer? _timer;
    private Task? _loopTask;
    private bool _disposed;

    public string Name => "Повтор записи истории эфиров";

    public int StartOrder => -100;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_loopTask != null)
            {
                return;
            }

            if (historyStore.HasPendingFlush)
            {
                historyStore.TryFlush();
            }

            _cts = new();
            _timer = new(RetryInterval, timeProvider);
            _loopTask = RunLoopAsync(_cts.Token);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_cts != null)
            {
                await _cts.CancelAsync().ConfigureAwait(false);
            }

            if (_loopTask != null)
            {
                try
                {
                    await _loopTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // expected on shutdown
                }
            }

            _timer?.Dispose();
            _timer = null;
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;

            if (historyStore.HasPendingFlush)
            {
                historyStore.TryFlush();
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cts?.Cancel();
        _timer?.Dispose();
        _cts?.Dispose();
        _lifecycleGate.Dispose();
        _disposed = true;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer!.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    if (historyStore.HasPendingFlush)
                    {
                        historyStore.TryFlush();
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Ошибка периодического повтора записи истории эфиров");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }
}
