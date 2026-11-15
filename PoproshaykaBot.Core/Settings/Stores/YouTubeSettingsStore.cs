using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.YouTube;

namespace PoproshaykaBot.Core.Settings.Stores;

public class YouTubeSettingsStore
{
    private readonly JsonStore<YouTubeSettings> _store;

    public YouTubeSettingsStore(
        ILogger<YouTubeSettingsStore>? logger = null,
        string? filePath = null,
        SettingsWriteGate? gate = null)
    {
        var path = filePath ?? AppPaths.SettingsFile(LegacyDataCatalog.YouTubeSettingsFileName);
        _store = new(path, logger, describe: SettingsDescriber.Describe, gate: gate);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug(
                "YouTubeSettingsStore инициализирован из {FilePath}: {Description}",
                path,
                SettingsDescriber.Describe(_store.Load()));
        }
    }

    public virtual YouTubeSettings Load()
    {
        return _store.Load();
    }

    public virtual bool Save(YouTubeSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return _store.Save(value);
    }

    public virtual bool Mutate(Action<YouTubeSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        return _store.Mutate(mutator);
    }
}
