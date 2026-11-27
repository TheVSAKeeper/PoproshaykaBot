using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

internal sealed record PlatformPagesExtraction(
    IReadOnlyList<SettingsSectionPresentation> SettingsPresentations,
    IReadOnlyList<SettingsSectionPresentation> PlatformPresentations);

internal static class PlatformSettingsPages
{
    public const string ChannelCatalogKey = "channels/catalog";

    internal const string MultistreamKey = "multistream";
    private const string LegacySectionKey = "platforms";
    private const string TwitchSearch = "twitch твич helix eventsub чат отображение история недавняя recent-messages";
    private const string YouTubeSearch = "youtube ютуб google cloud квота testing production desktop";
    private const string MultistreamSearch = "мультистрим профиль профили вещание трансляция автозапуск группы рейтинг рассылка остановка";

    public static PlatformPagesExtraction Create(
        IReadOnlyList<SettingsSectionPresentation> presentations,
        BroadcastProfilesSettingsSectionViewModel? profiles,
        IChatPlatformRegistry? registry)
    {
        var allBlocks = presentations.SelectMany(page => page.Blocks).ToArray();
        var byBlock = allBlocks.ToLookup(block => block.Key, StringComparer.Ordinal);
        var byPlatform = allBlocks.Where(block => block.Platform.HasValue).ToLookup(block => block.Platform.GetValueOrDefault());
        var platformNames = registry is null
            ? byPlatform.ToDictionary(group => group.Key, group => group.Key.ToString())
            : registry.All.ToDictionary(platform => platform.Id, platform => platform.DisplayName);

        var groups = new List<(SettingsSubsection Tab, IReadOnlyList<SettingsSectionBlockDescriptor> Blocks)>();
        foreach (var (platform, name) in platformNames)
        {
            var key = platform.ToString().ToLowerInvariant();
            AddGroup(key, name, [.. byBlock[ChannelCatalogKey], .. byPlatform[platform]]);
        }

        List<SettingsSectionBlockDescriptor> multistreamBlocks = [.. byBlock["stream/auto"], .. byBlock["stream/lifecycle"], .. byBlock["channels/groups"]];
        if (profiles is not null)
        {
            multistreamBlocks.Insert(0, new(
                "platforms/profiles", "Профили трансляции",
                "Выберите профиль, чтобы изменить его настройки и целевые площадки.", profiles));
        }

        if (multistreamBlocks.Count > 0)
        {
            AddGroup(MultistreamKey, "Профили и автоматика", multistreamBlocks);
        }

        if (groups.Count == 0)
        {
            return new(presentations, []);
        }

        var section = new SettingsSection(
            LegacySectionKey, "Каналы и профили", PackIconLucideKind.Layers,
            $"стрим платформа канал каталог мониторинг переключение oauth авторизация аккаунт бот стример client id secret {TwitchSearch} {YouTubeSearch} {MultistreamSearch} {string.Join(' ', platformNames.Values)}");

        var result = new List<SettingsSectionPresentation>();
        foreach (var page in presentations)
        {
            switch (page.Section.Key)
            {
                case "oauth" or "youtube" or "channels":
                    break;
                case "stream":
                    result.Add(page with { Blocks = [.. page.Blocks.Where(block => block.Key == "stream/polls")] });
                    break;
                default:
                    var sharedBlocks = page.Blocks.Where(block => block.Platform is null).ToArray();
                    if (sharedBlocks.Length > 0)
                    {
                        result.Add(page with { Blocks = sharedBlocks });
                    }
                    break;
            }
        }

        IReadOnlyList<SettingsSectionPresentation> platformPresentations =
            [.. groups.Select(group => new SettingsSectionPresentation(section, group.Blocks, false, group.Tab))];
        return new(result, platformPresentations);

        void AddGroup(string key, string title, IReadOnlyList<SettingsSectionBlockDescriptor> blocks)
        {
            if (blocks.Count > 0)
            {
                groups.Add((new(key, title), blocks));
            }
        }
    }

    public static StreamingPlatform? GetPlatform(SettingsSubsection? group) =>
        Enum.TryParse<StreamingPlatform>(group?.Key, ignoreCase: true, out var platform) ? platform : null;

    public static string RestorePath(string path) => path.Split('/', 2)[0] switch
    {
        LegacySectionKey or "oauth" or "youtube" or "channels" => "basic",
        "basic" when path == "basic/channel" => "basic",
        "stream" when path == "stream/auto" || path.StartsWith("stream/lifecycle", StringComparison.Ordinal) => "basic",
        _ => path,
    };
}
