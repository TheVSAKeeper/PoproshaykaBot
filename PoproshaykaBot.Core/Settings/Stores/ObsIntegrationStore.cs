using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Settings.Obs;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class ObsIntegrationStore
{
    private readonly IEventBus _eventBus;
    private readonly JsonStore<ObsIntegrationSettings> _store;

    public ObsIntegrationStore(IEventBus eventBus, ILogger<ObsIntegrationStore>? logger = null, string? filePath = null, SettingsWriteGate? gate = null)
    {
        ArgumentNullException.ThrowIfNull(eventBus);

        var path = filePath ?? AppPaths.SettingsFile("obs-integration.json");

        _eventBus = eventBus;
        _store = new(path, logger, describe: Describe, gate: gate);

        logger?.LogDebug("ObsIntegrationStore инициализирован из {FilePath}", path);
    }

    public static string Describe(ObsIntegrationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return $"интеграция {SettingsDescriber.OnOff(settings.Enabled)}, автоподключение {SettingsDescriber.OnOff(settings.AutoConnect)}, "
            + $"{SettingsDescriber.Value(settings.Host)}:{settings.Port}, пароль {SettingsDescriber.Present(settings.Password)}, "
            + $"сцена {SettingsDescriber.Value(settings.SceneName)}, источник {SettingsDescriber.Value(settings.SourceName)} {settings.Width}x{settings.Height}, "
            + $"автосоздание источника {SettingsDescriber.OnOff(settings.AutoProvisionBrowserSource)}, "
            + $"сцена по профилю {SettingsDescriber.OnOff(settings.ApplySceneOnProfile)}, профиль по сцене {SettingsDescriber.OnOff(settings.ApplyProfileOnScene)}, "
            + $"источников дашборда {settings.DashboardSourceNames.Count}, микрофон {SettingsDescriber.Present(settings.DashboardMicrophoneName)}, "
            + $"задержка индикатора {settings.DashboardVolumeMeterDelayMs} мс, "
            + $"источников чата для обновления {settings.ChatRefreshSources.Count}, "
            + $"обновление на старте стрима {SettingsDescriber.OnOff(settings.RefreshChatSourcesOnStreamStart)}";
    }

    public ObsIntegrationSettings Load()
    {
        return _store.Load();
    }

    public void Save(ObsIntegrationSettings value)
    {
        var committed = JsonStoreClone.DeepClone(value);
        _store.Save(committed);

        _ = _eventBus.PublishAsync(new ObsIntegrationSettingsChangedEvent(committed));
    }
}
