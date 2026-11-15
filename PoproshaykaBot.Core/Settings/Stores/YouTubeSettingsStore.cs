using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.YouTube;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class YouTubeSettingsStore
{
    private readonly JsonStore<YouTubeSettings> _store;

    public YouTubeSettingsStore(
        ILogger<YouTubeSettingsStore>? logger = null,
        string? filePath = null,
        SettingsWriteGate? gate = null)
    {
        var path = filePath ?? AppPaths.SettingsFile(LegacyDataCatalog.YouTubeSettingsFileName);
        _store = new(path, logger, describe: Describe, gate: gate);

        if (logger?.IsEnabled(LogLevel.Debug) == true)
        {
            logger.LogDebug(
                "YouTubeSettingsStore инициализирован из {FilePath}: {Description}",
                path,
                Describe(_store.Load()));
        }
    }

    public static string Describe(YouTubeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return $"включено {SettingsDescriber.OnOff(settings.Enabled)}, projectId {SettingsDescriber.Value(settings.ProjectId)}, "
            + $"clientId {SettingsDescriber.Present(settings.ClientId)}, канал {SettingsDescriber.Value(settings.Channel)}, "
            + $"бюджет квоты {settings.DailyQuotaBudget}";
    }

    public YouTubeSettings Load()
    {
        return _store.Load();
    }

    public bool Save(YouTubeSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return _store.Save(value);
    }

    public bool Mutate(Action<YouTubeSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        return _store.Mutate(mutator);
    }
}
