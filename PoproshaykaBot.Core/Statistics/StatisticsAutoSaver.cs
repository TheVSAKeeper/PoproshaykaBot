using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Hosting;

namespace PoproshaykaBot.Core.Statistics;

public sealed class StatisticsAutoSaver(
    IUserStatisticsRepository userRepository,
    IBotStatisticsRepository botRepository,
    StatisticsFileStore fileStore,
    ILogger<StatisticsAutoSaver> logger)
    : IHostedComponent, IAsyncDisposable
{
    private static readonly TimeSpan DefaultAutoSaveInterval = TimeSpan.FromMinutes(1);

    private readonly TimeSpan _autoSaveInterval = DefaultAutoSaveInterval;
    private readonly SemaphoreSlim _saveSemaphore = new(1, 1);

    private PeriodicTimer? _periodicTimer;
    private CancellationTokenSource? _cts;
    private Task? _autoSaveTask;
    private bool _disposed;

    public string Name => "Инициализация статистики...";

    public int StartOrder => 100;

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (_autoSaveTask != null)
        {
            logger.LogWarning("Попытка повторного запуска автосохранения статистики проигнорирована");
            return;
        }

        logger.LogDebug("Запуск автосохранения статистики...");

        try
        {
            await LoadAsync(cancellationToken).ConfigureAwait(false);
            botRepository.ResetStartTime();

            _cts = new();
            _periodicTimer = new(_autoSaveInterval);
            _autoSaveTask = RunAutoSaveLoopAsync(_cts.Token);

            logger.LogInformation("Автосохранение статистики запущено (интервал: {Interval})", _autoSaveInterval);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Критическая ошибка при запуске автосохранения статистики");
            throw new InvalidOperationException($"Ошибка запуска автосохранения статистики: {exception.Message}", exception);
        }
    }

    public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        logger.LogDebug("Инициирована остановка автосохранения статистики");

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
            catch (OperationCanceledException)
            {
                logger.LogDebug("Задача автосохранения успешно отменена");
            }
        }

        _periodicTimer?.Dispose();
        _periodicTimer = null;
        _autoSaveTask = null;

        try
        {
            await SaveNowAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Автосохранение статистики корректно остановлено");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка при финальном сохранении статистики");
            throw new InvalidOperationException($"Ошибка остановки автосохранения статистики: {exception.Message}", exception);
        }
    }

    public Task SaveNowAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Принудительный вызов сохранения статистики");
        return SaveAsync(true, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        logger.LogDebug("Освобождение ресурсов StatisticsAutoSaver");

        await StopAsync(new Progress<string>(), CancellationToken.None).ConfigureAwait(false);
        _cts?.Dispose();
        _saveSemaphore.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug("Начало загрузки статистики");

        try
        {
            var users = await fileStore.LoadUsersAsync(cancellationToken).ConfigureAwait(false);
            var bot = await fileStore.LoadBotAsync(cancellationToken).ConfigureAwait(false) ?? BotStatistics.Create();

            userRepository.ReplaceAll(users);
            botRepository.Replace(bot);

            logger.LogInformation("Статистика успешно загружена. Загружено пользователей: {UserCount}", users.Count);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Сбой при комплексной загрузке статистики");
            throw new InvalidOperationException($"Ошибка загрузки статистики: {exception.Message}", exception);
        }
    }

    private async Task RunAutoSaveLoopAsync(CancellationToken ct)
    {
        logger.LogDebug("Цикл автосохранения запущен");

        try
        {
            while (await _periodicTimer!.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await SaveAsync(false, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Цикл автосохранения прерван (OperationCanceledException)");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Непредвиденная ошибка в фоновом цикле автосохранения статистики");
        }
    }

    private async Task SaveAsync(bool force, CancellationToken cancellationToken)
    {
        await _saveSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var saveUsers = force || userRepository.HasChanges;
            var saveBot = force || botRepository.HasChanges;

            if (!saveUsers && !saveBot)
            {
                return;
            }

            List<UserStatistics>? userSnapshot = null;
            BotStatistics? botSnapshot = null;

            if (saveUsers)
            {
                userSnapshot = userRepository.CreateSnapshotAndMarkSaved();
            }

            if (saveBot)
            {
                botSnapshot = botRepository.CreateSnapshotAndMarkSaved();
            }

            if (userSnapshot != null)
            {
                try
                {
                    await fileStore.SaveUsersAsync(userSnapshot, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    userRepository.MarkChanged();
                    throw;
                }
            }

            if (botSnapshot != null)
            {
                try
                {
                    await fileStore.SaveBotAsync(botSnapshot, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    botRepository.MarkChanged();
                    throw;
                }
            }

            logger.LogDebug("Сохранение статистики успешно завершено");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Сбой при попытке сохранения статистики");
            throw new InvalidOperationException($"Ошибка сохранения статистики: {exception.Message}", exception);
        }
        finally
        {
            _saveSemaphore.Release();
        }
    }
}
