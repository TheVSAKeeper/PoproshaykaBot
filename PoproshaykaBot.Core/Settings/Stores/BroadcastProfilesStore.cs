using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure;

namespace PoproshaykaBot.Core.Settings.Stores;

public class BroadcastProfilesStore
{
    private readonly JsonStore<BroadcastProfilesSettings> _store;

    public BroadcastProfilesStore(ILogger<BroadcastProfilesStore>? logger = null, string? filePath = null)
    {
        var path = filePath ?? AppPaths.SettingsFile("broadcast-profiles.json");
        _store = new(path, logger, describe: SettingsDescriber.Describe);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug("BroadcastProfilesStore инициализирован из {FilePath} (профилей: {ProfileCount})",
                path,
                _store.Load().Profiles.Count);
        }
    }

    public virtual BroadcastProfilesSettings Load()
    {
        return _store.Load();
    }

    public virtual void Mutate(Action<BroadcastProfilesSettings> mutator)
    {
        _store.Mutate(mutator);
    }

    public virtual TResult Mutate<TResult>(Func<BroadcastProfilesSettings, TResult> mutator)
    {
        return _store.Mutate(mutator);
    }

    public virtual void Save(BroadcastProfilesSettings value)
    {
        _store.Save(value);
    }
}
