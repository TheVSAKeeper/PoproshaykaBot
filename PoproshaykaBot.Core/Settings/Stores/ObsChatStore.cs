using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

public sealed class ObsChatStore
{
    private static readonly JsonDocumentOptions ParseDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IEventBus _eventBus;
    private readonly JsonStore<ObsChatSettings> _store;

    public ObsChatStore(IEventBus eventBus, ILogger<ObsChatStore>? logger = null, string? filePath = null)
    {
        ArgumentNullException.ThrowIfNull(eventBus);

        var path = filePath ?? AppPaths.SettingsFile(SettingsFileNames.ObsChatFileName);

        _eventBus = eventBus;
        _store = new(path, logger, parser: ParseFile, describe: Describe);

        logger?.LogDebug("ObsChatStore инициализирован из {FilePath}", path);
    }

    public static string Describe(ObsChatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return $"сообщений на экране {settings.MaxMessages}, время {SettingsDescriber.OnOff(settings.ShowTimestamp)}, "
            + $"аватары {SettingsDescriber.OnOff(settings.ShowUserAvatars)}, анимации {SettingsDescriber.OnOff(settings.EnableAnimations)}, "
            + $"затухание {SettingsDescriber.OnOff(settings.EnableMessageFadeOut)} через {settings.MessageLifetimeSeconds} с, "
            + $"кегль {settings.FontSize}, автопрокрутка {SettingsDescriber.OnOff(settings.AutoScrollEnabled)}, "
            + $"картинки из сообщений {SettingsDescriber.OnOff(settings.ShowMessageImages)} (роли {settings.MessageImageRoles}, "
            + $"хостов {settings.MessageImageAllowedHosts?.Count ?? 0}, высота {settings.MessageImageMaxHeightPixels} px)";
    }

    public ObsChatSettings Load()
    {
        return _store.Load();
    }

    public void Save(ObsChatSettings value)
    {
        var committed = JsonStoreClone.DeepClone(value);
        _store.Save(committed);

        _ = _eventBus.PublishAsync(new ChatSettingsChangedEvent(committed));
    }

    private static ObsChatSettings? ParseFile(string json)
    {
        var parsed = JsonSerializer.Deserialize<ObsChatSettings>(json, JsonStoreOptions.Default);

        if (parsed == null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json, ParseDocumentOptions);

        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            parsed.PlatformBadgeMode = ObsPlatformBadgeMode.Normalize(parsed.PlatformBadgeMode);
            return parsed;
        }

        var hasMode = TryGetPropertyIgnoreCase(document.RootElement, "platformBadgeMode", out var modeElement)
            && modeElement.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(modeElement.GetString());

        if (hasMode)
        {
            parsed.PlatformBadgeMode = ObsPlatformBadgeMode.Normalize(parsed.PlatformBadgeMode);
        }
        else if (TryGetPropertyIgnoreCase(document.RootElement, "showPlatformBadge", out var legacyElement)
            && legacyElement.ValueKind == JsonValueKind.True)
        {
            parsed.PlatformBadgeMode = ObsPlatformBadgeMode.Always;
        }
        else
        {
            parsed.PlatformBadgeMode = ObsPlatformBadgeMode.Auto;
        }

        return parsed;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
