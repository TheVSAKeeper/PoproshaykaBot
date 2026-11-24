using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class PlatformLifecycleSettingsFile
{
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<StreamingPlatform, BotLifecycleAutomationSettings> Platforms { get; init; } = [];
}

public sealed class PlatformLifecycleSettingsStore
{
    private readonly SettingsManager? _settingsManager;
    private readonly JsonStore<PlatformLifecycleSettingsFile> _store;
    private readonly Lock _overrideLock = new();
    private bool _hasExplicitValue;

    public PlatformLifecycleSettingsStore(
        SettingsManager? settingsManager = null,
        ILogger<PlatformLifecycleSettingsStore>? logger = null,
        string? filePath = null,
        SettingsWriteGate? gate = null)
    {
        var path = filePath ?? AppPaths.SettingsFile("platform-lifecycle.json");

        _settingsManager = settingsManager;
        _hasExplicitValue = File.Exists(path);
        _store = new(path, logger, describe: Describe, gate: gate);

        logger?.LogDebug("PlatformLifecycleSettingsStore инициализирован из {FilePath}", path);
    }

    public static string Describe(PlatformLifecycleSettingsFile settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var parts = settings.Platforms
            .Where(static pair => Enum.IsDefined(pair.Key) && pair.Value is not null)
            .OrderBy(static pair => pair.Key)
            .Select(static pair =>
                $"{pair.Key}: автоподключение {SettingsDescriber.OnOff(pair.Value.AutoConnectOnStreamOnline)}, "
                + $"автоотключение {SettingsDescriber.OnOff(pair.Value.AutoDisconnectOnStreamOffline)}")
            .ToArray();

        return parts.Length == 0 ? "нет настроенных площадок" : string.Join("; ", parts);
    }

    public PlatformLifecycleSettingsFile Load()
    {
        lock (_overrideLock)
        {
            var loaded = Normalize(_store.Load());
            SeedLegacyTwitchLocked(loaded);
            return loaded;
        }
    }

    public BotLifecycleAutomationSettings Get(StreamingPlatform platform)
    {
        var loaded = Load();
        return loaded.Platforms.TryGetValue(platform, out var settings)
            ? settings
            : new BotLifecycleAutomationSettings();
    }

    public bool Save(PlatformLifecycleSettingsFile value)
    {
        ArgumentNullException.ThrowIfNull(value);

        lock (_overrideLock)
        {
            var snapshot = Normalize(value);
            var written = _store.Save(snapshot);
            _hasExplicitValue = true;
            return written;
        }
    }

    public bool Set(StreamingPlatform platform, BotLifecycleAutomationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return Mutate(state =>
        {
            if (Enum.IsDefined(platform))
            {
                state.Platforms[platform] = CloneSettings(settings);
            }
        });
    }

    public bool Mutate(Action<PlatformLifecycleSettingsFile> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        lock (_overrideLock)
        {
            var written = _store.Mutate(state =>
            {
                SeedLegacyTwitchLocked(state);
                mutator(state);
                var normalized = Normalize(state);
                state.Platforms.Clear();
                foreach (var pair in normalized.Platforms)
                {
                    state.Platforms[pair.Key] = pair.Value;
                }
            });

            _hasExplicitValue = true;
            return written;
        }
    }

    private void SeedLegacyTwitchLocked(PlatformLifecycleSettingsFile state)
    {
        if (!_hasExplicitValue && _settingsManager is not null && !state.Platforms.ContainsKey(StreamingPlatform.Twitch))
        {
            state.Platforms[StreamingPlatform.Twitch] = CloneSettings(_settingsManager.Current.Twitch.BotLifecycleAutomation);
        }
    }

    private static PlatformLifecycleSettingsFile Normalize(PlatformLifecycleSettingsFile source)
    {
        var normalized = new PlatformLifecycleSettingsFile();
        foreach (var pair in source.Platforms)
        {
            if (!Enum.IsDefined(pair.Key) || pair.Value is null)
            {
                continue;
            }

            normalized.Platforms[pair.Key] = CloneSettings(pair.Value);
        }

        return normalized;
    }

    private static BotLifecycleAutomationSettings CloneSettings(BotLifecycleAutomationSettings settings)
    {
        return new()
        {
            AutoConnectOnStreamOnline = settings.AutoConnectOnStreamOnline,
            AutoDisconnectOnStreamOffline = settings.AutoDisconnectOnStreamOffline,
        };
    }
}
