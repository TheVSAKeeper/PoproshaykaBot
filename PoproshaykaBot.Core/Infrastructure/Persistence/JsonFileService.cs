using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.Core.Infrastructure.Persistence;

/// <summary>
/// Сервис для работы с JSON файлами.
/// Предоставляет единообразные методы сериализации и десериализации.
/// </summary>
public class JsonFileService
{
    private static readonly JsonSerializerOptions DefaultOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Загружает объект из JSON файла.
    /// </summary>
    /// <typeparam name="T">Тип объекта для десериализации.</typeparam>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="options">Опции сериализации (опционально).</param>
    /// <returns>Десериализованный объект или null.</returns>
    public async Task<T?> LoadAsync<T>(string filePath, JsonSerializerOptions? options = null) where T : class
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Путь к файлу не может быть пустым", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            var result = JsonSerializer.Deserialize<T>(json, options ?? DefaultOptions);
            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Ошибка загрузки из файла {filePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Загружает объект из JSON файла синхронно.
    /// </summary>
    public T? Load<T>(string filePath, JsonSerializerOptions? options = null) where T : class
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Путь к файлу не может быть пустым", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var result = JsonSerializer.Deserialize<T>(json, options ?? DefaultOptions);
            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Ошибка загрузки из файла {filePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Сохраняет объект в JSON файл с поддержкой атомарной записи.
    /// </summary>
    /// <typeparam name="T">Тип объекта для сериализации.</typeparam>
    /// <param name="filePath">Путь к файлу.</param>
    /// <param name="data">Данные для сохранения.</param>
    /// <param name="options">Опции сериализации (опционально).</param>
    /// <param name="createBackup">Создать резервную копию перед перезаписью.</param>
    public async Task SaveAsync<T>(string filePath, T data, JsonSerializerOptions? options = null, bool createBackup = false)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Путь к файлу не может быть пустым", nameof(filePath));
        }

        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(data, options ?? DefaultOptions);
            var tempFilePath = filePath + ".tmp";

            await File.WriteAllTextAsync(tempFilePath, json, Encoding.UTF8);

            if (createBackup && File.Exists(filePath) && !File.Exists(filePath + ".bak"))
            {
                File.Copy(filePath, filePath + ".bak", true);
            }

            File.Replace(tempFilePath, filePath, filePath + ".old");

            var oldFilePath = filePath + ".old";
            if (File.Exists(oldFilePath))
            {
                try
                {
                    File.Delete(oldFilePath);
                }
                catch
                {
                }
            }
        }
        catch (Exception ex)
        {
            if (!createBackup || !File.Exists(filePath + ".bak"))
            {
                throw new InvalidOperationException($"Ошибка сохранения в файл {filePath}: {ex.Message}", ex);
            }

            try
            {
                File.Copy(filePath + ".bak", filePath, true);
                Console.WriteLine($"Восстановлен файл из бэкапа: {filePath}");
            }
            catch (Exception backupEx)
            {
                Console.WriteLine($"Ошибка восстановления из бэкапа: {backupEx.Message}");
            }

            throw new InvalidOperationException($"Ошибка сохранения в файл {filePath}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Сохраняет объект в JSON файл синхронно.
    /// </summary>
    public void Save<T>(string filePath, T data, JsonSerializerOptions? options = null, bool createBackup = false)
    {
        SaveAsync(filePath, data, options, createBackup).GetAwaiter().GetResult();
    }
}
