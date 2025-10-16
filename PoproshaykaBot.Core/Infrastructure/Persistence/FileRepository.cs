namespace PoproshaykaBot.Core.Infrastructure.Persistence;

/// <summary>
/// Базовый репозиторий для работы с файловым хранилищем.
/// Предоставляет высокоуровневые операции загрузки/сохранения с обработкой ошибок.
/// </summary>
/// <typeparam name="T">Тип данных для хранения.</typeparam>
public class FileRepository<T> where T : class, new()
{
    private readonly string _filePath;
    private readonly JsonFileService _jsonService;
    private readonly FileBackupService _backupService;

    public FileRepository(string filePath, JsonFileService jsonService, FileBackupService backupService)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Путь к файлу не может быть пустым", nameof(filePath));
        }

        _filePath = filePath;
        _jsonService = jsonService ?? throw new ArgumentNullException(nameof(jsonService));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
    }

    /// <summary>
    /// Загружает данные из файла. Если файл не существует или повреждён, возвращает новый экземпляр.
    /// </summary>
    public async Task<T> LoadAsync()
    {
        try
        {
            var data = await _jsonService.LoadAsync<T>(_filePath);
            return data ?? new T();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки данных из {_filePath}: {ex.Message}");

            _backupService.CreateBackup(_filePath, "invalid");

            return new();
        }
    }

    /// <summary>
    /// Сохраняет данные в файл с автоматическим созданием бэкапа.
    /// </summary>
    public async Task SaveAsync(T data, bool createBackup = true)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        try
        {
            await _jsonService.SaveAsync(_filePath, data, createBackup: createBackup);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка сохранения данных в {_filePath}: {ex.Message}");

            if (_backupService.RestoreFromBackup(_filePath))
            {
                Console.WriteLine("Данные восстановлены из бэкапа");
            }

            throw;
        }
    }

    /// <summary>
    /// Загружает данные синхронно.
    /// </summary>
    public T Load()
    {
        return LoadAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Сохраняет данные синхронно.
    /// </summary>
    public void Save(T data, bool createBackup = true)
    {
        SaveAsync(data, createBackup).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Проверяет существование файла с данными.
    /// </summary>
    public bool Exists()
    {
        return File.Exists(_filePath);
    }

    /// <summary>
    /// Удаляет файл с данными.
    /// </summary>
    public bool Delete()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка удаления файла {_filePath}: {ex.Message}");
            return false;
        }
    }
}
