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
            Assert.That(column?.Height.Length.IsStar, Is.True,
                "Разбор сетки задал столбцу доли, а заданная доля растягивает трек и у плитки по контенту – иначе процент не виден.");
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
            Is.EqualTo(860),
            "Полы всех трёх плиток складываются – по ним хост решает, не пора ли уйти в стопку: растягивающимся достаётся общий пол полосы, плитке по контенту – её собственный.");
    }

    [Test]
    public void Authored_weights_stretch_a_node_of_content_sized_tiles()
    {
        using var dashboard = CreateDashboard(AuthoredWeightsLayout(), new FakeTile("stream-info"), new FakeTile("broadcast-status"));

        var root = dashboard.Pane as SplitPaneLayout;

        Assert.Multiple(() =>
        {
            Assert.That(root?.Children[0].HasWeight, Is.True,
                "Доля из файла задаёт трек сама – иначе процент, выставленный разделителем, ни на что не влияет.");
            Assert.That(root?.Width.Length.IsStar, Is.True,
                "Узел с явными долями обязан растягиваться: в Auto-колонке звёздочные треки схлопываются.");
        });
    }

    [Test]
    public void Tile_minimums_add_up_along_the_split_and_never_pass_the_ceiling()
    {
        using var dashboard = CreateDashboard(
            SideBySideLayout(),
            new FakeTile("stream-info", minWidth: 200, minHeight: 150),
            new FakeTile("broadcast-status", maxHeight: 100, minWidth: 180, minHeight: 180),
            new FakeTile("twitch-chat", fills: true, minWidth: 280, minHeight: 220));

        var root = dashboard.Pane as SplitPaneLayout;
        var column = root?.Children[0].Pane as SplitPaneLayout;
        var status = column?.Children[1].Pane as TilePaneLayout;

        Assert.Multiple(() =>
        {
            Assert.That(status?.Height.Min, Is.EqualTo(100),
                "Пол не пробивает потолок: у плитки с MaxHeight 100 минимум обязан ужаться до него.");
            Assert.That(column?.Height.Min, Is.EqualTo(250),
                "Вдоль разреза минимумы складываются – столбцу нужно место под обе плитки.");
            Assert.That(column?.Width.Min, Is.EqualTo(200),
                "Поперёк разреза берётся самый широкий из детей.");
            Assert.That(root?.Width.Min, Is.EqualTo(480),
                "Корень режется по колонкам, значит ширины столбца и чата суммируются.");
        });
    }

    [Test]
    public void Tile_floors_reach_the_rows_of_a_layout_no_guillotine_cut_expresses()
    {
        using var dashboard = CreateDashboard(HoleLayout(), UserLayoutTiles(420));

        Assert.That(dashboard.Pane, Is.Null, "В сетке дыра, дерева не будет – полосный путь и обязан донести полы до строк.");

        var rows = dashboard.Bands.Single().Rows;

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Min, Is.EqualTo(200),
                "Строка берёт наибольший пол своих плиток: 200 у «Информации о стриме» против 65 у профилей рассылки на две строки.");
            Assert.That(rows[1].Min, Is.EqualTo(70), "Пол плитки на две строки делится между ними, иначе строка просит вдвое больше нужного.");
            Assert.That(rows[2].Min, Is.EqualTo(70));
            Assert.That(rows[3].Min, Is.EqualTo(220 / 3D).Within(0.001), "Растягивающийся чат тоже несёт пол – 220 на три строки.");
            Assert.That(rows.Sum(row => row.Min), Is.LessThanOrEqualTo(573),
                "Полы колонки обязаны влезать в дашборд при MinHeight окна 640: 640 − 37 заголовка − 30 статусной строки, иначе нижняя плитка уезжает за край без прокрутки.");
        });
    }

    [Test]
    public void Row_floor_of_a_band_yields_to_the_ceiling_and_to_a_collapsed_tile()
    {
        var layout = HoleLayout();

        layout.Tiles.Single(tile => string.Equals(tile.TypeId, "stream-info", StringComparison.Ordinal)).IsCollapsed = true;

        using var dashboard = CreateDashboard(layout, UserLayoutTiles(100));

        var rows = dashboard.Bands.Single().Rows;

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Min, Is.EqualTo(65),
                "Свёрнутая плитка пола не просит – строке остаётся доля соседа, иначе свёртка перестанет освобождать место.");
            Assert.That(rows[2].Min, Is.EqualTo(50),
                "Пол не пробивает потолок и в полосе: MaxHeight 100 у плитки на две строки даёт 50 на строку.");
        });
    }

    [TestCase(573, ExpectedResult = true)]
    [TestCase(733, ExpectedResult = false)]
    public bool Column_of_the_default_layout_outgrows_the_viewport_and_asks_for_scrolling(double viewport)
    {
        using var dashboard = CreateDashboard(DefaultColumnLayout(), DefaultColumnTiles());

        Assert.That(dashboard.Pane?.MinHeight(96), Is.EqualTo(730),
            "Полы колонки складываются вдоль разреза строк: 200 + 130 + 130 + 130 + 140 – столько высоты просит раскладка по умолчанию.");

        return dashboard.Pane?.MinHeight(96) > viewport;
    }

    [Test]
    public void Band_without_a_tree_reports_the_floor_of_its_rows()
    {
        using var dashboard = CreateDashboard(HoleLayout(), UserLayoutTiles(420));

        Assert.That(dashboard.Bands.Single().MinHeight, Is.EqualTo(560).Within(0.001),
            "Полосный путь считает пол по своим строкам: 560 влезает в бюджет 573, и панель обязана остаться сеткой без прокрутки.");
    }

    [Test]
    public void Edit_mode_unfolds_a_collapsed_tile_and_folds_it_back_on_exit()
    {
        var layout = AuthoredWeightsLayout();

        layout.Tiles.Single(tile => string.Equals(tile.TypeId, "broadcast-status", StringComparison.Ordinal)).IsCollapsed = true;

        var store = new FakeLayoutStore(layout);
        var status = new FakeTile("broadcast-status");

        using var dashboard = new DashboardViewModel([new FakeTile("stream-info"), status], new(store), TimeProvider.System);

        Assert.That(status.IsCollapsed, Is.True, "Вне правки свёрнутая плитка остаётся свёрнутой.");

        dashboard.ToggleEditCommand.Execute(null);

        Assert.That(status.IsCollapsed, Is.False,
            "В правке плитку надо видеть целиком: свёрнутая идёт по содержимому, её нельзя ни растянуть, ни поймать за ручку.");

        dashboard.ToggleEditCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsCollapsed, Is.True, "Выход из правки возвращает свёрнутость.");
            Assert.That(
                store.LoadDashboard()!.Tiles.Single(tile => string.Equals(tile.TypeId, "broadcast-status", StringComparison.Ordinal)).IsCollapsed,
                Is.True,
                "Раскрытие на время правки не должно уезжать в файл.");
        });
    }

    [Test]
    public void A_collapsed_leaf_ignores_its_weight()
    {
        var layout = AuthoredWeightsLayout();

        layout.Tiles.Single(tile => string.Equals(tile.TypeId, "broadcast-status", StringComparison.Ordinal)).IsCollapsed = true;

        using var dashboard = CreateDashboard(layout, new FakeTile("stream-info"), new FakeTile("broadcast-status"));

        var root = dashboard.Pane as SplitPaneLayout;

        Assert.That(root?.Children[1].HasWeight, Is.False,
            "Свёрнутая плитка идёт по контенту, доля её не растягивает.");
    }

    [Test]
    public void A_leaf_of_a_foreign_host_does_not_shift_the_paths_of_its_siblings()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 3,
            RowCount = 1,
            Root = new SplitPane(SplitOrientation.Columns,
            [
                new(new TilePane("logs"), 0.2),
                new(new TilePane("stream-info"), 0.3),
                new(new TilePane("twitch-chat"), 0.5),
            ]),
        };

        AddTile(layout, "logs", 0, 0, 1, 1);
        AddTile(layout, "stream-info", 0, 1, 1, 1);
        AddTile(layout, "twitch-chat", 0, 2, 1, 1);

        using var dashboard = CreateDashboard(layout, new FakeTile("stream-info"), new FakeTile("twitch-chat", fills: true));

        var root = dashboard.Pane as SplitPaneLayout;

        Assert.Multiple(() =>
        {
            Assert.That(root?.IsComplete, Is.False,
                "Плитка чужого хоста выпала из представления – доли такого узла править нельзя, разделителя там не будет.");
            Assert.That((root?.Children[0].Pane as TilePaneLayout)?.Path, Is.EqualTo(new[] { 1 }),
                "Путь листа адресует исходное дерево, а не сжатое представление: иначе правка уехала бы в чужой узел.");
            Assert.That((root?.Children[1].Pane as TilePaneLayout)?.Path, Is.EqualTo(new[] { 2 }));
        });
    }

    private static DashboardLayoutSettings AuthoredWeightsLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 1,
            Root = new SplitPane(SplitOrientation.Columns,
            [
                new(new TilePane("stream-info"), 0.6),
                new(new TilePane("broadcast-status"), 0.4),
            ]),
        };

        AddTile(layout, "stream-info", 0, 0, 1, 1);
        AddTile(layout, "broadcast-status", 0, 1, 1, 1);

        return layout;
    }

    private static DashboardViewModel CreateDashboard(DashboardLayoutSettings layout, params DashboardTileViewModel[] tiles)
    {
        return new(tiles, new(new FakeLayoutStore(layout)), TimeProvider.System);
    }

    private static DashboardLayoutSettings HoleLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 6,
        };

        AddTile(layout, "stream-info", 0, 0, 1, 1);
        AddTile(layout, "broadcast-profiles", 0, 1, 2, 1);
        AddTile(layout, "obs-info", 1, 0, 2, 1);
        AddTile(layout, "twitch-chat", 3, 0, 3, 2);

        return layout;
    }

    private static DashboardLayoutSettings DefaultColumnLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 1,
            RowCount = 5,
        };

        AddTile(layout, "stream-info", 0, 0, 1, 1);
        AddTile(layout, "broadcast-profiles", 1, 0, 1, 1);
        AddTile(layout, "polls", 2, 0, 1, 1);
        AddTile(layout, "logs", 3, 0, 1, 1);
        AddTile(layout, "obs-info", 4, 0, 1, 1);

        return layout;
    }

    private static DashboardTileViewModel[] DefaultColumnTiles()
    {
        return
        [
            new FakeTile("stream-info", minHeight: 200),
            new FakeTile("broadcast-profiles", grows: true, minHeight: 130),
            new FakeTile("polls", grows: true, minHeight: 130),
            new FakeTile("logs", fills: true, minHeight: 130),
            new FakeTile("obs-info", minHeight: 140),
        ];
    }

    private static DashboardTileViewModel[] UserLayoutTiles(int obsMaxHeight)
    {
        return
        [
            new FakeTile("stream-info", maxWidth: 420, maxHeight: 400, minHeight: 200),
            new FakeTile("broadcast-profiles", grows: true, maxWidth: 500, maxHeight: 320, minHeight: 130),
            new FakeTile("obs-info", maxWidth: 380, maxHeight: obsMaxHeight, minHeight: 140),
            new FakeTile("twitch-chat", fills: true, minWidth: 280, minHeight: 220),
        ];
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
