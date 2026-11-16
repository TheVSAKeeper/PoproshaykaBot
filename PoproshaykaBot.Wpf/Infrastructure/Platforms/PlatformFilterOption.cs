using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.Infrastructure.Platforms;

public sealed record PlatformFilterOption(StreamingPlatform? Platform, string Label)
{
    public static PlatformFilterOption All { get; } = new(null, "Все");

    public static IReadOnlyList<PlatformFilterOption> AllOptions { get; } =
    [
        All,
        new(StreamingPlatform.Twitch, PlatformVisuals.GetDisplayName(StreamingPlatform.Twitch)),
        new(StreamingPlatform.YouTube, PlatformVisuals.GetDisplayName(StreamingPlatform.YouTube)),
    ];
}
