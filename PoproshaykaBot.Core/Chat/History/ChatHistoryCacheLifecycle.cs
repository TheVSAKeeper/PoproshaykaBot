using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;

namespace PoproshaykaBot.Core.Chat.History;

public sealed class ChatHistoryCacheLifecycle : IAppLifetimeComponent, IDisposable
{
    private static readonly TimeSpan FlushDebounceDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PeriodicMaintenanceInterval = TimeSpan.FromMinutes(1);

    private readonly ChatHistoryManager _manager;
    private readonly ChatHistoryCacheStore _store;
    private readonly IEventBus _eventBus;
    private readonly ILogger<ChatHistoryCacheLifecycle> _logger;
    private readonly TimeProvider _timeProvider;

    private CancellationTokenSource? _cts;
    private Task? _backgroundTask;
    private bool _disposed;

    public ChatHistoryCacheLifecycle(
        ChatHistoryManager manager,
        ChatHistoryCacheStore store,
        IEventBus eventBus,
        ILogger<ChatHistoryCacheLifecycle> logger,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(logger);

        _manager = manager;
        _store = store;
        _eventBus = eventBus;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Name => nameof(ChatHistoryCacheLifecycle);

    public int StartOrder => -50;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var restoredEvents = await _store.LoadAndRestoreAsync(_manager, cancellationToken).ConfigureAwait(false);

        foreach (var loaded in restoredEvents)
        {
            await _eventBus.PublishAsync(loaded, cancellationToken).ConfigureAwait(false);
        }

        _cts = new();
        _backgroundTask = Task.Run(() => RunBackgroundLoopAsync(_cts.Token), CancellationToken.None);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_backgroundTask is not null)
        {
            try
            {
                await _backgroundTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _store.FlushDirtyAsync(_manager, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }

    private async Task RunBackgroundLoopAsync(CancellationToken cancellationToken)
    {
        var lastMaintenanceAt = _timeProvider.GetUtcNow();

        while (!cancellationToken.IsCancellationRequested)
        {
            var signaled = await _store.WaitForDirtyAsync(PeriodicMaintenanceInterval, cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (signaled)
            {
                try
                {
                    await Task.Delay(FlushDebounceDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            try
            {
                await _store.FlushDirtyAsync(_manager, cancellationToken).ConfigureAwait(false);

                var now = _timeProvider.GetUtcNow();
                if (now - lastMaintenanceAt >= PeriodicMaintenanceInterval)
                {
                    lastMaintenanceAt = now;
                    await _store.PruneExpiredAsync(_manager, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Фоновое обслуживание кэша истории чата завершилось с ошибкой");
            }
        }
    }
}
