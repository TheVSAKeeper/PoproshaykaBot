using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Infrastructure;

namespace PoproshaykaBot.Core.Settings.Stores;

public class BroadcastProfileYouTubeStore
{
    private readonly JsonStore<BroadcastProfileYouTubeSettings> _store;

    public BroadcastProfileYouTubeStore(
        ILogger<BroadcastProfileYouTubeStore>? logger = null,
        string? filePath = null)
    {
        var path = filePath ?? AppPaths.SettingsFile(SettingsFileNames.BroadcastProfileYouTubeFileName);
        _store = new(path, logger, describe: Describe);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug(
                "BroadcastProfileYouTubeStore инициализирован из {FilePath} (профилей: {ProfileCount})",
                path,
                _store.Load().Profiles.Count);
        }
    }

    public static string Describe(BroadcastProfileYouTubeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return $"записей={settings.Profiles.Count}";
    }

    public virtual BroadcastProfileYouTubeSettings Load()
    {
        return _store.Load();
    }

    public virtual bool Save(BroadcastProfileYouTubeSettings value)
    {
        return _store.Save(value);
    }

    public virtual bool Mutate(Action<BroadcastProfileYouTubeSettings> mutator)
    {
        return _store.Mutate(mutator);
    }

    public virtual BroadcastProfileYouTubeMetadata? GetMetadata(Guid profileId)
    {
        var settings = _store.Load();

        if (!settings.Profiles.TryGetValue(profileId, out var metadata) || metadata is null)
        {
            return null;
        }

        var normalized = metadata.Normalize();
        return normalized.IsEmpty ? null : normalized;
    }

    public virtual bool SetMetadata(Guid profileId, BroadcastProfileYouTubeMetadata? metadata)
    {
        var normalized = metadata?.Normalize();

        return _store.Mutate(settings =>
        {
            if (normalized is null || normalized.IsEmpty)
            {
                settings.Profiles.Remove(profileId);
            }
            else
            {
                settings.Profiles[profileId] = normalized;
            }
        });
    }

    public virtual bool RemoveProfile(Guid profileId)
    {
        return _store.Mutate(settings => settings.Profiles.Remove(profileId));
    }
}
