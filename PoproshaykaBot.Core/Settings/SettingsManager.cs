using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Migrations;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoproshaykaBot.Core.Settings;

public class SettingsManager
{
    private readonly ILogger<SettingsManager> _logger;
    private readonly string _settingsFilePath;
    private readonly object _syncLock = new();

    private AppSettings? _currentSettings;

    public SettingsManager(ILogger<SettingsManager> logger)
        : this(logger, null)
    {
    }

    public SettingsManager(ILogger<SettingsManager> logger, string? settingsFilePath)
    {
        _logger = logger;
        _settingsFilePath = settingsFilePath ?? AppPaths.SettingsFile("settings.json");
    }

    public virtual AppSettings Current
    {
        get
        {
            lock (_syncLock)
            {
                return _currentSettings ??= LoadSettingsInternal();
            }
        }
    }

    public virtual void SaveSettings(AppSettings settings)
    {
        _logger.LogDebug("Начало сохранения user-настроек в {SettingsFilePath}", _settingsFilePath);

        lock (_syncLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, JsonStoreOptions.Default);
                AtomicFile.Save(_settingsFilePath, json, _logger);

                _currentSettings = settings;
                _logger.LogInformation("User-настройки приложения успешно сохранены");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Критическая ошибка при сохранении настроек в {SettingsFilePath}", _settingsFilePath);
                throw new InvalidOperationException($"Ошибка сохранения настроек: {exception.Message}", exception);
            }
        }
    }

    private static AppSettings CreateDefaultSettings()
    {
        return new();
    }

    private AppSettings LoadSettingsInternal()
    {
        _logger.LogDebug("Начало загрузки настроек из {SettingsFilePath}", _settingsFilePath);

        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                _logger.LogInformation("Файл настроек {SettingsFilePath} не найден. Применяются настройки по умолчанию", _settingsFilePath);
                return CreateDefaultSettings();
            }

            var json = File.ReadAllText(_settingsFilePath, Encoding.UTF8);
            var node = JsonNode.Parse(json);

            if (node is not JsonObject root)
            {
                throw new InvalidOperationException("Корневой элемент settings.json не является JSON-объектом");
            }

            var baseDirectory = Path.GetDirectoryName(_settingsFilePath)!;

            if (SettingsMigrator.TryMigrate(root, _logger, baseDirectory))
            {
                JsonStoreBackup.CreateBackup(_settingsFilePath, "pre-migration", _logger);
                var migratedJson = root.ToJsonString(JsonStoreOptions.Default);
                AtomicFile.Save(_settingsFilePath, migratedJson, _logger);
                _logger.LogInformation("Настройки мигрированы в актуальный формат и сохранены");
            }

            var settings = root.Deserialize<AppSettings>(JsonStoreOptions.Default);

            if (settings == null)
            {
                throw new InvalidOperationException("Не удалось десериализовать настройки (null)");
            }

            _logger.LogInformation("Настройки приложения успешно загружены");
            return settings;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка загрузки или десериализации настроек из файла {SettingsFilePath}", _settingsFilePath);

            JsonStoreBackup.CreateBackup(_settingsFilePath, "invalid", _logger);

            _logger.LogWarning("Из-за ошибки загрузки применяются настройки по умолчанию");
            return CreateDefaultSettings();
        }
    }
}
