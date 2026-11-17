using PoproshaykaBot.Core.Statistics;
using Serilog;
using System.Windows;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Bootstrap;

public sealed class StatisticsPreload : IDisposable
{
    private readonly UserStatisticsLoader _loader;
    private readonly Action<Action> _postBackground;
    private readonly Lock _sync = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _loadTask;
    private bool _stopped;
    private bool _disposed;

    public StatisticsPreload(UserStatisticsLoader loader, Action<Action>? postBackground = null)
    {
        ArgumentNullException.ThrowIfNull(loader);

        _loader = loader;
        _postBackground = postBackground ?? DispatchUiBackground;
    }

    public bool IsCompleted => Volatile.Read(ref _loadTask) is { IsCompleted: true };

    public void Start()
    {
        _ = EnsureLoadedAsync();
    }

    public Task EnsureLoadedAsync()
    {
        lock (_sync)
        {
            if (_loadTask is not null)
            {
                return _loadTask;
            }

            if (_disposed || _stopped)
            {
                _loadTask = Task.CompletedTask;
                return _loadTask;
            }

            var token = _shutdown.Token;
            _loadTask = Task.Run(() => StatisticsBootstrap.LoadCoreAsync(_loader, token), CancellationToken.None);
            return _loadTask;
        }
    }

    public void ScheduleAfterLoad(Action onUiReady)
    {
        ArgumentNullException.ThrowIfNull(onUiReady);

        var task = EnsureLoadedAsync();

        if (task.IsCompleted)
        {
            InvokeBackgroundIfActive(onUiReady);
            return;
        }

        _ = task.ContinueWith(
            static (_, state) =>
            {
                var (preload, callback) = ((StatisticsPreload, Action))state!;
                preload.InvokeBackgroundIfActive(callback);
            },
            (this, onUiReady),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public void Stop(CancellationToken cancellationToken = default)
    {
        Task? task;

        lock (_sync)
        {
            _stopped = true;
            task = _loadTask;
            if (!_disposed)
            {
                _shutdown.Cancel();
            }
        }

        if (task is not { IsCompleted: false })
        {
            return;
        }

        if (cancellationToken.CanBeCanceled)
        {
            try
            {
                task.Wait(cancellationToken);
                return;
            }
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                Log.ForContext<StatisticsPreload>()
                    .Warning(exception, "Истёк мягкий таймаут ожидания предзагрузки статистики; дожидаемся завершения чтения до освобождения контейнера");
            }
        }

        task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Task? task;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            task = _loadTask;
            _shutdown.Cancel();
        }

        if (task is null or { IsCompleted: true })
        {
            _shutdown.Dispose();
        }
        else
        {
            _ = task.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                _shutdown,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void InvokeBackgroundIfActive(Action callback)
    {
        if (_disposed || _shutdown.IsCancellationRequested)
        {
            return;
        }

        _postBackground(() =>
        {
            if (_disposed || _shutdown.IsCancellationRequested)
            {
                return;
            }

            callback();
        });
    }

    private static void DispatchUiBackground(Action callback)
    {
        if (Application.Current?.Dispatcher is { HasShutdownStarted: false, HasShutdownFinished: false } dispatcher)
        {
            _ = dispatcher.BeginInvoke(DispatcherPriority.Background, callback);
            return;
        }

        callback();
    }
}
