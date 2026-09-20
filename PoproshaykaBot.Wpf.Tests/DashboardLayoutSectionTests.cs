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
