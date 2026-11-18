using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

internal static class PlatformSettingsPages
{
    public const string SectionKey = "platforms";
    public const string ChannelCatalogKey = "channels/catalog";

    private const string TwitchKey = "twitch";
    private const string YouTubeKey = "youtube";
    private const string MultistreamKey = "multistream";
    private const string TwitchSearch = "twitch твич helix eventsub чат отображение";
    private const string YouTubeSearch = "youtube ютуб google cloud квота testing production desktop";
    private const string MultistreamSearch = "мультистрим профиль профили вещание трансляция автозапуск группы рейтинг рассылка остановка";

    public static IReadOnlyList<SettingsSectionPresentation> Create(
        IReadOnlyList<SettingsSectionPresentation> presentations,
        BroadcastProfilesSettingsSectionViewModel? profiles)
    {
        var byBlock = presentations.SelectMany(page => page.Blocks).ToLookup(block => block.Key, StringComparer.Ordinal);
        var groups = new List<(SettingsSubsection Tab, IReadOnlyList<SettingsSectionBlockDescriptor> Blocks)>();
        AddGroup(TwitchKey, "Twitch",
            [.. byBlock[ChannelCatalogKey], .. byBlock["oauth/app"], .. byBlock["basic/channel"]]);
        AddGroup(YouTubeKey, "YouTube", [.. byBlock[ChannelCatalogKey], .. byBlock["youtube/project"]]);

        List<SettingsSectionBlockDescriptor> multistreamBlocks = [.. byBlock["stream/auto"], .. byBlock["stream/lifecycle"], .. byBlock["channels/groups"]];
        if (profiles is not null)
        {
            multistreamBlocks.Insert(0, new(
                "platforms/profiles", "Профили трансляции",
                "Выберите профиль, чтобы изменить его настройки и целевые площадки.", profiles));
        }

        AddGroup(MultistreamKey, "Мультистрим", multistreamBlocks);
        if (groups.Count == 0)
        {
            return presentations;
        }

        var section = new SettingsSection(
            SectionKey, "Площадки", PackIconLucideKind.Layers,
            $"стрим платформа канал каталог мониторинг переключение oauth авторизация аккаунт бот стример client id secret {TwitchSearch} {YouTubeSearch} {MultistreamSearch}",
            [.. groups.Select(group => group.Tab)]);

        var result = new List<SettingsSectionPresentation>();
        foreach (var page in presentations)
        {
            switch (page.Section.Key)
            {
                case "oauth" or "youtube" or "channels":
                    break;
                case "basic":
                    result.Add(page with { Blocks = [.. page.Blocks.Where(block => block.Key != "basic/channel")] });
                    break;
                case "stream":
                    result.Add(page with { Blocks = [.. page.Blocks.Where(block => block.Key == "stream/polls")] });
                    break;
                default:
                    result.Add(page);
                    break;
            }
        }

        result.InsertRange(result.FirstOrDefault()?.Section.Key == "basic" ? 1 : 0,
            groups.Select(group => new SettingsSectionPresentation(section, group.Blocks, false, group.Tab)));
        return result;

        void AddGroup(string key, string title, IReadOnlyList<SettingsSectionBlockDescriptor> blocks)
        {
            if (blocks.Count > 0)
            {
                groups.Add((new(key, title), blocks));
            }
        }
    }

    public static StreamingPlatform? GetPlatform(SettingsSubsection? group) => group?.Key switch
    {
        TwitchKey => StreamingPlatform.Twitch,
        YouTubeKey => StreamingPlatform.YouTube,
        _ => null,
    };

    public static string? FindSearchGroup(string query)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = new[] { (TwitchKey, TwitchSearch), (YouTubeKey, YouTubeSearch), (MultistreamKey, MultistreamSearch) }
            .Where(group => terms.Any(term => group.Item2.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        return matches.Length == 1 ? matches[0].Item1 : null;
    }

    public static string RestorePath(string path) => path.Split('/', 2)[0] switch
    {
        "oauth" => $"{SectionKey}/{TwitchKey}",
        "youtube" => $"{SectionKey}/{YouTubeKey}",
        "channels" when path == "channels/groups" => $"{SectionKey}/{MultistreamKey}",
        "channels" => $"{SectionKey}/{TwitchKey}",
        "basic" when path == "basic/channel" => $"{SectionKey}/{TwitchKey}",
        "stream" when path is "stream/auto" or "stream/lifecycle" => $"{SectionKey}/{MultistreamKey}",
        _ => path,
    };
}
