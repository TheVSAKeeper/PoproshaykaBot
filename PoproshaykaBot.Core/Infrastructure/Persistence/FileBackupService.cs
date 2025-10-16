namespace PoproshaykaBot.Core.Infrastructure.Persistence;

/// <summary>
/// Сервис для управления резервными копиями файлов.
/// </summary>
public class FileBackupService
{
    /// <summary>
    /// Создаёт резервную копию файла с указанным суффиксом.
    /// </summary>
    /// <param name="originalPath">Путь к оригинальному файлу.</param>
    /// <param name="suffix">Суффикс для имени бэкапа (например, "invalid").</param>
    /// <returns>Путь к созданному бэкапу или null, если не удалось создать.</returns>
    public string? CreateBackup(string originalPath, string suffix)
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            throw new ArgumentException("Путь к файлу не может быть пустым", nameof(originalPath));
        }

        if (string.IsNullOrWhiteSpace(suffix))
        {
            throw new ArgumentException("Суффикс не может быть пустым", nameof(suffix));
        }

        if (!File.Exists(originalPath))
        {
            return null;
        }

        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = Path.GetFileNameWithoutExtension(originalPath);
            var extension = Path.GetExtension(originalPath);
            var directory = Path.GetDirectoryName(originalPath);

            if (string.IsNullOrEmpty(directory))
            {
                throw new InvalidOperationException("Не удалось определить директорию файла");
            }

            var backupFileName = $"{fileName}.{suffix}-{timestamp}{extension}";
            var backupPath = Path.Combine(directory, backupFileName);

            File.Copy(originalPath, backupPath, true);
            Console.WriteLine($"Создан бэкап файла: {backupPath}");

            return backupPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка создания бэкапа файла {originalPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Создаёт резервную копию файла с временной меткой.
    /// </summary>
    /// <param name="originalPath">Путь к оригинальному файлу.</param>
    /// <returns>Путь к созданному бэкапу или null, если не удалось создать.</returns>
    public string? CreateTimestampedBackup(string originalPath)
    {
        return CreateBackup(originalPath, "backup");
    }

    /// <summary>
    /// Восстанавливает файл из бэкапа.
    /// </summary>
    /// <param name="originalPath">Путь к оригинальному файлу.</param>
    /// <param name="backupExtension">Расширение бэкапа (по умолчанию ".bak").</param>
    /// <returns>true, если восстановление прошло успешно.</returns>
    public bool RestoreFromBackup(string originalPath, string backupExtension = ".bak")
    {
        if (string.IsNullOrWhiteSpace(originalPath))
        {
            throw new ArgumentException("Путь к файлу не может быть пустым", nameof(originalPath));
        }

        var backupPath = originalPath + backupExtension;

        if (!File.Exists(backupPath))
        {
            Console.WriteLine($"Бэкап не найден: {backupPath}");
            return false;
        }

        try
        {
            File.Copy(backupPath, originalPath, true);
            Console.WriteLine($"Файл восстановлен из бэкапа: {originalPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка восстановления из бэкапа {backupPath}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Удаляет старые бэкапы файла, оставляя только указанное количество последних.
    /// </summary>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="keepCount">Количество бэкапов для сохранения.</param>
    /// <param name="pattern">Паттерн поиска бэкапов (например, "*.backup-*").</param>
    public void CleanupOldBackups(string filePath, int keepCount = 5, string? pattern = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Путь к файлу не может быть пустым", nameof(filePath));
        }

        if (keepCount < 0)
        {
            throw new ArgumentException("Количество бэкапов не может быть отрицательным", nameof(keepCount));
        }

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var extension = Path.GetExtension(filePath);
        var searchPattern = pattern ?? $"{fileName}.*{extension}";

        try
        {
            var backupFiles = Directory.GetFiles(directory, searchPattern)
                .Where(f => f != filePath)
                .OrderByDescending(File.GetLastWriteTime)
                .Skip(keepCount)
                .ToList();

            foreach (var backupFile in backupFiles)
            {
                try
                {
                    File.Delete(backupFile);
                    Console.WriteLine($"Удалён старый бэкап: {backupFile}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Не удалось удалить бэкап {backupFile}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка очистки старых бэкапов для {filePath}: {ex.Message}");
        }
    }
}
