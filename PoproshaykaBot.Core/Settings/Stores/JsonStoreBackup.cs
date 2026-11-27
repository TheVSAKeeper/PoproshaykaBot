using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Settings.Stores;

internal static class JsonStoreBackup
{
    public static void CreateBackup(string filePath, string suffix, ILogger? logger = null)
    {
        CreateBackup(filePath, suffix, logger, null);
    }

    public static void CreateBackup(string filePath, string suffix, ILogger? logger, Func<string, string>? redactor)
    {
        TryCreateBackup(filePath, suffix, logger, redactor, out _);
    }

    public static bool TryCreateBackup(
        string filePath,
        string suffix,
        ILogger? logger,
        Func<string, string>? redactor,
        out string? backupPath)
    {
        backupPath = null;

        if (!File.Exists(filePath))
        {
            return false;
        }

        try
        {
            var candidatePath = BuildTimestampedPath(filePath, suffix);

            if (redactor == null)
            {
                File.Copy(filePath, candidatePath, true);
                backupPath = candidatePath;
                logger?.LogInformation("Создан бэкап файла: {BackupPath}", candidatePath);
                return true;
            }

            string raw;
            try
            {
                raw = File.ReadAllText(filePath);
            }
            catch (Exception readException)
            {
                logger?.LogError(readException,
                    "Не удалось прочитать {FilePath} для редактируемого бэкапа – бэкап не создан, чтобы не утечь сырое содержимое",
                    filePath);

                return false;
            }

            string redacted;
            try
            {
                redacted = redactor(raw);
            }
            catch (Exception redactException)
            {
                logger?.LogError(redactException,
                    "Сбой редактора при подготовке бэкапа {FilePath} – бэкап не создан",
                    filePath);

                return false;
            }

            File.WriteAllText(candidatePath, redacted);
            backupPath = candidatePath;
            logger?.LogInformation("Создан редактированный бэкап файла: {BackupPath}", candidatePath);
            return true;
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Ошибка при создании бэкапа для файла {FilePath}", filePath);
            return false;
        }
    }

    public static string BuildTimestampedPath(string filePath, string suffix)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return BuildSuffixedPath(filePath, $"{suffix}-{timestamp}");
    }

    public static bool TryCreateOneTimeBackup(string sourcePath, string suffix, ILogger? logger = null)
    {
        if (!File.Exists(sourcePath))
        {
            return true;
        }

        var backupPath = BuildSuffixedPath(sourcePath, suffix);
        if (File.Exists(backupPath))
        {
            return true;
        }

        var tempBackupPath = $"{backupPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(sourcePath, tempBackupPath, overwrite: true);
            File.Move(tempBackupPath, backupPath, overwrite: false);
            logger?.LogInformation("Создана страховочная копия {BackupPath}", backupPath);
            return true;
        }
        catch (Exception exception)
        {
            try
            {
                if (File.Exists(tempBackupPath))
                {
                    File.Delete(tempBackupPath);
                }
            }
            catch (Exception cleanupException)
            {
                logger?.LogDebug(cleanupException, "Не удалось удалить временный файл копии {TempPath}", tempBackupPath);
            }

            logger?.LogError(exception, "Не удалось создать страховочную копию {BackupPath}", backupPath);
            return false;
        }
    }

    private static string BuildSuffixedPath(string filePath, string suffixPart)
    {
        var directory = Path.GetDirectoryName(filePath);
        var name = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        var fileName = $"{name}.{suffixPart}{extension}";
        return string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
    }
}
