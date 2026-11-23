using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed record PlatformLinkTarget(string Key, string Title, StreamingPlatform? Platform);

internal sealed record PlatformPagesExtraction(
    IReadOnlyList<SettingsSectionPresentation> SettingsPresentations,
    IReadOnlyList<SettingsSectionPresentation> PlatformPresentations,
    IReadOnlyList<PlatformLinkTarget> LinkTargets);

internal static class PlatformSettingsPages
{
    public const string SectionKey = "platforms";
    public const string SectionTitle = "Площадки";
    public const string CardTitle = "Площадки и мультистрим";
    public const string ChannelCatalogKey = "channels/catalog";

    internal const string MultistreamKey = "multistream";
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
        var linkTargets = new List<PlatformLinkTarget>();
        foreach (var (platform, name) in platformNames)
        {
            var key = platform.ToString().ToLowerInvariant();
            AddGroup(key, name, [.. byBlock[ChannelCatalogKey], .. byPlatform[platform]]);
            linkTargets.Add(new(key, name, platform));
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
            linkTargets.Add(new(MultistreamKey, "Профили и автоматика", null));
        }

        if (groups.Count == 0)
        {
            return new(presentations, [], []);
        }

        var section = new SettingsSection(
            SectionKey, SectionTitle, PackIconLucideKind.Layers,
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

        result.Insert(result.FirstOrDefault()?.Section.Key == "basic" ? 1 : 0,
            new SettingsSectionPresentation(section, [], false));
        IReadOnlyList<SettingsSectionPresentation> platformPresentations =
            [.. groups.Select(group => new SettingsSectionPresentation(section, group.Blocks, false, group.Tab))];
        return new(result, platformPresentations, linkTargets);

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
        SectionKey or "oauth" or "youtube" or "channels" => SectionKey,
        "basic" when path == "basic/channel" => SectionKey,
        "stream" when path is "stream/auto" or "stream/lifecycle" => SectionKey,
        _ => path,
    };
}
