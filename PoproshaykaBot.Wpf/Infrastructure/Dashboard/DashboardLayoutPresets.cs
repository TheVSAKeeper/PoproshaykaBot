using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Ui;

namespace PoproshaykaBot.Wpf.Infrastructure.Dashboard;

public enum DashboardLayoutPreset
{
    Custom = 0,
    Compact = 1,
    Wide = 2,
}

public static class DashboardLayoutPresets
{
    public const string StreamChannelsTypeId = "stream-channels";
    public const string BroadcastProfilesTypeId = "broadcast-profiles";
    public const string ObsInfoTypeId = "obs-info";
    public const string TwitchChatTypeId = "twitch-chat";
    public const string StreamInfoTypeId = "stream-info";
    public const string BroadcastStatusTypeId = "broadcast-status";
    public const string PollsControlTypeId = "polls-control";
    public const string StreamPreviewTypeId = "stream-preview";
    public const string ChatOverlayPreviewTypeId = "chat-overlay-preview";
    public const string MultiplatformChatTypeId = "multiplatform-chat";
    public const string MultistreamTypeId = "multistream";

    public static string GetDisplayName(DashboardLayoutPreset preset) => preset switch
    {
        DashboardLayoutPreset.Compact => "Компактный (~970×1050)",
        DashboardLayoutPreset.Wide => "Широкий",
        _ => "Своя раскладка",
    };

    public static string GetShortName(DashboardLayoutPreset preset) => preset switch
    {
        DashboardLayoutPreset.Compact => "Компакт",
        DashboardLayoutPreset.Wide => "Широкий",
        _ => "Своя",
    };

    public static DashboardLayoutSettings Create(DashboardLayoutPreset preset) => preset switch
    {
        DashboardLayoutPreset.Wide => CreateWide(),
        _ => CreateCompact(),
    };

