using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Update;

public sealed class UpdateBackgroundService(
    IUpdateCoordinator coordinator,
    UpdateStore store,
    TimeProvider timeProvider,
    ILogger<UpdateBackgroundService> logger)
    : IAppLifetimeComponent, IDisposable
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);

    private CancellationTokenSource? _cts;
    private Task? _loop;

    public string Name => "Проверка обновлений";

    public int StartOrder => 50;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (coordinator.Kind == UpdateKind.Unsupported)
        {
            logger.LogInformation("Сборка не поддерживает автообновление — фоновая проверка отключена");
            return Task.CompletedTask;
        }

        _cts?.Dispose();
        _cts = new();
        _loop = RunLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _cts = null;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ожидаемо при остановке
            }
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(InitialDelay, timeProvider, cancellationToken).ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                var settings = store.Load();

                if (settings.AutoCheckEnabled && coordinator.IsUpdatable)
                {
                    await coordinator.CheckNowAsync(cancellationToken).ConfigureAwait(false);
                }

                var hours = Math.Max(1, settings.CheckIntervalHours);
                await Task.Delay(TimeSpan.FromHours(hours), timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // штатная остановка
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Фоновая проверка обновлений остановлена из-за ошибки");
        }
    }
}
