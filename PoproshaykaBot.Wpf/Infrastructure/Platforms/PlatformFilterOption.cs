using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.Infrastructure.Platforms;

public sealed record PlatformFilterOption(StreamingPlatform? Platform, string Label)
{
    public static PlatformFilterOption All { get; } = new(null, "Все");

    public static IReadOnlyList<PlatformFilterOption> AllOptions { get; } =
    [
        All,
        .. PlatformVisuals.OrderedPlatforms.Select(static platform =>
            new PlatformFilterOption(platform, PlatformVisuals.GetDisplayName(platform))),
    ];

    public static IReadOnlyList<PlatformFilterOption> ForRegistry(
        IChatPlatformRegistry? registry,
        PlatformFilterOption? firstOption = null)
    {
        var head = firstOption ?? All;
        if (registry is null)
        {
            return firstOption is null
                ? AllOptions
                : [head, .. AllOptions.Skip(1)];
        }

        return [head, .. registry.All.Select(static platform => new PlatformFilterOption(platform.Id, platform.DisplayName))];
    }
}
