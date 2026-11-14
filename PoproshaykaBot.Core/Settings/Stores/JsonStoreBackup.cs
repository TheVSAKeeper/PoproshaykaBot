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
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var directory = Path.GetDirectoryName(filePath)!;
            var name = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            var candidatePath = Path.Combine(directory, $"{name}.{suffix}-{timestamp}{extension}");

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
}
