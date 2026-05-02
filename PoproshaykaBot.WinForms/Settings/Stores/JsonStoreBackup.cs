using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.WinForms.Settings.Stores;

internal static class JsonStoreBackup
{
    public static void CreateBackup(string filePath, string suffix, ILogger? logger = null)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var directory = Path.GetDirectoryName(filePath)!;
            var name = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            var backupPath = Path.Combine(directory, $"{name}.{suffix}-{timestamp}{extension}");

            File.Copy(filePath, backupPath, true);
            logger?.LogInformation("Создан бэкап файла настроек: {BackupPath}", backupPath);
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Ошибка при создании бэкапа для файла {FilePath}", filePath);
        }
    }
}
