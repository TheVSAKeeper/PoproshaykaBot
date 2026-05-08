using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public class AppHost
{
    private static readonly IProgress<string> NullProgress = new Progress<string>(_ => { });

    private readonly IReadOnlyList<IHostedComponent> _components;
    private readonly ILogger _logger;
    private readonly List<IHostedComponent> _started = [];
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    public AppHost(IEnumerable<IHostedComponent> components, ILogger<AppHost> logger)
        : this(components, (ILogger)logger)
    {
    }

    protected AppHost(IEnumerable<IHostedComponent> components, ILogger logger)
    {
        _components = components.OrderBy(c => c.StartOrder).ToArray();
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return StartAsync(NullProgress, cancellationToken);
    }

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_started.Count > 0)
            {
                throw new InvalidOperationException("AppHost уже запущен. Вызовите StopAsync перед повторным StartAsync.");
            }

            if (_components.Count == 0)
            {
                _logger.LogDebug("AppHost запущен без зарегистрированных компонентов");
                return;
            }

            foreach (var component in _components)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation("Запуск компонента {Component} (порядок {Order})", component.Name, component.StartOrder);
                progress.Report(component.Name);

                await component.StartAsync(progress, cancellationToken).ConfigureAwait(false);
                _started.Add(component);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return StopAsync(NullProgress, cancellationToken);
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            for (var i = _started.Count - 1; i >= 0; i--)
            {
                var component = _started[i];

                try
                {
                    _logger.LogInformation("Остановка компонента {Component}", component.Name);
                    await component.StopAsync(progress, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка остановки компонента {Component}", component.Name);
                    progress.Report($"Ошибка остановки '{component.Name}': {ex.Message}");
                }
            }

            _started.Clear();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }
}
