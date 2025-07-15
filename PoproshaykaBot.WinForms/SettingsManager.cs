using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.WinForms;

public static class SettingsManager
{
    private static readonly string SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PoproshaykaBot");

    private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

    private static AppSettings? _currentSettings;
    public static AppSettings Current => _currentSettings ??= LoadSettings();

    public static AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsFilePath) == false)
            {
                var defaultSettings = CreateDefaultSettings();
                SaveSettings(defaultSettings);
                return defaultSettings;
            }

            var json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, GetJsonOptions());

            if (settings == null)
            {
                throw new InvalidOperationException("Не удалось десериализовать настройки");
            }

            ValidateSettings(settings);

            _currentSettings = settings;
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

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            ValidateSettings(settings);

            Directory.CreateDirectory(SettingsDirectory);

            var json = JsonSerializer.Serialize(settings, GetJsonOptions());
            File.WriteAllText(SettingsFilePath, json, Encoding.UTF8);

            _currentSettings = settings;
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

    private static void ValidateSettings(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Twitch.BotUsername))
        {
            settings.Twitch.BotUsername = "bobito217";
        }

        if (string.IsNullOrWhiteSpace(settings.Twitch.Channel))
        {
            settings.Twitch.Channel = "bobito217";
        }

        if (settings.Twitch.MessagesAllowedInPeriod <= 0)
        {
            settings.Twitch.MessagesAllowedInPeriod = 750;
        }

        if (settings.Twitch.ThrottlingPeriodSeconds <= 0)
        {
            settings.Twitch.ThrottlingPeriodSeconds = 30;
        }
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
}