    public static DashboardLayoutSettings CreateCompact()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 4,
        };

        AddTile(layout, StreamChannelsTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1);
        AddTile(layout, BroadcastProfilesTypeId, row: 0, column: 1, columnSpan: 1, rowSpan: 1);
        AddTile(layout, ObsInfoTypeId, row: 1, column: 0, columnSpan: 2, rowSpan: 1);
        AddTile(layout, TwitchChatTypeId, row: 2, column: 0, columnSpan: 2, rowSpan: 2);

        AddTile(layout, BroadcastStatusTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, ChatOverlayPreviewTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, MultiplatformChatTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, StreamInfoTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, MultistreamTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, PollsControlTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, StreamPreviewTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);

        return layout;
    }

    public static DashboardLayoutSettings CreateWide()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 4,
            RowCount = 5,
        };

        AddTile(layout, StreamInfoTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1);
        AddTile(layout, BroadcastStatusTypeId, row: 1, column: 0, columnSpan: 1, rowSpan: 3);
        AddTile(layout, BroadcastProfilesTypeId, row: 4, column: 0, columnSpan: 1, rowSpan: 1);
        AddTile(layout, PollsControlTypeId, row: 3, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, ObsInfoTypeId, row: 0, column: 1, columnSpan: 1, rowSpan: 3);
        AddTile(layout, StreamPreviewTypeId, row: 0, column: 1, columnSpan: 1, rowSpan: 2, isVisible: false);
        AddTile(layout, ChatOverlayPreviewTypeId, row: 3, column: 1, columnSpan: 1, rowSpan: 2);
        AddTile(layout, TwitchChatTypeId, row: 0, column: 2, columnSpan: 1, rowSpan: 5);
        AddTile(layout, MultiplatformChatTypeId, row: 0, column: 3, columnSpan: 1, rowSpan: 3);
        AddTile(layout, MultistreamTypeId, row: 3, column: 3, columnSpan: 1, rowSpan: 2);
        AddTile(layout, StreamChannelsTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);

        return layout;
    }

    internal static DashboardLayoutSettings CreateCompactTrial()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 5,
        };

        AddTile(layout, StreamChannelsTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1);
        AddTile(layout, BroadcastProfilesTypeId, row: 0, column: 1, columnSpan: 1, rowSpan: 1);
        AddTile(layout, StreamInfoTypeId, row: 1, column: 0, columnSpan: 1, rowSpan: 1);
        AddTile(layout, MultistreamTypeId, row: 1, column: 1, columnSpan: 1, rowSpan: 1);
        AddTile(layout, ObsInfoTypeId, row: 2, column: 0, columnSpan: 2, rowSpan: 1);
        AddTile(layout, TwitchChatTypeId, row: 3, column: 0, columnSpan: 2, rowSpan: 2);

        AddTile(layout, BroadcastStatusTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, ChatOverlayPreviewTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, MultiplatformChatTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, PollsControlTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);
        AddTile(layout, StreamPreviewTypeId, row: 0, column: 0, columnSpan: 1, rowSpan: 1, isVisible: false);

        return layout;
    }

    public static DashboardLayoutPreset DetectPreset(DashboardLayoutSettings? layout, IReadOnlySet<string>? knownTypeIds = null)
    {
        if (layout is null || layout.Tiles.Count == 0)
        {
            return DashboardLayoutPreset.Compact;
        }

        if (MatchesPreset(layout, CreateCompact(), knownTypeIds))
        {
            return DashboardLayoutPreset.Compact;
        }

        if (MatchesPreset(layout, CreateWide(), knownTypeIds))
        {
            return DashboardLayoutPreset.Wide;
        }

        return DashboardLayoutPreset.Custom;
    }

    private static bool MatchesPreset(
        DashboardLayoutSettings layout,
        DashboardLayoutSettings template,
        IReadOnlySet<string>? knownTypeIds)
    {
        if (layout.ColumnCount != template.ColumnCount || layout.RowCount != template.RowCount)
        {
            return false;
        }

        var actualByTypeId = new Dictionary<string, DashboardTileSettings>(StringComparer.Ordinal);
        foreach (var tile in layout.Tiles)
        {
            if (!tile.IsVisible || (knownTypeIds is not null && !knownTypeIds.Contains(tile.TypeId)))
            {
                continue;
            }

            if (!actualByTypeId.TryAdd(tile.TypeId, tile))
            {
                return false;
            }
        }

        var expectedVisible = template.Tiles.Where(static tile => tile.IsVisible).ToArray();
        if (actualByTypeId.Count != expectedVisible.Length)
        {
            return false;
        }

        foreach (var expected in expectedVisible)
        {
            if (!actualByTypeId.TryGetValue(expected.TypeId, out var actual)
                || actual.Row != expected.Row
                || actual.Column != expected.Column
                || actual.ColumnSpan != expected.ColumnSpan
                || actual.RowSpan != expected.RowSpan)
            {
                return false;
            }
        }

        if (layout.Root is null)
        {
            return true;
        }

        var templateRoot = DashboardLayoutTree.TryBuild(template.Tiles, template.ColumnCount, template.RowCount);
        return templateRoot is not null && MatchesRootWeights(layout.Root, templateRoot);
    }

    private static bool MatchesRootWeights(DashboardPane actual, DashboardPane expected)
    {
        switch (actual, expected)
        {
            case (TilePane actualTile, TilePane expectedTile):
                return string.Equals(actualTile.TypeId, expectedTile.TypeId, StringComparison.Ordinal);

            case (SplitPane actualSplit, SplitPane expectedSplit):
                if (actualSplit.Orientation != expectedSplit.Orientation
                    || actualSplit.Children.Count != expectedSplit.Children.Count)
                {
                    return false;
                }

                for (var index = 0; index < actualSplit.Children.Count; index++)
                {
                    var actualChild = actualSplit.Children[index];
                    var expectedChild = expectedSplit.Children[index];
                    if (actualChild.Weight is { } actualWeight
                        && (expectedChild.Weight is not { } expectedWeight
                            || Math.Abs(actualWeight - expectedWeight) > 1e-4))
                    {
                        return false;
                    }

                    if (!MatchesRootWeights(actualChild.Pane, expectedChild.Pane))
                    {
                        return false;
                    }
                }

                return true;

            default:
                return false;
        }
    }

    private static void AddTile(
        DashboardLayoutSettings layout,
        string typeId,
        int row,
        int column,
        int columnSpan,
        int rowSpan,
        bool isVisible = true)
    {
        layout.Tiles.Add(new()
        {
            Id = typeId,
            TypeId = typeId,
            Order = layout.Tiles.Count,
            Row = row,
            Column = column,
            ColumnSpan = columnSpan,
            RowSpan = rowSpan,
            IsVisible = isVisible,
        });
    }
}
