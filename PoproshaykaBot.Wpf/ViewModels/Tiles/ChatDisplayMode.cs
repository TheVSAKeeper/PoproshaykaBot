using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public enum ChatDisplayLayout
{
    Single = 0,
    Columns = 1,
    Rows = 2,
}

public sealed record ChatDisplayMode(StreamingPlatform Platform, ChatDisplayLayout Layout, string Label)
{
    public string SettingsValue => $"{Platform}/{Layout}";
    public bool IsSplit => Layout != ChatDisplayLayout.Single;
}
