using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class DashboardPaneLayoutTests
{
    [Test]
    public void Grid_without_a_tree_in_the_file_is_rendered_through_the_guillotine_split()
    {
        using var dashboard = CreateDashboard(SideBySideLayout(), new FakeTile("stream-info"), new FakeTile("broadcast-status"), new FakeTile("twitch-chat", fills: true));

        Assert.That(dashboard.Pane, Is.TypeOf<SplitPaneLayout>(), "Раскладка режется вертикальным швом, значит дерево обязано собраться и без поля root в файле.");

        var root = (SplitPaneLayout)dashboard.Pane!;

        Assert.Multiple(() =>
        {
            Assert.That(root.Orientation, Is.EqualTo(SplitOrientation.Columns));
            Assert.That(root.Children, Has.Count.EqualTo(2));
            Assert.That(root.Children[0].Weight, Is.EqualTo(0.5).Within(0.001), "Левая половина занимает две колонки из четырёх.");
            Assert.That(root.Children[1].Weight, Is.EqualTo(0.5).Within(0.001));
            Assert.That(dashboard.Bands, Is.Empty, "Полосы – запасной путь, при живом дереве их считать незачем.");
        });

        var chat = root.Children[1].Pane as TilePaneLayout;
        var column = root.Children[0].Pane as SplitPaneLayout;

        Assert.Multiple(() =>
        {
            Assert.That(chat?.Tile.TypeId, Is.EqualTo("twitch-chat"));
            Assert.That(chat?.Width.Length.IsStar, Is.True, "Чат занимает свободное место, значит его колонка звёздочная.");
            Assert.That(chat?.Height.Length.IsStar, Is.True);
            Assert.That(column?.Orientation, Is.EqualTo(SplitOrientation.Rows));
            Assert.That(column?.Children, Has.Count.EqualTo(2));
            Assert.That(column?.Height.Length.IsAuto, Is.True, "Обе плитки столбца идут по контенту, значит и узел по контенту.");
        });
    }

    [Test]
    public void Weights_written_in_the_file_survive_the_render()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 4,
            RowCount = 2,
            Root = new SplitPane(SplitOrientation.Columns, [
                new(new TilePane("twitch-chat"), 0.7),
                new(new SplitPane(SplitOrientation.Rows, [
                    new(new TilePane("stream-info"), 0.5),
                    new(new TilePane("broadcast-status"), 0.5),
                ]), 0.3),
            ]),
        };

        AddTile(layout, "twitch-chat", 0, 0, 2, 3);
        AddTile(layout, "stream-info", 0, 3, 1, 1);
        AddTile(layout, "broadcast-status", 1, 3, 1, 1);

        using var dashboard = CreateDashboard(layout, new FakeTile("stream-info"), new FakeTile("broadcast-status"), new FakeTile("twitch-chat", fills: true));

        var root = dashboard.Pane as SplitPaneLayout;

        Assert.Multiple(() =>
        {
            Assert.That(root?.Children[0].Weight, Is.EqualTo(0.7).Within(0.001), "Доля из файла – это и есть пропорция, которую рисует хост.");
            Assert.That((root?.Children[0].Pane as TilePaneLayout)?.Tile.TypeId, Is.EqualTo("twitch-chat"));
        });
    }

    [Test]
    public void Leaf_of_a_tile_the_host_does_not_know_collapses_its_node()
    {
        using var dashboard = CreateDashboard(SideBySideLayout(), new FakeTile("stream-info"), new FakeTile("twitch-chat", fills: true));

        var root = dashboard.Pane as SplitPaneLayout;
        var left = root?.Children[0].Pane as TilePaneLayout;

        Assert.Multiple(() =>
        {
            Assert.That(root?.Children, Has.Count.EqualTo(2), "Плитка чужого хоста выпадает из дерева, а не оставляет пустой узел.");
            Assert.That(left?.Tile.TypeId, Is.EqualTo("stream-info"), "Узел с единственным выжившим листом схлопывается в сам лист.");
        });
    }

    [Test]
    public void Grid_no_guillotine_cut_expresses_falls_back_to_bands()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 3,
            RowCount = 3,
        };

        AddTile(layout, "stream-info", 0, 0, 1, 2);
        AddTile(layout, "broadcast-status", 0, 2, 2, 1);
        AddTile(layout, "broadcast-profiles", 2, 1, 1, 2);
        AddTile(layout, "polls", 1, 0, 2, 1);
        AddTile(layout, "twitch-chat", 1, 1, 1, 1);

        using var dashboard = CreateDashboard(
            layout,
            new FakeTile("stream-info"),
            new FakeTile("broadcast-status"),
            new FakeTile("broadcast-profiles"),
            new FakeTile("polls"),
            new FakeTile("twitch-chat", fills: true));

        Assert.Multiple(() =>
        {
            Assert.That(dashboard.Pane, Is.Null, "Вертушку не режет ни один сквозной шов – дерева тут быть не должно.");
            Assert.That(dashboard.Bands, Is.Not.Empty, "Без дерева раскладку рисует прежний полосный путь, а не пустой экран.");
        });
    }

    [Test]
    public void Collapsed_tile_keeps_its_leaf_on_content()
    {
        var layout = SideBySideLayout();

        layout.Tiles.Single(tile => string.Equals(tile.TypeId, "twitch-chat", StringComparison.Ordinal)).IsCollapsed = true;

        using var dashboard = CreateDashboard(layout, new FakeTile("stream-info"), new FakeTile("broadcast-status"), new FakeTile("twitch-chat", fills: true));

        var chat = (dashboard.Pane as SplitPaneLayout)?.Children[1].Pane as TilePaneLayout;

        Assert.Multiple(() =>
        {
            Assert.That(chat?.Width.Length.IsAuto, Is.True, "Свёрнутая плитка – это её шапка, растягивать её не за что.");
            Assert.That(chat?.Height.Length.IsAuto, Is.True);
        });
    }

    [Test]
    public void Missing_weight_takes_the_remainder_the_core_would_give_it()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 4,
            RowCount = 1,
            Root = new SplitPane(SplitOrientation.Columns, [
                new(new TilePane("twitch-chat"), 0.75),
                new(new TilePane("stream-info"), null),
            ]),
        };

        AddTile(layout, "twitch-chat", 0, 0, 1, 3);
        AddTile(layout, "stream-info", 0, 3, 1, 1);

        using var dashboard = CreateDashboard(layout, new FakeTile("stream-info"), new FakeTile("twitch-chat", fills: true));

        var root = dashboard.Pane as SplitPaneLayout;

        Assert.That(
            root?.Children[1].Weight,
            Is.EqualTo(0.25).Within(0.001),
            "Незаданная доля – это остаток, как её считает Core; подстановка единицы нарисовала бы 43 % вместо 25 %.");
    }

    [Test]
    public void Floor_of_every_stretching_leaf_adds_up_across_the_split()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 3,
            RowCount = 2,
        };

        AddTile(layout, "stream-info", 0, 0, 2, 1);
        AddTile(layout, "twitch-chat", 0, 1, 2, 1);
        AddTile(layout, "chat-overlay", 0, 2, 2, 1);

        using var dashboard = CreateDashboard(
            layout,
            new FakeTile("stream-info"),
            new FakeTile("twitch-chat", fills: true),
            new FakeTile("chat-overlay", fills: true));

        Assert.That(
            dashboard.Pane?.MinWidth(320),
            Is.EqualTo(640),
            "Пол двух растягивающихся плиток складывается – по нему хост решает, не пора ли уйти в стопку.");
    }

    private static DashboardViewModel CreateDashboard(DashboardLayoutSettings layout, params DashboardTileViewModel[] tiles)
    {
        return new(tiles, new(new FakeLayoutStore(layout)));
    }

    private static DashboardLayoutSettings SideBySideLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 4,
            RowCount = 2,
        };

        AddTile(layout, "stream-info", 0, 0, 1, 2);
        AddTile(layout, "broadcast-status", 1, 0, 1, 2);
        AddTile(layout, "twitch-chat", 0, 2, 2, 2);

        return layout;
    }

    private static void AddTile(DashboardLayoutSettings layout, string typeId, int row, int column, int rowSpan, int columnSpan)
    {
        layout.Tiles.Add(new()
        {
            Id = typeId,
            TypeId = typeId,
            Order = layout.Tiles.Count,
            Row = row,
            Column = column,
            RowSpan = rowSpan,
            ColumnSpan = columnSpan,
            IsVisible = true,
        });
    }
}
