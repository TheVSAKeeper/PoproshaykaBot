using Microsoft.Extensions.Logging;
using System.Text;

namespace PoproshaykaBot.Core.Infrastructure.Persistence;

public static class AtomicFile
{
    public static void Save(string targetPath, string content, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(targetPath);
        ArgumentNullException.ThrowIfNull(content);

        var directory = Path.GetDirectoryName(targetPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            WriteAtomic(targetPath, content, logger);
        }
        catch
        {
            TryRestoreFromBackup(targetPath, logger);
            throw;
        }
    }

    private static void WriteAtomic(string targetPath, string content, ILogger? logger)
    {
        var tempPath = targetPath + ".tmp";

        File.WriteAllText(tempPath, content, Encoding.UTF8);

        if (File.Exists(targetPath))
        {
            var backupPath = targetPath + ".bak";
            File.Copy(targetPath, backupPath, true);

            var oldPath = targetPath + ".old";
            File.Replace(tempPath, targetPath, oldPath);
            TryDelete(oldPath, logger);
        }
        else
        {
            File.Move(tempPath, targetPath);
        }
    }

    private static void TryRestoreFromBackup(string targetPath, ILogger? logger)
    {
        var backupPath = targetPath + ".bak";

        if (!File.Exists(backupPath))
        {
            return;
        }

        try
        {
            File.Copy(backupPath, targetPath, true);
            logger?.LogInformation("Содержимое {TargetPath} восстановлено из бэкапа {BackupPath}", targetPath, backupPath);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Не удалось восстановить {TargetPath} из бэкапа {BackupPath}", targetPath, backupPath);
        }
    }

    private static void TryDelete(string path, ILogger? logger)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Не удалось удалить временный файл {Path}", path);
        }
    }
}
