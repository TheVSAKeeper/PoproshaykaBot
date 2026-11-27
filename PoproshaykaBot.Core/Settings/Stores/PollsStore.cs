using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Polls;

namespace PoproshaykaBot.Core.Settings.Stores;

public class PollsStore
{
    private readonly JsonStore<PollsSettings> _store;

    public PollsStore(ILogger<PollsStore>? logger = null, string? filePath = null)
    {
        var path = filePath ?? AppPaths.SettingsFile(SettingsFileNames.PollsFileName);
        _store = new(path, logger, describe: Describe);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug("PollsStore инициализирован из {FilePath} (профилей: {ProfileCount})",
                path,
                _store.Load().Profiles.Count);
        }
    }

    public static string Describe(PollsSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var templates = settings.ChatTemplates;

        return $"профилей опросов {settings.Profiles.Count}, объявление старта {SettingsDescriber.OnOff(templates.StartEnabled)}, "
            + $"прогресс {SettingsDescriber.OnOff(templates.ProgressEnabled)} каждые {templates.ProgressAnnounceIntervalSeconds} с, "
            + $"завершение {SettingsDescriber.OnOff(templates.EndEnabled)}, прерывание {SettingsDescriber.OnOff(templates.TerminatedEnabled)}, "
            + $"архив {SettingsDescriber.OnOff(templates.ArchivedEnabled)}, стоп-дата автозапуска {SettingsDescriber.Moment(settings.AutoTriggerKillSwitchDateUtc)}, "
            + $"история до {settings.HistoryMaxItems}";
    }

    public virtual PollsSettings Load()
    {
        return _store.Load();
    }

    public virtual bool Mutate(Action<PollsSettings> mutator)
    {
        return _store.Mutate(mutator);
    }

    public virtual bool Save(PollsSettings value)
    {
        return _store.Save(value);
    }
}
