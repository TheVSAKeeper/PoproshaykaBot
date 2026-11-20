using PoproshaykaBot.Core.Platforms;
using System.Collections.Frozen;
using System.Windows;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Infrastructure.Platforms;

public sealed record PlatformDescriptor(
    StreamingPlatform Platform,
    string DisplayName,
    string ShortTag,
    string GeometryResourceKey,
    string BrandBrushResourceKey,
    string FallbackPathData,
    Color FallbackBrandColor,
    string OpenStreamTooltip);

public static class PlatformVisuals
{
    private static readonly PlatformDescriptor TwitchDescriptor = new(
        StreamingPlatform.Twitch,
        "Twitch",
        "TW",
        "Icon.Platform.Twitch",
        "Brush.Platform.Twitch",
        "M4.3,3 H20 V14.7 L15.3,19.4 H11.4 L8.9,22 H6.5 V19.4 H3 V6.2 Z M6,5 V16.6 H9.5 V18.9 L11.8,16.6 H16 L18.3,14.3 V5 Z M14.8,8.2 H16.7 V13.4 H14.8 Z M9.9,8.2 H11.8 V13.4 H9.9 Z",
        Color.FromRgb(0x91, 0x46, 0xFF),
        "Открыть канал на Twitch");

    private static readonly PlatformDescriptor YouTubeDescriptor = new(
        StreamingPlatform.YouTube,
        "YouTube",
        "YT",
        "Icon.Platform.YouTube",
        "Brush.Platform.YouTube",
        "M21.6,7.2 C21.4,6.3 20.6,5.5 19.7,5.3 C18,4.8 12,4.8 12,4.8 C12,4.8 6,4.8 4.3,5.3 C3.4,5.5 2.6,6.3 2.4,7.2 C2,8.9 2,12 2,12 C2,12 2,15.1 2.4,16.8 C2.6,17.7 3.4,18.5 4.3,18.7 C6,19.2 12,19.2 12,19.2 C12,19.2 18,19.2 19.7,18.7 C20.6,18.5 21.4,17.7 21.6,16.8 C22,15.1 22,12 22,12 C22,12 22,8.9 21.6,7.2 Z M10,15.1 V8.9 L15.4,12 Z",
        Color.FromRgb(0xFF, 0x00, 0x00),
        "Открыть канал или эфир на YouTube");

    private static readonly PlatformDescriptor DefaultDescriptor = new(
        default,
        "Площадка",
        "",
        "Icon.Platform.Default",
        "Brush.Platform.Default",
        "M2,5 H22 V19 H2 Z M4,7 V17 H20 V7 Z M10,9 L15,12 L10,15 Z",
        Color.FromRgb(0x80, 0x87, 0x8E),
        "Открыть канал или эфир");

    private static readonly Geometry DefaultGeometry = CreateFrozenGeometry(DefaultDescriptor.FallbackPathData);
    private static readonly Brush DefaultBrush = CreateFrozenBrush(DefaultDescriptor.FallbackBrandColor);

    private static readonly FrozenDictionary<StreamingPlatform, PlatformDescriptor> Descriptors =
        new Dictionary<StreamingPlatform, PlatformDescriptor>
        {
            [StreamingPlatform.Twitch] = TwitchDescriptor,
            [StreamingPlatform.YouTube] = YouTubeDescriptor,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<StreamingPlatform, Geometry> FallbackGeometries =
        Descriptors.ToFrozenDictionary(
            static pair => pair.Key,
            static pair => CreateFrozenGeometry(pair.Value.FallbackPathData));

    private static readonly FrozenDictionary<StreamingPlatform, Brush> FallbackBrushes =
        Descriptors.ToFrozenDictionary(
            static pair => pair.Key,
            static pair => CreateFrozenBrush(pair.Value.FallbackBrandColor));

    public static IReadOnlyList<StreamingPlatform> OrderedPlatforms { get; } =
    [
        StreamingPlatform.Twitch,
        StreamingPlatform.YouTube,
    ];

    public static PlatformDescriptor Get(StreamingPlatform platform)
    {
        return Descriptors.TryGetValue(platform, out var descriptor)
            ? descriptor
            : DefaultDescriptor with { Platform = platform };
    }

    public static string GetDisplayName(StreamingPlatform platform, IChatPlatformRegistry? registry = null)
    {
        return registry?.Find(platform)?.DisplayName
            ?? Descriptors.GetValueOrDefault(platform, DefaultDescriptor).DisplayName;
    }

    public static Geometry ResolveGeometry(StreamingPlatform platform, FrameworkElement? host = null)
    {
        var descriptor = Get(platform);

        if (host?.TryFindResource(descriptor.GeometryResourceKey) is Geometry fromHost)
        {
            return fromHost;
        }

        if (Application.Current?.TryFindResource(descriptor.GeometryResourceKey) is Geometry fromApp)
        {
            return fromApp;
        }

        return FallbackGeometries.GetValueOrDefault(platform) ?? DefaultGeometry;
    }

    public static Brush ResolveBrandBrush(StreamingPlatform platform, FrameworkElement? host = null)
    {
        var descriptor = Get(platform);

        if (host?.TryFindResource(descriptor.BrandBrushResourceKey) is Brush fromHost)
        {
            return fromHost;
        }

        if (Application.Current?.TryFindResource(descriptor.BrandBrushResourceKey) is Brush fromApp)
        {
            return fromApp;
        }

        return FallbackBrushes.GetValueOrDefault(platform) ?? DefaultBrush;
    }

    private static Geometry CreateFrozenGeometry(string pathData)
    {
        var parsed = Geometry.Parse(pathData);
        if (parsed.CanFreeze)
        {
            parsed.Freeze();
        }

        return parsed;
    }

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }
}
