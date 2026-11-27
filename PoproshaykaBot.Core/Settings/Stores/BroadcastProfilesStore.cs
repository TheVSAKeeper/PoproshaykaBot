using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure;

namespace PoproshaykaBot.Core.Settings.Stores;

public class BroadcastProfilesStore
{
    private readonly JsonStore<BroadcastProfilesSettings> _store;

    public BroadcastProfilesStore(ILogger<BroadcastProfilesStore>? logger = null, string? filePath = null)
    {
        var path = filePath ?? AppPaths.SettingsFile(SettingsFileNames.BroadcastProfilesFileName);
        _store = new(path, logger, describe: Describe);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug("BroadcastProfilesStore инициализирован из {FilePath} (профилей: {ProfileCount})",
                path,
                _store.Load().Profiles.Count);
        }
    }

    public static string Describe(BroadcastProfilesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var lastApplied = settings.LastAppliedProfileId is { } id ? id.ToString() : SettingsDescriber.Unset;

        return $"профилей {settings.Profiles.Count}, последний применённый {lastApplied}";
    }

    public virtual BroadcastProfilesSettings Load()
    {
        return _store.Load();
    }

    public virtual bool Mutate(Action<BroadcastProfilesSettings> mutator)
    {
        return _store.Mutate(mutator);
    }

    public virtual bool Save(BroadcastProfilesSettings value)
    {
        return _store.Save(value);
    }
}
