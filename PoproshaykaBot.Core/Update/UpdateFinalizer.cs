using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Update;

public static class UpdateFinalizer
{
    private const int MaxAttempts = 12;
    private const int RetryDelayMs = 300;

    public static void Run(string stagingDirectory, string currentExecutablePath, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var backupPath = currentExecutablePath + UpdateSwapPlanner.BackupSuffix;

        DeleteWithRetry(() => File.Exists(backupPath),
            () => File.Delete(backupPath),
            backupPath,
            logger);

        DeleteWithRetry(() => Directory.Exists(stagingDirectory),
            () => Directory.Delete(stagingDirectory, true),
            stagingDirectory,
            logger);
    }

    private static void DeleteWithRetry(Func<bool> exists, Action delete, string target, ILogger logger)
    {
        if (!exists())
        {
            return;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                delete();
                logger.LogInformation("Очищен временный объект обновления {Target}", target);
                return;
            }
            catch (Exception exception)
            {
                if (attempt == MaxAttempts)
                {
                    logger.LogWarning(exception,
                        "Не удалось удалить {Target} после обновления, будет очищено при следующем запуске",
                        target);

                    return;
                }

                Thread.Sleep(RetryDelayMs);
            }
        }
    }
}
