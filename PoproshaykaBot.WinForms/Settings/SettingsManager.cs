using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Settings;

public class SettingsManager
{
    private readonly string _settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PoproshaykaBot");

    private readonly string _settingsFilePath;

    private AppSettings? _currentSettings;

    public SettingsManager()
    {
        _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");
        _currentSettings = LoadSettings();
    }

    public event Action<ObsChatSettings>? ChatSettingsChanged;

    public AppSettings Current => _currentSettings ?? throw new InvalidOperationException("Settings not loaded");

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);

            var json = JsonSerializer.Serialize(settings, GetJsonOptions());

            var tempFilePath = _settingsFilePath + ".tmp";
            File.WriteAllText(tempFilePath, json, Encoding.UTF8);

            var backupCreated = false;

            if (File.Exists(_settingsFilePath) && !File.Exists(_settingsFilePath + ".bak"))
            {
                File.Copy(_settingsFilePath, _settingsFilePath + ".bak", true);
                backupCreated = true;
            }

            File.Replace(tempFilePath, _settingsFilePath, _settingsFilePath + ".old");

            var oldFilePath = _settingsFilePath + ".old";

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

            _currentSettings = settings;

            ChatSettingsChanged?.Invoke(settings.Twitch.ObsChat);

            if (backupCreated)
            {
                Console.WriteLine($"Создан бэкап настроек: {_settingsFilePath}.bak");
            }
        }
        catch (Exception exception)
        {
            if (File.Exists(_settingsFilePath + ".bak"))
            {
                try
                {
                    File.Copy(_settingsFilePath + ".bak", _settingsFilePath, true);
                    Console.WriteLine("Восстановлены настройки из бэкапа");
                }
                catch (Exception backupException)
                {
                    Console.WriteLine($"Ошибка восстановления из бэкапа: {backupException.Message}");
                }
            }

            throw new InvalidOperationException($"Ошибка сохранения настроек: {exception.Message}", exception);
        }
    }

    private static AppSettings CreateDefaultSettings()
    {
        return new();
    }

    private static JsonSerializerOptions GetJsonOptions()
    {
        return new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    private AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                var defaultSettings = CreateDefaultSettings();
                SaveSettings(defaultSettings);
                return defaultSettings;
            }

            var json = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, GetJsonOptions());

            _currentSettings = settings ?? throw new InvalidOperationException("Не удалось десериализовать настройки");
            return settings;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Неожиданная ошибка загрузки настроек: {exception.Message}");
            CreateBackupFile(_settingsFilePath, "invalid");

            var defaultSettings = CreateDefaultSettings();
            _currentSettings = defaultSettings;
            return defaultSettings;
        }
    }

    private void CreateBackupFile(string originalPath, string suffix)
    {
        if (!File.Exists(originalPath))
        {
            return;
        }

        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var fileName = Path.GetFileNameWithoutExtension(originalPath);
            var extension = Path.GetExtension(originalPath);
            var backupFileName = $"{fileName}.{suffix}-{timestamp}{extension}";
            var backupPath = Path.Combine(Path.GetDirectoryName(originalPath)!, backupFileName);

            File.Copy(originalPath, backupPath, true);
            Console.WriteLine($"Создан бэкап поврежденного файла: {backupPath}");
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Ошибка создания бэкапа файла: {exception.Message}");
        }
    }
}
