using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public enum ChatDisplayLayout
{
    Single = 0,
    Columns = 1,
    Rows = 2,
}

public sealed record ChatDisplayMode(
    StreamingPlatform Platform,
    ChatDisplayLayout Layout,
    string Label,
    StreamingPlatform? PrimaryPlatform = null)
{
    public StreamingPlatform EffectivePrimaryPlatform => PrimaryPlatform ?? Platform;
    public string SettingsValue => IsSplit && PrimaryPlatform is { } primary && primary != StreamingPlatform.Twitch
        ? $"{primary}+{Platform}/{Layout}"
        : $"{Platform}/{Layout}";
    public bool IsSplit => Layout != ChatDisplayLayout.Single;
}
