using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class DashboardLayoutSectionTests
{
    private static DashboardLayoutSectionViewModel CreateSection()
    {
        return new([new FakeTile("stream-info"), new FakeTile("broadcast-status"), new FakeTile("twitch-chat", fills: true)]);
    }

    private static DashboardLayoutSettings TwoColumnLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 1,
            Root = new SplitPane(SplitOrientation.Columns, [
                new(new TilePane("stream-info"), 0.6),
                new(new TilePane("twitch-chat"), 0.4),
            ]),
        };

        AddTile(layout, "stream-info", 0, 0);
        AddTile(layout, "twitch-chat", 0, 1);

        return layout;
    }

    private static DashboardLayoutSettings LooseSlotLayout()
    {
        var layout = TwoColumnLayout();

        layout.Root = new SplitPane(SplitOrientation.Columns, [
            new(new TilePane("stream-info"), 0.6),
            new(new TilePane("twitch-chat"), null),
        ]);

        return layout;
    }

    private static void AddTile(
        DashboardLayoutSettings layout,
        string typeId,
        int row,
        int column,
        bool visible = true,
        int rowSpan = 1,
        int columnSpan = 1)
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
            IsVisible = visible,
        });
    }

    private static DashboardLayoutSectionViewModel CreatePinwheelSection()
    {
        var section = new DashboardLayoutSectionViewModel([
            new FakeTile("stream-info"),
            new FakeTile("broadcast-status"),
            new FakeTile("twitch-chat", fills: true),
            new FakeTile("obs-info"),
            new FakeTile("broadcast-profiles"),
        ]);

        var layout = new DashboardLayoutSettings { ColumnCount = 3, RowCount = 3 };

        AddTile(layout, "stream-info", 0, 0, columnSpan: 2);
        AddTile(layout, "broadcast-status", 0, 2, rowSpan: 2);
        AddTile(layout, "twitch-chat", 1, 0, rowSpan: 2);
        AddTile(layout, "obs-info", 2, 1, columnSpan: 2);
        AddTile(layout, "broadcast-profiles", 1, 1);

        section.LoadSettings(layout);

        return section;
    }

    private static int[] PathOf(DashboardLayoutSectionViewModel section, string typeId)
    {
        var leaf = Leaf(section.Pane, typeId);

        Assert.That(leaf, Is.Not.Null, $"Плитка {typeId} обязана быть в дереве превью.");

        return leaf!.Path;
    }

    private static PaneLayout? Leaf(PaneLayout? pane, string typeId)
    {
        switch (pane)
        {
            case TilePaneLayout leaf:
                return string.Equals(leaf.Tile.TypeId, typeId, StringComparison.Ordinal) ? leaf : null;

            case SplitPaneLayout split:
                foreach (var child in split.Children)
                {
                    if (Leaf(child.Pane, typeId) is { } found)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    [Test]
    public void Carries_the_split_tree_from_the_file()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());

        var root = section.BuildLayout().Root as SplitPane;

        Assert.Multiple(() =>
        {
            Assert.That(root, Is.Not.Null, "Без переноса дерева первое сохранение из настроек стёрло бы пропорции, заданные на дашборде.");
            Assert.That(root!.Children[0].Weight, Is.EqualTo(0.6).Within(0.001));
            Assert.That(section.Pane, Is.TypeOf<SplitPaneLayout>(), "Превью рисует то же дерево, что и дашборд.");
        });
    }

    [Test]
    public void Saving_after_an_edit_keeps_the_tree_the_hole_and_the_records_of_other_hosts()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 2,
            Root = new SplitPane(SplitOrientation.Columns, [
                new(new SplitPane(SplitOrientation.Rows, [
                    new(new TilePane("stream-info"), 0.5),
                    new(new TilePane(DashboardLayoutTree.EmptySlotTypeId), 0.5),
                ]), 0.6),
                new(new SplitPane(SplitOrientation.Rows, [
                    new(new TilePane("twitch-chat"), 0.5),
                    new(new TilePane("logs"), 0.5),
                ]), 0.4),
            ]),
        };

        AddTile(layout, "stream-info", 0, 0);
        AddTile(layout, "twitch-chat", 0, 1);
        AddTile(layout, "logs", 1, 1);
        AddTile(layout, "broadcast-status", 1, 0, visible: false);

        var store = new FakeLayoutStore(layout);
        var coordinator = new DashboardLayoutCoordinator(store);
        var section = CreateSection();

        section.LoadSettings(coordinator.Read().Layout);
        section.Resize(PathOf(section, "stream-info")[..^1], [0.3, 0.7]);

        coordinator.Commit(section.BuildLayout(), coordinator.Read().Revision, keepDraftRoot: true);

        var saved = store.Saved;

        Assert.That(saved, Is.Not.Null);

        var savedRoot = saved!.Root as SplitPane;
        var column = savedRoot?.Children[0].Pane as SplitPane;

        Assert.Multiple(() =>
        {
            Assert.That(savedRoot, Is.Not.Null, "Сохранение из настроек обязано унести дерево на диск, иначе пропорции теряются.");
            Assert.That(column?.Children[1].Pane, Is.TypeOf<TilePane>(), "Дыра остаётся листом дерева.");
            Assert.That((column?.Children[1].Pane as TilePane)?.TypeId, Is.EqualTo(DashboardLayoutTree.EmptySlotTypeId));
            Assert.That(column?.Children[0].Weight, Is.EqualTo(0.3).Within(0.001), "Сдвинутая доля обязана дойти до файла.");
            Assert.That(savedRoot?.Children[0].Weight, Is.EqualTo(0.6).Within(0.001), "Чужие доли правка одного узла не трогает.");
            Assert.That(saved.Tiles.Count(tile => tile.TypeId == "logs"), Is.EqualTo(1), "Запись чужого хоста из файла не исчезает.");
            Assert.That(saved.Tiles.Count(tile => tile.TypeId == "broadcast-status"), Is.EqualTo(1), "Скрытая запись остаётся в файле.");
            Assert.That(saved.Tiles.Count(tile => tile.TypeId == DashboardLayoutTree.EmptySlotTypeId), Is.Zero,
                "Зарезервированный тип дыры в Tiles попадать не должен.");
        });
    }

    [Test]
    public void A_removed_tile_returns_to_the_palette_and_stays_in_the_file()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.Remove(PathOf(section, "stream-info"));

        var layout = section.BuildLayout();

        Assert.Multiple(() =>
        {
            Assert.That(section.AvailablePalette.Select(meta => meta.TypeId), Does.Contain("stream-info"));
            Assert.That(layout.Tiles.Count(tile => tile.TypeId == "stream-info"), Is.EqualTo(1));
            Assert.That(layout.Tiles.Single(tile => tile.TypeId == "stream-info").IsVisible, Is.False,
                "Убранная плитка выключается, а не стирается из файла.");
        });
    }

    [Test]
    public void The_last_tile_cannot_be_removed()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.Remove(PathOf(section, "stream-info"));
        section.Remove(PathOf(section, "twitch-chat"));

        Assert.Multiple(() =>
        {
            Assert.That(section.Notice, Does.Contain("последнюю плитку"));
            Assert.That(Leaf(section.Pane, "twitch-chat"), Is.Not.Null);
        });
    }

    [Test]
    public void A_tile_from_the_palette_splits_the_target_pane()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.Add("broadcast-status", PathOf(section, "twitch-chat"), PaneSide.Bottom);

        Assert.Multiple(() =>
        {
            Assert.That(Leaf(section.Pane, "broadcast-status"), Is.Not.Null, "Брошенная плитка обязана появиться в дереве превью.");
            Assert.That(section.AvailablePalette.Select(meta => meta.TypeId), Does.Not.Contain("broadcast-status"));
            Assert.That(section.BuildLayout().Tiles.Single(tile => tile.TypeId == "broadcast-status").IsVisible, Is.True);
        });
    }

    [Test]
    public void Undo_returns_the_previous_tree()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.Add("broadcast-status", PathOf(section, "twitch-chat"), PaneSide.Bottom);
        section.UndoCommand.Execute(null);

        Assert.That(Leaf(section.Pane, "broadcast-status"), Is.Null, "Отмена обязана вернуть дерево к состоянию до броска.");
    }

    [Test]
    public void The_reference_decides_whether_the_preview_stacks()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 3,
            RowCount = 1,
            Root = new SplitPane(SplitOrientation.Columns, [
                new(new TilePane("stream-info"), 0.34),
                new(new TilePane("broadcast-status"), 0.33),
                new(new TilePane("twitch-chat"), 0.33),
            ]),
        };

        AddTile(layout, "stream-info", 0, 0);
        AddTile(layout, "broadcast-status", 0, 1);
        AddTile(layout, "twitch-chat", 0, 2);

        var section = new DashboardLayoutSectionViewModel([
            new FakeTile("stream-info", fills: true),
            new FakeTile("broadcast-status", fills: true),
            new FakeTile("twitch-chat", fills: true),
        ]);

        section.LoadSettings(layout);

        section.Reference = DashboardPreviewReference.Window1920;
        var wide = section.Stacked;

        section.Reference = DashboardPreviewReference.Window1024;

        Assert.Multiple(() =>
        {
            Assert.That(wide, Is.False, "На эталоне 1920 три колонки помещаются, стопки быть не должно.");
            Assert.That(section.Stacked, Is.True,
                "На эталоне 1024 сумма минимумов трёх колонок не влезает – дашборд там уходит в стопку, и превью обязано показать то же.");
            Assert.That(section.ContentArea.Width, Is.EqualTo(1024 - DashboardPreviewReference.NavWidthExpanded).Within(0.001));
        });
    }

    [Test]
    public void The_caption_names_the_reference_and_the_scale()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.Reference = DashboardPreviewReference.Window1920;
        section.Scale = 0.38;

        Assert.That(section.ReferenceCaption, Does.Contain("1920").And.Contains("38"));
    }

    [Test]
    public void A_layout_no_seam_cuts_says_so_before_the_first_attempt()
    {
        var section = CreatePinwheelSection();

        Assert.Multiple(() =>
        {
            Assert.That(section.Pane, Is.Null, "Вертушку не режет ни один сквозной шов – дерева у неё нет.");
            Assert.That(section.Bands, Is.Not.Empty, "Без дерева превью рисуется полосами.");
            Assert.That(section.CanEditTree, Is.False, "Палитра обязана быть погашена: бросок в такую раскладку невыразим.");
            Assert.That(section.Notice, Is.Not.Empty,
                "Пользователю говорят, почему правка недоступна, сразу, а не молчат до первой неудачной попытки.");
        });
    }

    [Test]
    public void One_more_column_opens_an_empty_cell_and_keeps_the_proportions_inside_the_columns()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnRowsLayout());
        section.ColumnCount = 3;

        var root = section.BuildLayout().Root as SplitPane;
        var column = root?.Children.Select(child => child.Pane).OfType<SplitPane>().FirstOrDefault();

        Assert.Multiple(() =>
        {
            Assert.That(section.ColumnCount, Is.EqualTo(3));
            Assert.That(Holes(section.Pane), Is.Not.Zero, "Новая колонка обязана быть видна пустой ячейкой, иначе положить в неё плитку некуда.");
            Assert.That(column?.Children[0].Weight, Is.EqualTo(0.7).Within(0.01),
                "Пропорции строк внутри уцелевшей колонки менять число колонок не должно.");
            Assert.That(section.Notice, Is.Empty, "Ничего не потерялось – тревожить пользователя нечем.");
        });
    }

    [Test]
    public void One_more_column_keeps_the_proportions_of_the_columns_that_survived()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.ColumnCount = 3;

        var root = section.BuildLayout().Root as SplitPane;

        Assert.That(root?.Children, Has.Count.EqualTo(3), "Прибавленная колонка обязана стать третьим ребёнком корня.");
        Assert.That(root!.Children[0].Weight / root.Children[1].Weight, Is.EqualTo(1.5).Within(0.05),
            "Разное число детей не повод сбросить доли колонок, которые остались на месте.");
    }

    [Test]
    public void A_slot_without_a_weight_does_not_cost_the_node_its_proportions()
    {
        var section = CreateSection();

        section.LoadSettings(LooseSlotLayout());
        section.ColumnCount = 3;

        var root = section.BuildLayout().Root as SplitPane;

        Assert.That(root?.Children[0].Weight, Is.EqualTo(0.6).Within(0.05),
            "Сосед по содержимому весa не несёт, и это не повод потерять долю взвешенной колонки.");
    }

    [Test]
    public void Undo_clears_the_notice_about_a_switched_off_tile()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.ColumnCount = 1;

        Assert.That(section.Notice, Is.Not.Empty, "Проверка холостая: о выключенной плитке не сказали.");

        section.UndoCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(section.BuildLayout().Tiles.All(static tile => tile.IsVisible), Is.True, "Отмена вернула плитку на панель.");
            Assert.That(section.Notice, Is.Empty, "Лента продолжает утверждать, что плитка выключена, хотя она вернулась.");
        });
    }

    [Test]
    public void A_tile_that_stops_fitting_the_grid_is_switched_off_and_named()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.ColumnCount = 1;

        var layout = section.BuildLayout();
        var hidden = layout.Tiles.Where(tile => !tile.IsVisible).Select(tile => tile.TypeId).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(hidden, Has.Count.EqualTo(1), "Одной из двух плиток в одной колонке одной строки места нет.");
            Assert.That(layout.Tiles, Has.Count.EqualTo(2), "Не поместившаяся плитка остаётся в файле, а не стирается из него.");
            Assert.That(section.Notice, Does.Contain(hidden[0]), "Пользователю говорят, какая плитка выключена.");
            Assert.That(section.AvailablePalette.Select(meta => meta.TypeId), Does.Contain(hidden[0]),
                "Выключенная плитка возвращается в палитру, иначе вернуть её нечем.");
        });
    }

    [TestCase(PaneSide.None, false)]
    [TestCase(PaneSide.Bottom, true)]
    public void A_tile_from_the_palette_lands_in_the_cell_the_new_column_opened(PaneSide side, bool holeRemains)
    {
        var section = new DashboardLayoutSectionViewModel([
            new FakeTile("stream-info"),
            new FakeTile("broadcast-status"),
            new FakeTile("twitch-chat", fills: true),
            new FakeTile("obs-info"),
        ]);

        section.LoadSettings(TwoColumnRowsLayout());
        section.ColumnCount = 3;

        var hole = HolePath(section.Pane);

        Assert.That(hole, Is.Not.Null, "Пустая ячейка обязана быть адресуемой – иначе бросок в неё невыразим.");

        var predicted = section.PreviewAdd("obs-info", hole!, side);

        section.Add("obs-info", hole!, side);

        Assert.Multiple(() =>
        {
            Assert.That(Leaf(section.Pane, "obs-info"), Is.Not.Null, "Плитка обязана встать в открывшуюся ячейку.");
            Assert.That(section.AvailablePalette.Select(meta => meta.TypeId), Does.Not.Contain("obs-info"));
            Assert.That(HolePath(section.Pane) is not null, Is.EqualTo(holeRemains),
                "Бросок в центр дыры занимает её целиком, бросок к краю по-прежнему её режет.");
            Assert.That(HolePath(predicted) is not null, Is.EqualTo(holeRemains),
                "Подсказка броска обязана обещать то же, что сделает сам бросок.");
        });
    }

    [TestCase(PaneSide.None, false)]
    [TestCase(PaneSide.Top, true)]
    public void A_tile_moved_into_the_centre_of_a_hole_takes_it_whole(PaneSide side, bool holeRemains)
    {
        var section = new DashboardLayoutSectionViewModel([
            new FakeTile("stream-info"),
            new FakeTile("broadcast-status"),
            new FakeTile("twitch-chat", fills: true),
        ]);

        section.LoadSettings(TwoColumnRowsLayout());
        section.ColumnCount = 3;

        var hole = HolePath(section.Pane);
        var source = Leaf(section.Pane, "broadcast-status")?.Path;

        Assert.That(hole, Is.Not.Null);
        Assert.That(source, Is.Not.Null);

        section.Move(source!, hole!, side);

        Assert.Multiple(() =>
        {
            Assert.That(Leaf(section.Pane, "broadcast-status"), Is.Not.Null);
            Assert.That(HolePath(section.Pane) is not null, Is.EqualTo(holeRemains),
                "Перенос в центр дыры не оставляет дыры ни на месте броска, ни на прежнем месте плитки.");
        });
    }

    [Test]
    public void A_column_with_nowhere_to_grow_is_named_instead_of_appearing_as_a_sliver()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.Reference = DashboardPreviewReference.Window1024;
        section.ColumnCount = 3;

        Assert.Multiple(() =>
        {
            Assert.That(section.Stacked, Is.False, "Проверяется именно узкий, а не стопочный случай.");
            Assert.That(section.Notice, Does.Contain("ширины"),
                "Пустая ячейка шириной в пару пикселей выглядит как «ничего не произошло» – это надо сказать словами.");
        });
    }

    [Test]
    public void A_stacked_preview_says_that_the_new_cell_is_not_shown_there()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 3,
            RowCount = 1,
            Root = new SplitPane(SplitOrientation.Columns, [
                new(new TilePane("stream-info"), 0.34),
                new(new TilePane("broadcast-status"), 0.33),
                new(new TilePane("twitch-chat"), 0.33),
            ]),
        };

        AddTile(layout, "stream-info", 0, 0);
        AddTile(layout, "broadcast-status", 0, 1);
        AddTile(layout, "twitch-chat", 0, 2);

        var section = new DashboardLayoutSectionViewModel([
            new FakeTile("stream-info", fills: true),
            new FakeTile("broadcast-status", fills: true),
            new FakeTile("twitch-chat", fills: true),
        ]);

        section.LoadSettings(layout);
        section.Reference = DashboardPreviewReference.Window1024;
        section.ColumnCount = 4;

        Assert.Multiple(() =>
        {
            Assert.That(section.Stacked, Is.True, "Три растягивающиеся колонки на эталоне 1024 не влезают полами – превью уходит в стопку.");
            Assert.That(section.Notice, Does.Contain("стопкой"),
                "В стопке пустых ячеек не видно – иначе ползунок выглядит сломанным.");
        });
    }

    [Test]
    public void Undo_returns_the_grid_size_together_with_the_tree()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.RowCount = 3;
        section.UndoCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(section.RowCount, Is.EqualTo(1), "Отмена обязана вернуть и число строк – правка сетки такая же правка, как разрез.");
            Assert.That(section.BuildLayout().RowCount, Is.EqualTo(1));
            Assert.That(Holes(section.Pane), Is.Zero, "Вместе с сеткой уходят и открытые ею пустые ячейки.");
        });
    }

    [Test]
    public void The_grid_size_survives_a_concurrent_write_of_the_dashboard()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.RowCount = 2;

        var layout = section.BuildLayout();

        Assert.That(section.TreeEdited, Is.True, "Смена сетки – правка дерева: иначе дерево с диска перебьёт её при сохранении.");

        DashboardLayoutReconciler.MergeConcurrentEdits(layout, TwoColumnLayout(), section.TreeEdited);

        Assert.That(layout.RowCount, Is.EqualTo(2), "Число строк принадлежит черновику настроек, а не файлу.");
    }

    private static DashboardLayoutSettings TwoColumnRowsLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 2,
            Root = new SplitPane(SplitOrientation.Columns, [
                new(new SplitPane(SplitOrientation.Rows, [
                    new(new TilePane("stream-info"), 0.7),
                    new(new TilePane("broadcast-status"), 0.3),
                ]), 0.6),
                new(new TilePane("twitch-chat"), 0.4),
            ]),
        };

        AddTile(layout, "stream-info", 0, 0);
        AddTile(layout, "broadcast-status", 1, 0);
        AddTile(layout, "twitch-chat", 0, 1, rowSpan: 2);

        return layout;
    }

    private static int[]? HolePath(PaneLayout? pane)
    {
        switch (pane)
        {
            case EmptyPaneLayout hole:
                return hole.Path;

            case SplitPaneLayout split:
                foreach (var child in split.Children)
                {
                    if (HolePath(child.Pane) is { } found)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private static int Holes(PaneLayout? pane)
    {
        return pane switch
        {
            EmptyPaneLayout => 1,
            SplitPaneLayout split => split.Children.Sum(child => Holes(child.Pane)),
            _ => 0,
        };
    }

    [Test]
    public void Changing_only_a_max_size_lets_a_newer_tree_from_disk_win()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());
        section.SetMaxWidth("stream-info", 420);

        var persisted = TwoColumnLayout();

        persisted.Root = new SplitPane(SplitOrientation.Columns, [
            new(new TilePane("stream-info"), 0.25),
            new(new TilePane("twitch-chat"), 0.75),
        ]);

        var layout = section.BuildLayout();

        Assert.That(section.TreeEdited, Is.False, "Максимальная ширина плитки дерева не трогает.");

        DashboardLayoutReconciler.MergeConcurrentEdits(layout, persisted, section.TreeEdited);

        Assert.That(((SplitPane)layout.Root!).Children[0].Weight, Is.EqualTo(0.25).Within(0.001),
            "Пропорции, записанные с дашборда, пока раздел был открыт, обязаны пережить сохранение настроек.");
    }

    [Test]
    public void An_edit_of_the_tree_wins_over_the_proportions_on_disk()
    {
        var section = CreateSection();

        section.LoadSettings(TwoColumnLayout());

        Assert.That(section.Resize([], [0.3, 0.7]), Is.True, "Сдвиг разделителя корня обязан пройти.");
        Assert.That(section.TreeEdited, Is.True, "Сдвиг разделителя – правка дерева.");

        var layout = section.BuildLayout();

        DashboardLayoutReconciler.MergeConcurrentEdits(layout, TwoColumnLayout(), section.TreeEdited);

        Assert.That(((SplitPane)layout.Root!).Children[0].Weight, Is.EqualTo(0.3).Within(0.001),
            "Своя правка пропорций в превью не должна теряться при сохранении настроек.");
    }
}
