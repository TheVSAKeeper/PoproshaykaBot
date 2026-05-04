using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public sealed class AppLifetime
{
    private readonly IReadOnlyList<IAppLifetimeComponent> _components;
    private readonly ILogger<AppLifetime> _logger;
    private readonly List<IAppLifetimeComponent> _started = [];
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    public AppLifetime(IEnumerable<IAppLifetimeComponent> components, ILogger<AppLifetime> logger)
    {
        _components = components.OrderBy(c => c.StartOrder).ToArray();
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_started.Count > 0)
            {
                throw new InvalidOperationException("AppLifetime уже запущен. Вызовите StopAsync перед повторным StartAsync.");
            }

            if (_components.Count == 0)
            {
                return;
            }

            foreach (var component in _components)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation("Запуск компонента жизненного цикла приложения {Component} (порядок {Order})", component.Name, component.StartOrder);
                await component.StartAsync(cancellationToken).ConfigureAwait(false);
                _started.Add(component);
            }
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
            for (var i = _started.Count - 1; i >= 0; i--)
            {
                var component = _started[i];

                try
                {
                    _logger.LogInformation("Остановка компонента жизненного цикла приложения {Component}", component.Name);
                    await component.StopAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка остановки компонента жизненного цикла {Component}", component.Name);
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
