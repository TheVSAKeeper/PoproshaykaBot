using PoproshaykaBot.Core.Domain.Models.Settings;

namespace PoproshaykaBot.Core.Infrastructure.Persistence;

/// <summary>
/// Менеджер настроек приложения.
/// Использует JsonFileService и FileBackupService для работы с файлами.
/// </summary>
public class SettingsManager
{
    private readonly string _settingsFilePath;
    private readonly JsonFileService _jsonService;
    private readonly FileBackupService _backupService;
    private AppSettings? _currentSettings;

    public SettingsManager()
        : this(new(), new())
    {
    }

    public SettingsManager(JsonFileService jsonService, FileBackupService backupService)
    {
        _jsonService = jsonService ?? throw new ArgumentNullException(nameof(jsonService));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));

        var settingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoproshaykaBot");

        _settingsFilePath = Path.Combine(settingsDirectory, "settings.json");
        _currentSettings = LoadSettings();
    }

    public event Action<ObsChatSettings>? ChatSettingsChanged;

    public AppSettings Current => _currentSettings ?? throw new InvalidOperationException("Settings not loaded");

    public void SaveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            _jsonService.Save(_settingsFilePath, settings, createBackup: true);
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

            var settings = _jsonService.Load<AppSettings>(_settingsFilePath);
            _currentSettings = settings ?? throw new InvalidOperationException("Не удалось десериализовать настройки");
            return settings;
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Неожиданная ошибка загрузки настроек: {exception.Message}");
            _backupService.CreateBackup(_settingsFilePath, "invalid");

            var defaultSettings = CreateDefaultSettings();
            _currentSettings = defaultSettings;
            return defaultSettings;
        }
    }
}
