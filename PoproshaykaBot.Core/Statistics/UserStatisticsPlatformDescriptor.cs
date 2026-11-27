using System.Collections.Frozen;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Statistics;

public sealed record UserStatisticsPlatformDescriptor(
    StreamingPlatform Platform,
    string RelativePath,
    string EntityName);

public static class UserStatisticsPlatformCatalog
{
    public static IReadOnlyList<UserStatisticsPlatformDescriptor> All { get; } =
    [
        new(
            StreamingPlatform.Twitch,
            SettingsFileNames.UserStatisticsFileName,
            "пользователей"),
        new(
            StreamingPlatform.YouTube,
            $"{SettingsFileNames.YouTubeFolderName}/{SettingsFileNames.UserStatisticsFileName}",
            "пользователей YouTube"),
    ];

    private static readonly FrozenDictionary<StreamingPlatform, UserStatisticsPlatformDescriptor> ByPlatform =
        All.ToFrozenDictionary(static descriptor => descriptor.Platform);

    public static UserStatisticsPlatformDescriptor Get(StreamingPlatform platform)
    {
        return ByPlatform.TryGetValue(platform, out var descriptor)
            ? descriptor
            : throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
    }
}
