using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Hosting;

namespace PoproshaykaBot.Core.Statistics;

public sealed class StatisticsAutoSaver : IHostedComponent, IAsyncDisposable
{
    private static readonly TimeSpan DefaultAutoSaveInterval = TimeSpan.FromMinutes(1);

    private readonly IReadOnlyList<IPeriodicStatisticsUnit> _units;
    private readonly IReadOnlyList<IFlushableHistoryStore> _flushableStores;
    private readonly ILogger<StatisticsAutoSaver> _logger;
    private readonly TimeSpan _autoSaveInterval;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    private PeriodicTimer? _periodicTimer;
    private CancellationTokenSource? _cts;
    private Task? _autoSaveTask;
    private bool _disposed;
    private long _lastRunAtUtcTicks;
    private long _nextRunAtUtcTicks;
    private string? _lastError;

    public StatisticsAutoSaver(
        IEnumerable<IPeriodicStatisticsUnit> units,
        IEnumerable<IFlushableHistoryStore> flushableStores,
        ILogger<StatisticsAutoSaver> logger)
        : this(units, flushableStores, logger, DefaultAutoSaveInterval)
    {
    }

    internal StatisticsAutoSaver(
        IEnumerable<IPeriodicStatisticsUnit> units,
        IEnumerable<IFlushableHistoryStore> flushableStores,
        ILogger<StatisticsAutoSaver> logger,
        TimeSpan autoSaveInterval)
    {
        _units = [.. units];
        _flushableStores = [.. flushableStores];
        _logger = logger;
        _autoSaveInterval = autoSaveInterval;
    }

    public string Name => "Инициализация статистики...";

    public int StartOrder => 100;

    public TimeSpan Interval => _autoSaveInterval;

    public DateTimeOffset? LastRunAt => ToTimestamp(Interlocked.Read(ref _lastRunAtUtcTicks));

    public DateTimeOffset? NextRunAt => ToTimestamp(Interlocked.Read(ref _nextRunAtUtcTicks));

    public string? LastError => Volatile.Read(ref _lastError);

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_autoSaveTask != null)
        {
            _logger.LogWarning("Попытка повторного запуска автосохранения статистики проигнорирована");
            return;
        }

        _logger.LogDebug("Запуск автосохранения статистики...");

        try
        {
            await LoadAsync(cancellationToken).ConfigureAwait(false);
            foreach (var unit in _units)
            {
                unit.OnStarted();
            }

            _cts = new();
            _periodicTimer = new(_autoSaveInterval);
            Interlocked.Exchange(ref _nextRunAtUtcTicks, (DateTimeOffset.UtcNow + _autoSaveInterval).UtcTicks);
            _autoSaveTask = RunAutoSaveLoopAsync(_cts.Token);

            _logger.LogInformation("Автосохранение статистики запущено (интервал: {Interval})", _autoSaveInterval);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Критическая ошибка при запуске автосохранения статистики");
            throw new InvalidOperationException($"Ошибка запуска автосохранения статистики: {exception.Message}", exception);
        }
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Инициирована остановка автосохранения статистики");

        if (_cts != null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        if (_autoSaveTask != null)
        {
            try
            {
                await _autoSaveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogDebug(ex, "Задача автосохранения успешно отменена");
            }
        }

        _periodicTimer?.Dispose();
        _periodicTimer = null;
        _autoSaveTask = null;
        Interlocked.Exchange(ref _nextRunAtUtcTicks, 0);

        try
        {
            if (await StopUnderSaveLockAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogInformation("Автосохранение статистики корректно остановлено");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "Ошибка при финальном сохранении статистики");
            throw new InvalidOperationException($"Ошибка остановки автосохранения статистики: {exception.Message}", exception);
        }
    }

    public Task SaveNowAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Принудительный вызов сохранения статистики");
        return SaveAsync(true, cancellationToken);
    }

    internal async Task<bool> SaveUnitNowAsync(
        IPeriodicStatisticsUnit unit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(unit);
        await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var work = unit.PrepareSave(force: true);
            if (work is null)
            {
                return false;
            }

            try
            {
                await work.CommitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch
            {
                work.Rollback();
                throw;
            }
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Освобождение ресурсов StatisticsAutoSaver");

        await StopAsync(new Progress<string>(), CancellationToken.None).ConfigureAwait(false);
        _cts?.Dispose();
        _saveSemaphore.Dispose();
        _disposed = true;
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Начало загрузки статистики");

        await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var unit in _units)
            {
                await unit.LoadForStartAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Сбой при комплексной загрузке статистики");
            throw new InvalidOperationException($"Ошибка загрузки статистики: {exception.Message}", exception);
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    private async Task RunAutoSaveLoopAsync(CancellationToken ct)
    {
        _logger.LogDebug("Цикл автосохранения запущен");

        try
        {
            while (await _periodicTimer!.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await SaveAsync(false, ct).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogError(exception, "Сбой тика автосохранения статистики, цикл продолжает работу");
                }

                Interlocked.Exchange(ref _nextRunAtUtcTicks, (DateTimeOffset.UtcNow + _autoSaveInterval).UtcTicks);
            }
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogDebug(ex, "Цикл автосохранения прерван (OperationCanceledException)");
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _lastError, exception.Message);
            _logger.LogError(exception, "Непредвиденная ошибка в фоновом цикле автосохранения статистики");
        }
        finally
        {
            Interlocked.Exchange(ref _nextRunAtUtcTicks, 0);
        }
    }

    private async Task<bool> StopUnderSaveLockAsync(CancellationToken cancellationToken)
    {
        await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_units.Any(static unit => unit.IsLoaded))
            {
                FlushHistories();
                _logger.LogDebug("Остановка автосохранения без финального сохранения: загрузка статистики не выполнялась");
                return false;
            }

            _logger.LogDebug("Финальное сохранение статистики при остановке");

            await SaveUnderSaveLockAsync(true, cancellationToken).ConfigureAwait(false);

            return true;
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    private async Task SaveAsync(bool force, CancellationToken cancellationToken)
    {
        await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await SaveUnderSaveLockAsync(force, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }

    private async Task SaveUnderSaveLockAsync(bool force, CancellationToken cancellationToken)
    {
        try
        {
            FlushHistories();

            var prepared = new List<ISnapshotSaveWork>(_units.Count);
            foreach (var unit in _units)
            {
                if (unit.PrepareSave(force) is { } work)
                {
                    prepared.Add(work);
                }
            }

            if (prepared.Count == 0)
            {
                RecordRun(null);
                return;
            }

            var committed = 0;
            try
            {
                for (; committed < prepared.Count; committed++)
                {
                    await prepared[committed].CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                for (var i = committed; i < prepared.Count; i++)
                {
                    prepared[i].Rollback();
                }

                throw;
            }

            RecordRun(null);
            _logger.LogDebug("Сохранение статистики успешно завершено");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RecordRun(exception.Message);
            _logger.LogError(exception, "Сбой при попытке сохранения статистики");
            throw new InvalidOperationException($"Ошибка сохранения статистики: {exception.Message}", exception);
        }
    }

    private void FlushHistories()
    {
        foreach (var store in _flushableStores)
        {
            store.TryFlush();
        }
    }

    private static DateTimeOffset? ToTimestamp(long utcTicks)
    {
        return utcTicks == 0 ? null : new DateTimeOffset(utcTicks, TimeSpan.Zero);
    }

    private void RecordRun(string? error)
    {
        Interlocked.Exchange(ref _lastRunAtUtcTicks, DateTimeOffset.UtcNow.UtcTicks);
        Volatile.Write(ref _lastError, error);
    }
}
