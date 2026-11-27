using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Update;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class UpdateStore
{
    private readonly JsonStore<UpdateSettings> _store;

    public UpdateStore(ILogger<UpdateStore>? logger = null, string? filePath = null)
    {
        var path = filePath ?? AppPaths.SettingsFile(SettingsFileNames.UpdateFileName);
        _store = new(path, logger, describe: Describe);

        logger?.LogDebug("UpdateStore инициализирован из {FilePath}", path);
    }

    public static string Describe(UpdateSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return $"автопроверка {SettingsDescriber.OnOff(settings.AutoCheckEnabled)} каждые {settings.CheckIntervalHours} ч, "
            + $"режим {settings.ApplyMode}, обновление framework-dependent {SettingsDescriber.OnOff(settings.AllowFrameworkDependentUpdate)}, "
            + $"репозиторий {SettingsDescriber.Value(settings.RepositoryOverride)}, пропущенная версия {SettingsDescriber.Value(settings.SkippedVersion)}, "
            + $"последняя проверка {SettingsDescriber.Moment(settings.LastCheckUtc)}";
    }

    public UpdateSettings Load()
    {
        return _store.Load();
    }

    public void Save(UpdateSettings value)
    {
        _store.Save(value);
    }
}
