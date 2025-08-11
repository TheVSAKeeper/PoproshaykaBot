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
            File.WriteAllText(_settingsFilePath, json, Encoding.UTF8);

            _currentSettings = settings;

            ChatSettingsChanged?.Invoke(settings.Twitch.ObsChat);
        }
        catch (Exception exception)
        {
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
            if (File.Exists(_settingsFilePath) == false)
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
            var defaultSettings = CreateDefaultSettings();
            _currentSettings = defaultSettings;
            return defaultSettings;
        }
    }
}
