using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;

namespace PoproshaykaBot.Core.Settings.Stores;

public class BroadcastProfilePlatformsStore
{
    private readonly JsonStore<BroadcastProfilePlatformsSettings> _store;

    public BroadcastProfilePlatformsStore(
        ILogger<BroadcastProfilePlatformsStore>? logger = null,
        string? filePath = null,
        SettingsWriteGate? gate = null)
    {
        var path = filePath ?? AppPaths.SettingsFile(LegacyDataCatalog.BroadcastProfilePlatformsFileName);
        _store = new(path, logger, describe: SettingsDescriber.Describe, gate: gate);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug(
                "BroadcastProfilePlatformsStore инициализирован из {FilePath} (профилей: {ProfileCount})",
                path,
                _store.Load().Profiles.Count);
        }
    }

    public virtual BroadcastProfilePlatformsSettings Load()
    {
        return _store.Load();
    }

    public virtual bool Save(BroadcastProfilePlatformsSettings value)
    {
        return _store.Save(value);
    }

    public virtual bool Mutate(Action<BroadcastProfilePlatformsSettings> mutator)
    {
        return _store.Mutate(mutator);
    }

    public virtual IReadOnlySet<StreamingPlatform> GetPlatformsOrDefault(Guid profileId)
    {
        var settings = _store.Load();

        if (!settings.Profiles.TryGetValue(profileId, out var platforms))
        {
            return new HashSet<StreamingPlatform> { StreamingPlatform.Twitch };
        }

        return platforms
            .Where(Enum.IsDefined)
            .ToHashSet();
    }

    public virtual bool SetPlatforms(Guid profileId, IReadOnlyCollection<StreamingPlatform> platforms)
    {
        ArgumentNullException.ThrowIfNull(platforms);

        var normalized = platforms
            .Where(Enum.IsDefined)
            .Distinct()
            .ToList();

        return _store.Mutate(settings => { settings.Profiles[profileId] = normalized; });
    }

    public virtual bool RemoveProfile(Guid profileId)
    {
        return _store.Mutate(settings => settings.Profiles.Remove(profileId));
    }
}
