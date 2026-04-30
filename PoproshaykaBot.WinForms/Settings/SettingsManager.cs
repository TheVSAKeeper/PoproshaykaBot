using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Infrastructure;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Settings;
using PoproshaykaBot.WinForms.Infrastructure.Persistence;
using PoproshaykaBot.WinForms.Settings.Migrations;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PoproshaykaBot.WinForms.Settings;

public class SettingsManager
{
    private readonly ILogger<SettingsManager> _logger;
    private readonly IEventBus _eventBus;
    private readonly string _settingsFilePath;

    private readonly object _syncLock = new();

    private AppSettings? _currentSettings;

    public SettingsManager(ILogger<SettingsManager> logger, IEventBus eventBus)
    {
        _logger = logger;
        _eventBus = eventBus;
        _settingsFilePath = AppPaths.Combine("settings.json");
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
        _logger.LogDebug("Начало сохранения настроек в {SettingsFilePath}", _settingsFilePath);

        lock (_syncLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, GetJsonOptions());
                AtomicFile.Save(_settingsFilePath, json, _logger);

                _currentSettings = settings;
                _logger.LogInformation("Настройки приложения успешно сохранены");

                InvokeChatSettingsChanged(settings.Twitch.ObsChat);
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

    private static JsonSerializerOptions GetJsonOptions()
    {
        return new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    private void InvokeChatSettingsChanged(ObsChatSettings obsChatSettings)
    {
        _ = _eventBus.PublishAsync(new ChatSettingsChangedEvent(obsChatSettings));
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
            var options = GetJsonOptions();
            var node = JsonNode.Parse(json);

            if (node is not JsonObject root)
            {
                throw new InvalidOperationException("Корневой элемент settings.json не является JSON-объектом");
            }

            if (SettingsMigrator.TryMigrate(root, _logger))
            {
                CreateBackupFile(_settingsFilePath, "pre-migration");
                var migratedJson = root.ToJsonString(options);
                AtomicFile.Save(_settingsFilePath, migratedJson, _logger);
                _logger.LogInformation("Настройки мигрированы в актуальный формат и сохранены");
            }

            var settings = root.Deserialize<AppSettings>(options);

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

            CreateBackupFile(_settingsFilePath, "invalid");

            _logger.LogWarning("Из-за ошибки загрузки применяются настройки по умолчанию");
            return CreateDefaultSettings();
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
            _logger.LogInformation("Создан бэкап поврежденного файла настроек: {BackupPath}", backupPath);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка при создании бэкапа для файла {OriginalPath}", originalPath);
        }
    }
}
