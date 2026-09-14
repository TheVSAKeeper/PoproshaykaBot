using KeepShell.Bootstrap;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class DashboardPaneLayoutTests
{
    private const int OverflowingTileHeight = 400;

    private static readonly Size PreviewSize = new(1000, 600);

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
        using var dashboard = CreateDashboard(PinwheelLayout(), PinwheelTiles());

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

    [TestCase(SplitOrientation.Columns, true)]
    [TestCase(SplitOrientation.Rows, false)]
    public void Collapsed_tile_drops_its_width_floor_to_the_strip_only_inside_a_columns_split(SplitOrientation orientation, bool strip)
    {
        var columns = orientation == SplitOrientation.Columns;
        var floor = strip ? DashboardTileViewModel.ScaledCollapsedStripWidth : 280d;

        var layout = new DashboardLayoutSettings
        {
            ColumnCount = columns ? 2 : 1,
            RowCount = columns ? 1 : 2,
        };

        AddTile(layout, "stream-info", 0, 0, 1, 1);
        AddTile(layout, "twitch-chat", columns ? 0 : 1, columns ? 1 : 0, 1, 1);

        layout.Tiles.Single(tile => string.Equals(tile.TypeId, "twitch-chat", StringComparison.Ordinal)).IsCollapsed = true;

        using var dashboard = CreateDashboard(layout, new FakeTile("stream-info"), new FakeTile("twitch-chat", fills: true, minWidth: 280));

        var root = dashboard.Pane as SplitPaneLayout;
        var chat = root?.Children[1].Pane as TilePaneLayout;

        Assert.Multiple(() =>
        {
            Assert.That(root?.Orientation, Is.EqualTo(orientation));
            Assert.That(chat?.Tile.TypeId, Is.EqualTo("twitch-chat"));
            Assert.That(chat?.Width.Min, Is.EqualTo(floor),
                "Вдоль колоночного разреза пол свёрнутой плитки – ширина полосы, а не её собственный минимум; поперёк она остаётся шапкой во всю колонку.");
            Assert.That(chat?.Tile.IsCollapsedToStrip, Is.EqualTo(strip));
        });

        dashboard.SetStacked(true);

        Assert.That(chat?.Tile.IsCollapsedToStrip, Is.False,
            "В стопке лист лежит во всю ширину, вертикальной полосе там взяться неоткуда.");

        dashboard.SetStacked(false);

        Assert.That(chat?.Tile.IsCollapsedToStrip, Is.EqualTo(strip), "Выход из стопки возвращает полосу.");
    }

    [Test]
    public void Font_scale_change_moves_the_strip_width_and_the_floor_of_the_collapsed_leaf_together()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 1,
        };

        AddTile(layout, "stream-info", 0, 0, 1, 1);
        AddTile(layout, "twitch-chat", 0, 1, 1, 1);

        layout.Tiles.Single(tile => string.Equals(tile.TypeId, "twitch-chat", StringComparison.Ordinal)).IsCollapsed = true;

        using var dashboard = CreateDashboard(layout, new FakeTile("stream-info"), new FakeTile("twitch-chat", fills: true, minWidth: 280));

        Assert.That(
            ((dashboard.Pane as SplitPaneLayout)?.Children[1].Pane as TilePaneLayout)?.Width.Min,
            Is.EqualTo(DashboardTileViewModel.CollapsedStripWidth).Within(0.001),
            "До смены масштаба пол полосы – её собственная ширина.");

        var notified = new List<string?>();

        void OnStaticPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            notified.Add(args.PropertyName);
        }

        DashboardTileViewModel.StaticPropertyChanged += OnStaticPropertyChanged;

        try
        {
            FontScaleManager.Apply(1.5);

            var chat = (dashboard.Pane as SplitPaneLayout)?.Children[1].Pane as TilePaneLayout;

            Assert.Multiple(() =>
            {
                Assert.That(notified, Does.Contain(nameof(DashboardTileViewModel.ScaledCollapsedStripWidth)),
                    "Ширину полосы шаблон берёт привязкой к статическому свойству: без уведомления повёрнутый заголовок остался бы в полосе прежней ширины.");
                Assert.That(DashboardTileViewModel.ScaledCollapsedStripWidth,
                    Is.EqualTo(DashboardTileViewModel.CollapsedStripWidth * 1.5).Within(0.001));
                Assert.That(chat?.Width.Min, Is.EqualTo(DashboardTileViewModel.ScaledCollapsedStripWidth).Within(0.001),
                    "Пол трека считает раскладка, поэтому смена масштаба обязана её повторить – иначе полоса шире своего пола и заголовок режется многоточием.");
            });
        }
        finally
        {
            DashboardTileViewModel.StaticPropertyChanged -= OnStaticPropertyChanged;
            FontScaleManager.Apply(FontScaleManager.DefaultScale);
        }
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
    public void A_tile_that_sizes_to_content_gives_up_its_authored_share()
    {
        using var dashboard = CreateDashboard(
            AuthoredWeightsLayout(),
            new FakeTile("stream-info", sizesToContent: true),
            new FakeTile("broadcast-status"));

        var root = dashboard.Pane as SplitPaneLayout;

        Assert.Multiple(() =>
        {
            Assert.That(root?.Children[0].SizesToContent, Is.True,
                "Плитка по содержимому не берёт долю разреза – иначе она растягивается вместе с колонкой.");
            Assert.That(root?.Children[0].HasWeight, Is.False,
                "Доля из файла для такой плитки не применяется, иначе трек снова станет звёздочным.");
            Assert.That(root?.Children[1].HasWeight, Is.True,
                "Сосед по узлу долю сохраняет: фиксируется одна плитка, а не весь узел.");
        });
    }

    [Test]
    public void Share_of_a_tile_that_sizes_to_content_is_cleared_in_the_file_on_load()
    {
        var store = new FakeLayoutStore(AuthoredWeightsLayout());

        using var dashboard = new DashboardViewModel(
            [new FakeTile("stream-info", sizesToContent: true), new FakeTile("broadcast-status")],
            new(store),
            TimeProvider.System);

        var root = store.Saved?.Root as SplitPane;

        Assert.Multiple(() =>
        {
            Assert.That(root?.Children[0].Weight, Is.Null,
                "Фиксированность обязана доехать до файла: иначе доля остаётся в root и Core раздаёт её при перетаскивании соседей.");
            Assert.That(root?.Children[1].Weight, Is.EqualTo(0.4));
        });
    }

    [Test]
    public void Empty_shares_of_content_sized_tiles_survive_the_write_and_the_next_load()
    {
        var store = new FakeLayoutStore(ContentSizedColumnsLayout());

        using var dashboard = new DashboardViewModel(
            [new FakeTile("stream-info", sizesToContent: true), new FakeTile("broadcast-status", sizesToContent: true)],
            new(store),
            TimeProvider.System);

        dashboard.Reload();

        var root = store.Saved?.Root as SplitPane;

        Assert.Multiple(() =>
        {
            Assert.That(root?.Children.Select(child => child.Weight), Is.EqualTo(new double?[] { null, null }),
                "Сшивка дерева при записи обязана держать пустые веса: пересборка по сетке вернула бы им доли.");
            Assert.That(store.Saved?.Tiles.Select(tile => tile.ColumnSpan), Is.EqualTo(new[] { 4, 1 }),
                "Размах фиксированных плиток менять при этом нечему.");
            Assert.That(store.SaveCount, Is.EqualTo(1),
                "Второй заход на дашборд обязан обойтись без записи, иначе каждая загрузка переписывает файл.");
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
    public void Hole_in_the_grid_goes_through_the_tree_and_carries_the_tile_floors()
    {
        using var dashboard = CreateDashboard(HoleLayout(), UserLayoutTiles(420));

        var hole = RightColumnOfTheHoleLayout(dashboard).Children[1];

        Assert.Multiple(() =>
        {
            Assert.That(dashboard.Bands, Is.Empty, "Дыра выражается пустым листом, полосный путь ей больше не нужен.");
            Assert.That(hole.Pane, Is.TypeOf<EmptyPaneLayout>(), "Дыра – это пустая ячейка дерева, а не отсутствующий ребёнок.");
            Assert.That(hole.HasWeight, Is.True, "Пустая ячейка держит свою долю строки, иначе соседи её съедят.");
            Assert.That(RightColumnOfTheHoleLayout(dashboard).IsComplete, Is.True,
                "Узел с дырой собран целиком – значит в правке у него будут и разделители, и клавиатурный перенос доли.");
            Assert.That(hole.Pane.MinHeight(96), Is.Zero, "Пустая ячейка высоты не просит.");
            Assert.That(dashboard.Pane?.MinHeight(96), Is.EqualTo(560),
                "Полы листьев складываются вдоль разреза строк: 200 + 140 у левой колонки и 220 у чата – столько же просила полосная раскладка.");
        });
    }

    [Test]
    public void Track_of_a_hole_gets_no_floor_while_its_neighbour_keeps_one()
    {
        using var dashboard = CreateDashboard(HoleLayout(), UserLayoutTiles(420));

        var right = RightColumnOfTheHoleLayout(dashboard);

        Assert.Multiple(() =>
        {
            Assert.That(DashboardView.TrackFloor(right.Children[1], alongColumns: false), Is.Zero,
                "Дыра высоты не просит: общий пол растягивающегося листа толкал бы настоящие плитки вверх.");
            Assert.That(DashboardView.TrackFloor(right.Children[1], alongColumns: true), Is.Zero,
                "По ширине то же самое – иначе пустая ячейка упирается в 320 px и гонит раскладку в стопку.");
            Assert.That(DashboardView.TrackFloor(right.Children[0], alongColumns: false), Is.EqualTo(130),
                "Настоящий сосед свой пол сохраняет.");
        });
    }

    [Test]
    public void Collapsed_leaf_next_to_a_hole_keeps_the_floor_of_its_track()
    {
        var layout = HoleLayout();

        layout.Tiles.Single(tile => string.Equals(tile.TypeId, "stream-info", StringComparison.Ordinal)).IsCollapsed = true;

        using var dashboard = CreateDashboard(layout, UserLayoutTiles(100));

        var right = RightColumnOfTheHoleLayout(dashboard);
        var left = (SplitPaneLayout)((SplitPaneLayout)((SplitPaneLayout)dashboard.Pane!).Children[0].Pane).Children[0].Pane;

        Assert.Multiple(() =>
        {
            Assert.That((left.Children[0].Pane as TilePaneLayout)?.Height.Min,
                Is.EqualTo(DashboardTileViewModel.ScaledCollapsedHeaderHeight).Within(0.001),
                "Свёрнутая плитка просит ровно свою шапку – место она освобождает, но не исчезает из подсчёта высоты.");
            Assert.That(left.Children[1].Pane.MinHeight(96), Is.EqualTo(100),
                "Пол не пробивает потолок и на дереве: MaxHeight 100 сильнее MinHeight 140.");
            Assert.That(right.MinHeight(96), Is.EqualTo(130), "Дыра рядом не добавляет к полу колонки ничего.");
        });
    }

    private static SplitPaneLayout RightColumnOfTheHoleLayout(DashboardViewModel dashboard)
    {
        var root = (SplitPaneLayout)dashboard.Pane!;
        var top = (SplitPaneLayout)root.Children[0].Pane;

        return (SplitPaneLayout)top.Children[1].Pane;
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

    [TestCase("stream-info;broadcast-profiles;polls;logs;obs-info", 1.0, 265d, 200d)]
    [TestCase("stream-info;broadcast-profiles;polls;logs;obs-info", 1.6, 424d, 400d)]
    [TestCase("broadcast-profiles;polls;logs", 1.0, 499d, 400d)]
    public void Column_of_collapsed_tiles_asks_for_the_height_of_their_headers(string collapsed, double scale, double floor, double viewport)
    {
        var layout = DefaultColumnLayout();

        foreach (var typeId in collapsed.Split(';'))
        {
            layout.Tiles.Single(tile => string.Equals(tile.TypeId, typeId, StringComparison.Ordinal)).IsCollapsed = true;
        }

        try
        {
            FontScaleManager.Apply(scale);

            using var dashboard = CreateDashboard(layout, DefaultColumnTiles());

            Assert.Multiple(() =>
            {
                Assert.That(dashboard.Pane?.MinHeight(96 * scale), Is.EqualTo(floor).Within(0.001),
                    "Свёрнутая плитка – это её шапка, и высоту шапки колонка просит так же, как просила бы содержимое.");
                Assert.That(dashboard.Pane?.MinHeight(96 * scale), Is.GreaterThan(viewport),
                    "С нулевым полом свёрнутой плитки колонка считалась помещающейся и уезжала за нижний край без полосы прокрутки.");
            });
        }
        finally
        {
            FontScaleManager.Apply(FontScaleManager.DefaultScale);
        }
    }

    [TestCase(1.0, 730, 100)]
    [TestCase(1.6, 1168, 160)]
    public void Floor_and_ceiling_of_a_column_follow_the_font_scale(double scale, double floor, double capped)
    {
        try
        {
            FontScaleManager.Apply(scale);

            using var column = CreateDashboard(DefaultColumnLayout(), DefaultColumnTiles());
            using var hole = CreateDashboard(HoleLayout(), UserLayoutTiles(100));

            var left = (SplitPaneLayout)((SplitPaneLayout)((SplitPaneLayout)hole.Pane!).Children[0].Pane).Children[0].Pane;

            Assert.Multiple(() =>
            {
                Assert.That(column.Pane?.MinHeight(96 * scale), Is.EqualTo(floor).Within(0.001),
                    "Содержимое плитки растёт вместе с масштабом шрифта, поэтому пол строки обязан расти тем же множителем – иначе «Информация о стриме» опять уходит во внутреннюю прокрутку.");
                Assert.That(left.Children[1].Pane.MinHeight(96 * scale), Is.EqualTo(capped).Within(0.001),
                    "Потолок масштабируется тем же множителем, иначе при 1.6 пол пробил бы MaxHeight 100 на ровном месте.");
            });
        }
        finally
        {
            FontScaleManager.Apply(FontScaleManager.DefaultScale);
        }
    }

    [Test]
    public void Band_without_a_tree_reports_the_floor_of_its_rows()
    {
        using var dashboard = CreateDashboard(PinwheelLayout(), PinwheelTiles());

        Assert.That(dashboard.Pane, Is.Null, "Вертушку дерево не выражает – считать полы обязан полосный путь.");
        Assert.That(dashboard.Bands.Max(band => band.MinHeight), Is.EqualTo(510).Within(0.001),
            "Полосный путь считает пол по своим строкам: 200 у «Информации о стриме», 220 у чата и 90 у профилей рассылки.");
    }

    [Test]
    public void Band_without_a_tree_counts_a_collapsed_row_by_its_header()
    {
        var layout = PinwheelLayout();

        foreach (var tile in layout.Tiles)
        {
            tile.IsCollapsed = true;
        }

        using var dashboard = CreateDashboard(layout, PinwheelTiles());

        Assert.That(dashboard.Pane, Is.Null, "Вертушку дерево не выражает – полы тут считает полосный путь.");
        Assert.That(dashboard.Bands.Max(band => band.MinHeight),
            Is.EqualTo(DashboardTileViewModel.CollapsedHeaderHeight * 3).Within(0.001),
            "Три строки свёрнутых плиток – это три шапки, а не ноль, иначе переполнение полосы не опознаётся.");
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
    public void A_move_that_leaves_the_layout_as_it_was_still_clears_the_notice()
    {
        using var dashboard = CreateDashboard(AuthoredWeightsLayout(), new FakeTile("stream-info"), new FakeTile("broadcast-status"));

        dashboard.ToggleEditCommand.Execute(null);
        dashboard.ShowEditNotice("Плитку не получилось перенести на это место.");

        dashboard.ReportMove(DashboardEditStatus.Applied);

        Assert.That(dashboard.EditNotice, Is.Null,
            "Перенос на занимаемое место дерева не меняет, события Changed не будет – ленту гасит сам удавшийся исход.");
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

    private static DashboardLayoutSettings ContentSizedColumnsLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 5,
            RowCount = 1,
            Root = new SplitPane(SplitOrientation.Columns,
            [
                new(new TilePane("stream-info"), 0.8),
                new(new TilePane("broadcast-status"), 0.2),
            ]),
        };

        AddTile(layout, "stream-info", 0, 0, 1, 4);
        AddTile(layout, "broadcast-status", 0, 4, 1, 1);

        return layout;
    }

    private static DashboardViewModel CreateDashboard(DashboardLayoutSettings layout, params DashboardTileViewModel[] tiles)
    {
        return new(tiles, new(new FakeLayoutStore(layout)), TimeProvider.System);
    }

    private static DashboardLayoutSettings PinwheelLayout()
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

        return layout;
    }

    private static DashboardTileViewModel[] PinwheelTiles()
    {
        return
        [
            new FakeTile("stream-info", minHeight: 200),
            new FakeTile("broadcast-status", minHeight: 100),
            new FakeTile("broadcast-profiles", minHeight: 90),
            new FakeTile("polls", minHeight: 130),
            new FakeTile("twitch-chat", fills: true, minHeight: 220),
        ];
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

    [Test]
    public void Splitter_hint_names_the_floor_that_actually_holds_the_track()
    {
        using var dashboard = CreateDashboard(StackedNeighbourLayout(), StackedNeighbourTiles());

        var root = (SplitPaneLayout)dashboard.Pane!;
        var below = (SplitPaneLayout)root.Children[1].Pane;

        Assert.Multiple(() =>
        {
            Assert.That(DashboardView.FloorObstacle(root.Children[1], alongColumns: false),
                Is.EqualTo("несколько плиток одна под другой уже на минимуме высоты"),
                "Высоту держит сумма полов колонки, а не самая высокая плитка ветки – называть одну из них нечестно.");
            Assert.That(DashboardView.FloorObstacle(root.Children[0], alongColumns: false),
                Is.EqualTo("плитка «stream-info» уже на минимуме"),
                "Один лист – один виновник, как и было.");
            Assert.That(DashboardView.FloorObstacle(below.Children[1], alongColumns: true),
                Is.EqualTo("колонка не бывает уже 320 px"),
                "Колонку держит общий пол растягивающегося листа, а не собственный минимум чата в 280 px.");
        });
    }

    [Test]
    public void Splitter_hint_tells_a_shared_ceiling_from_a_single_one()
    {
        using var dashboard = CreateDashboard(StackedNeighbourLayout(), StackedNeighbourTiles());

        var root = (SplitPaneLayout)dashboard.Pane!;
        var below = (SplitPaneLayout)root.Children[1].Pane;

        Assert.Multiple(() =>
        {
            Assert.That(DashboardView.CeilingObstacle(below.Children[0].Pane, alongColumns: false),
                Is.EqualTo("плитки одна под другой вместе не выше 740 px"),
                "Потолок колонки – сумма потолков её плиток, и одной плиткой он не объясняется.");
            Assert.That(DashboardView.CeilingObstacle(root.Children[0].Pane, alongColumns: false),
                Is.EqualTo("плитка «stream-info» не выше 400 px"));
            Assert.That(DashboardView.CeilingObstacle(below.Children[1].Pane, alongColumns: true),
                Is.Null,
                "У чата потолка нет, и упираться в него нечем.");
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Splitter_hint_keeps_quiet_until_the_grid_is_actually_laid_out()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new() { MinWidth = 320 },
                new() { MinWidth = 320 },
            },
        };

        Assert.That(DashboardView.HasReliableLayout(grid), Is.False,
            "До прохода раскладки дорожки нулевые, и любой упор по ним – выдуманный.");

        grid.Measure(new(800, 400));
        grid.Arrange(new(0, 0, 800, 400));

        Assert.That(DashboardView.HasReliableLayout(grid), Is.True,
            "Размеченная сетка – единственное состояние, в котором об упоре можно судить.");

        grid.InvalidateArrange();

        Assert.That(DashboardView.HasReliableLayout(grid), Is.False,
            "Перестроение дерева и автоповтор Ctrl+стрелки опережают Arrange, и прежние размеры дорожек уже не про эту раскладку.");
    }

    [Test]
    public void Drop_target_holds_on_over_the_seam_between_panes()
    {
        var panes = new[]
        {
            new Rect(0, 0, 398, 300),
            new Rect(402, 0, 398, 300),
            new Rect(0, 306, 800, 294),
        };

        Assert.Multiple(() =>
        {
            Assert.That(DashboardView.NearestPane(panes, new(200, 150), 24), Is.Zero,
                "Точка внутри панели берёт её саму.");

            Assert.That(DashboardView.NearestPane(panes, new(399, 150), 24), Is.Zero,
                "Разделитель лежит поверх шва, и подсказка обязана держаться за ближнюю панель, а не гаснуть под ним.");

            Assert.That(DashboardView.NearestPane(panes, new(401, 150), 24), Is.EqualTo(1),
                "На другой половине шва ближняя панель уже правая.");

            Assert.That(DashboardView.NearestPane(panes, new(200, 302), 24), Is.Zero,
                "Щель между рядами тоже отдаёт ближнюю панель.");

            Assert.That(DashboardView.NearestPane(panes, new(200, 700), 24), Is.EqualTo(-1),
                "Курсор, уехавший за дашборд, цели не имеет.");

            Assert.That(DashboardView.NearestPane(panes, new(399, 150), 24, skip: 0), Is.EqualTo(1),
                "Плитку-источник из целей выбрасывают: иначе на шве она выигрывает ничью у соседа и гасит подсказку у собственного края.");
        });
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Drop_hint_promises_the_width_the_move_frees_up()
    {
        var overlay = new FakeTile("chat-overlay", fills: true, minWidth: 320, minHeight: 220);
        var chat = new FakeTile("twitch-chat", fills: true, minWidth: 280, minHeight: 220);

        using var dashboard = CreateDashboard(TwoColumnsLayout(), overlay, chat);

        dashboard.ToggleEditCommand.Execute(null);

        var source = PathOf(dashboard, overlay);
        var target = PathOf(dashboard, chat);
        var preview = dashboard.PreviewEdit(source, target, PaneSide.Top);

        Assert.That(preview, Is.Not.Null, "Перенос разрешён, значит подсказке есть что показать.");

        var predicted = Measure(preview!, overlay);

        Assert.Multiple(() =>
        {
            Assert.That(predicted.X, Is.Zero.Within(0.5), "Левая колонка освобождается и узел схлопывается – подсказка начинается у левого края панели.");

            Assert.That(predicted.Width, Is.EqualTo(PreviewSize.Width).Within(0.5),
                "Подсказка обещала верх правой колонки, а плитка вставала на верх всей ширины – ровно эта жалоба и правится.");

            Assert.That(predicted.Height, Is.EqualTo(PreviewSize.Height / 2).Within(0.5));
        });

        Assert.That(dashboard.Move(source, target, PaneSide.Top), Is.EqualTo(DashboardEditStatus.Applied));

        AssertSameRect(predicted, Measure(dashboard.Pane!, overlay));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    [TestCase("stream-info", "twitch-chat", PaneSide.Bottom)]
    [TestCase("stream-info", "twitch-chat", PaneSide.None)]
    [TestCase("twitch-chat", "broadcast-status", PaneSide.Left)]
    [TestCase("twitch-chat", "stream-info", PaneSide.None)]
    [TestCase("broadcast-status", "stream-info", PaneSide.Top)]
    public void Drop_hint_matches_the_layout_the_drop_produces(string sourceTypeId, string targetTypeId, PaneSide side)
    {
        DashboardTileViewModel[] tiles =
        [
            new FakeTile("stream-info"),
            new FakeTile("broadcast-status"),
            new FakeTile("twitch-chat", fills: true, minWidth: 280, minHeight: 220),
        ];

        using var dashboard = CreateDashboard(SideBySideLayout(), tiles);

        dashboard.ToggleEditCommand.Execute(null);

        var dragged = tiles.Single(tile => string.Equals(tile.TypeId, sourceTypeId, StringComparison.Ordinal));
        var source = PathOf(dashboard, dragged);
        var target = PathOf(dashboard, tiles.Single(tile => string.Equals(tile.TypeId, targetTypeId, StringComparison.Ordinal)));

        var preview = dashboard.PreviewEdit(source, target, side);

        Assert.That(preview, Is.Not.Null, "Операция разрешена, значит подсказка обязана быть.");

        var predicted = Measure(preview!, dragged);

        var status = side == PaneSide.None
            ? dashboard.Swap(source, target)
            : dashboard.Move(source, target, side);

        Assert.That(status, Is.EqualTo(DashboardEditStatus.Applied));

        AssertSameRect(predicted, Measure(dashboard.Pane!, dragged));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    [TestCase(PaneSide.Top)]
    [TestCase(PaneSide.None)]
    public void Drop_hint_measures_an_empty_cell_like_any_other_target(PaneSide side)
    {
        var tiles = UserLayoutTiles(420);

        using var dashboard = CreateDashboard(HoleLayout(), tiles);

        dashboard.ToggleEditCommand.Execute(null);

        var dragged = tiles.Single(tile => string.Equals(tile.TypeId, "stream-info", StringComparison.Ordinal));
        var source = PathOf(dashboard, dragged);
        var hole = RightColumnOfTheHoleLayout(dashboard).Children[1].Pane.Path;

        var preview = dashboard.PreviewEdit(source, hole, side);

        Assert.That(preview, Is.Not.Null, "Пустая ячейка – такой же лист, и подсказка над ней обязана считаться.");

        var predicted = Measure(preview!, dragged);

        var status = side == PaneSide.None
            ? dashboard.Swap(source, hole)
            : dashboard.Move(source, hole, side);

        Assert.That(status, Is.EqualTo(DashboardEditStatus.Applied));

        AssertSameRect(predicted, Measure(dashboard.Pane!, dragged));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Drop_hint_keeps_quiet_where_the_drop_would_be_refused()
    {
        var chat = new FakeTile("twitch-chat", fills: true, minWidth: 280, minHeight: 220);

        using var dashboard = CreateDashboard(SingleTileLayout(), chat);

        dashboard.ToggleEditCommand.Execute(null);

        var only = PathOf(dashboard, chat);

        Assert.That(dashboard.PreviewEdit(only, only, PaneSide.Top), Is.Null,
            "Отказ операции – это отсутствие подсказки, а не рамка по старой геометрии.");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Drop_hint_leaves_room_for_the_scrollbar_of_a_content_sized_root()
    {
        DashboardTileViewModel[] tiles =
        [
            new FakeTile("stream-info", sizesToContent: true),
            new FakeTile("broadcast-status", sizesToContent: true),
        ];

        using var dashboard = CreateDashboard(ContentSizedRowsLayout(), tiles);

        Assert.That(dashboard.Pane!.Scrollable, Is.True,
            "Корень из одних Auto-листьев панель заворачивает в ScrollViewer – на этом случае и держится проверка.");

        var predicted = Measure(dashboard.Pane!, tiles[0], _ => new Size(PreviewSize.Width + 200, OverflowingTileHeight));

        Assert.That(predicted.Width, Is.LessThan(PreviewSize.Width),
            "Две плитки по содержимому не влезают по высоте, и в панели появляется полоса прокрутки, отнимающая ширину: измерение без той же обёртки обещает плитке всю ширину панели, и рамка подсказки вылезает за полосу.");
    }

    private static DashboardLayoutSettings ContentSizedRowsLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 1,
            RowCount = 2,
            Root = new SplitPane(SplitOrientation.Rows,
            [
                new(new TilePane("stream-info"), 0.5),
                new(new TilePane("broadcast-status"), 0.5),
            ]),
        };

        AddTile(layout, "stream-info", 0, 0, 1, 1);
        AddTile(layout, "broadcast-status", 1, 0, 1, 1);

        return layout;
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    [TestCase(SplitOrientation.Columns)]
    [TestCase(SplitOrientation.Rows)]
    public void Child_of_a_split_keeps_its_ceiling_across_the_cut_and_stands_in_the_corner(SplitOrientation orientation)
    {
        const double Ceiling = 300;

        var alongColumns = orientation == SplitOrientation.Columns;

        var fixedTile = new FakeTile(
            "stream-info",
            maxWidth: alongColumns ? null : (int)Ceiling,
            maxHeight: alongColumns ? (int)Ceiling : null);

        var chat = new FakeTile("twitch-chat", fills: true, minWidth: 280, minHeight: 220);

        using var dashboard = CreateDashboard(CrossAxisLayout(orientation), fixedTile, chat);

        var measured = Measure(dashboard.Pane!, fixedTile, _ => PreviewSize);

        var across = alongColumns ? measured.Height : measured.Width;
        var corner = alongColumns ? measured.Y : measured.X;

        Assert.Multiple(() =>
        {
            Assert.That(across, Is.EqualTo(Ceiling).Within(0.5),
                "Поперёк разреза потолок ставится самому потомку: трек сетки ограничивает только ось разреза, и плитка по содержимому растягивалась на всё место узла.");

            Assert.That(corner, Is.Zero.Within(0.5),
                "Панель по содержимому прижимается к началу своего места, а не висит по центру остатка.");
        });
    }

    private static DashboardLayoutSettings CrossAxisLayout(SplitOrientation orientation)
    {
        var alongColumns = orientation == SplitOrientation.Columns;

        var layout = new DashboardLayoutSettings
        {
            ColumnCount = alongColumns ? 2 : 1,
            RowCount = alongColumns ? 1 : 2,
            Root = new SplitPane(orientation,
            [
                new(new TilePane("stream-info"), 0.5),
                new(new TilePane("twitch-chat"), 0.5),
            ]),
        };

        AddTile(layout, "stream-info", 0, 0, 1, 1);
        AddTile(layout, "twitch-chat", alongColumns ? 0 : 1, alongColumns ? 1 : 0, 1, 1);

        return layout;
    }

    private static void AssertSameRect(Rect predicted, Rect actual)
    {
        Assert.Multiple(() =>
        {
            Assert.That(predicted.X, Is.EqualTo(actual.X).Within(0.5), "Предсказанное место обязано совпасть с тем, куда плитка встала после броска.");
            Assert.That(predicted.Y, Is.EqualTo(actual.Y).Within(0.5));
            Assert.That(predicted.Width, Is.EqualTo(actual.Width).Within(0.5));
            Assert.That(predicted.Height, Is.EqualTo(actual.Height).Within(0.5));
        });
    }

    private static Rect Measure(PaneLayout pane, DashboardTileViewModel tile, Func<DashboardTileViewModel, Size>? content = null)
    {
        var leaf = DashboardView.LeafOf(pane, tile);

        Assert.That(leaf, Is.Not.Null, "Перетаскиваемая плитка обязана найтись в получившемся дереве.");

        var rect = DashboardView.MeasurePane(pane, leaf!, PreviewSize, content);

        Assert.That(rect, Is.Not.Null, "Раскладка меряется на фактический размер панели.");

        return rect!.Value;
    }

    private static int[] PathOf(DashboardViewModel dashboard, DashboardTileViewModel tile)
    {
        var leaf = DashboardView.LeafOf(dashboard.Pane!, tile);

        Assert.That(leaf, Is.Not.Null, $"Плитка {tile.TypeId} обязана быть на панели.");

        return leaf!.Path;
    }

    private static DashboardLayoutSettings TwoColumnsLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 1,
        };

        AddTile(layout, "chat-overlay", 0, 0, 1, 1);
        AddTile(layout, "twitch-chat", 0, 1, 1, 1);

        return layout;
    }

    private static DashboardLayoutSettings SingleTileLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 1,
            RowCount = 1,
        };

        AddTile(layout, "twitch-chat", 0, 0, 1, 1);

        return layout;
    }

    private static DashboardTileViewModel[] StackedNeighbourTiles()
    {
        return
        [
            new FakeTile("stream-info", maxHeight: 400, minHeight: 200),
            new FakeTile("broadcast-profiles", grows: true, maxHeight: 320, minHeight: 130),
            new FakeTile("obs-info", maxHeight: 420, minHeight: 140),
            new FakeTile("twitch-chat", fills: true, minWidth: 280, minHeight: 220),
        ];
    }

    private static DashboardLayoutSettings StackedNeighbourLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 3,
            Root = new SplitPane(SplitOrientation.Rows,
            [
                new(new TilePane("stream-info"), 1.0 / 3),
                new(new SplitPane(SplitOrientation.Columns,
                [
                    new(new SplitPane(SplitOrientation.Rows,
                    [
                        new(new TilePane("broadcast-profiles"), 0.5),
                        new(new TilePane("obs-info"), 0.5),
                    ]), 0.5),
                    new(new TilePane("twitch-chat"), 0.5),
                ]), 2.0 / 3),
            ]),
        };

        AddTile(layout, "stream-info", 0, 0, 1, 2);
        AddTile(layout, "broadcast-profiles", 1, 0, 1, 1);
        AddTile(layout, "obs-info", 2, 0, 1, 1);
        AddTile(layout, "twitch-chat", 1, 1, 2, 1);

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
