using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Settings.Obs;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class ObsChatStore
{
    private readonly IEventBus _eventBus;
    private readonly JsonStore<ObsChatSettings> _store;

    public ObsChatStore(IEventBus eventBus, ILogger<ObsChatStore>? logger = null, string? filePath = null, SettingsWriteGate? gate = null)
    {
        ArgumentNullException.ThrowIfNull(eventBus);

        var path = filePath ?? AppPaths.SettingsFile("obs-chat.json");

        _eventBus = eventBus;
        _store = new(path, logger, describe: Describe, gate: gate);

        logger?.LogDebug("ObsChatStore инициализирован из {FilePath}", path);
    }

    public static string Describe(ObsChatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return $"сообщений на экране {settings.MaxMessages}, время {SettingsDescriber.OnOff(settings.ShowTimestamp)}, "
            + $"аватары {SettingsDescriber.OnOff(settings.ShowUserAvatars)}, анимации {SettingsDescriber.OnOff(settings.EnableAnimations)}, "
            + $"затухание {SettingsDescriber.OnOff(settings.EnableMessageFadeOut)} через {settings.MessageLifetimeSeconds} с, "
            + $"кегль {settings.FontSize}, автопрокрутка {SettingsDescriber.OnOff(settings.AutoScrollEnabled)}, "
            + $"картинки из сообщений {SettingsDescriber.OnOff(settings.ShowMessageImages)} (роли {settings.MessageImageRoles}, "
            + $"хостов {settings.MessageImageAllowedHosts?.Count ?? 0}, высота {settings.MessageImageMaxHeightPixels} px)";
    }

    public ObsChatSettings Load()
    {
        return _store.Load();
    }

    public void Save(ObsChatSettings value)
    {
        var committed = JsonStoreClone.DeepClone(value);
        _store.Save(committed);

        _ = _eventBus.PublishAsync(new ChatSettingsChangedEvent(committed));
    }
}
