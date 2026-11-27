using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.YouTube;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class YouTubeSettingsStore : IPlatformEnablementSource
{
    private readonly JsonStore<YouTubeSettings> _store;

    public YouTubeSettingsStore(
        ILogger<YouTubeSettingsStore>? logger = null,
        string? filePath = null)
    {
        var path = filePath ?? AppPaths.SettingsFile(SettingsFileNames.YouTubeSettingsFileName);
        _store = new(path, logger, describe: Describe);

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

    public event Action? Changed;

    StreamingPlatform IPlatformEnablementSource.Platform => StreamingPlatform.YouTube;

    bool IPlatformEnablementSource.IsEnabledInSettings => Load().Enabled;

    public YouTubeSettings Load()
    {
        return _store.Load();
    }

    public bool Save(YouTubeSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var saved = _store.Save(value);

        if (saved)
        {
            Changed?.Invoke();
        }

        return saved;
    }

    public (bool Matched, bool Written) TryReplace(YouTubeSettings expected, YouTubeSettings replacement)
    {
        var matched = false;
        var written = _store.Save(replacement,
            matches: current => matched = JsonStoreClone.SameValues(current, expected));
        if (written)
        {
            Changed?.Invoke();
        }

        return (matched, written);
    }

    public bool Mutate(Action<YouTubeSettings> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        var mutated = _store.Mutate(mutator);

        if (mutated)
        {
            Changed?.Invoke();
        }

        return mutated;
    }
}
