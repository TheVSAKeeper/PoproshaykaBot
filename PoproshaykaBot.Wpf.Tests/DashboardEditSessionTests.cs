using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class DashboardEditSessionTests
{
    [Test]
    public void A_resize_that_keeps_the_projection_still_reaches_the_file()
    {
        var store = new FakeLayoutStore(SideBySide());
        var time = new ManualTimeProvider();

        using var session = new DashboardEditSession(new(store), time);

        Assert.That(session.Resize([], [0.55, 0.45]), Is.True);

        session.Flush();

        var weights = Weights(store.Saved);

        Assert.Multiple(() =>
        {
            Assert.That(weights[0], Is.EqualTo(0.55).Within(0.001),
                "Сдвиг разделителя не меняет клеток проекции, и без владения деревом его затирало бы содержимым файла.");
            Assert.That(store.Saved!.Tiles.Select(tile => tile.Column), Is.EqualTo(new[] { 0, 2 }),
                "Клетки при этом остаются прежними – проекция округляет доли до сетки.");
        });
    }

    [Test]
    public void The_write_waits_for_the_debounce_and_happens_once()
    {
        var store = new FakeLayoutStore(SideBySide());
        var time = new ManualTimeProvider();

        using var session = new DashboardEditSession(new(store), time);

        session.Resize([], [0.6, 0.4]);
        session.Resize([], [0.7, 0.3]);

        Assert.That(store.SaveCount, Is.Zero, "До истечения задержки правка в файл не уходит.");

        time.Advance(DashboardEditSession.WriteDelay);

        Assert.Multiple(() =>
        {
            Assert.That(store.SaveCount, Is.EqualTo(1), "Две правки подряд дают одну запись, а не две.");
            Assert.That(Weights(store.Saved)[0], Is.EqualTo(0.7).Within(0.001));
        });
    }

    [Test]
    public void Undo_returns_the_previous_layout_whole()
    {
        var store = new FakeLayoutStore(SideBySide());
        var time = new ManualTimeProvider();

        using var session = new DashboardEditSession(new(store), time);

        session.Remove("twitch-chat");

        Assert.That(session.Draft.Tiles.Single(tile => tile.TypeId == "twitch-chat").IsVisible, Is.False,
            "Удаление гасит запись, а не выбрасывает её – иначе набор типов перестаёт быть монотонным.");

        Assert.That(session.Undo(), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(session.Draft.Tiles.Single(tile => tile.TypeId == "twitch-chat").IsVisible, Is.True);
            Assert.That(session.CanUndo, Is.False);
        });
    }

    [Test]
    public void A_write_from_another_editor_replaces_the_draft()
    {
        var store = new FakeLayoutStore(SideBySide());
        var time = new ManualTimeProvider();
        var coordinator = new DashboardLayoutCoordinator(store);

        using var session = new DashboardEditSession(coordinator, time);

        session.Resize([], [0.6, 0.4]);

        coordinator.Mutate(layout =>
        {
            layout!.Tiles.Single(tile => tile.TypeId == "stream-info").IsCollapsed = true;

            return layout;
        });

        Assert.Multiple(() =>
        {
            Assert.That(session.Draft.Tiles.Single(tile => tile.TypeId == "stream-info").IsCollapsed, Is.True,
                "Чужая запись обязана заменить черновик, иначе следующий дебаунс воскресит устаревшую раскладку.");
            Assert.That(session.CanUndo, Is.False, "Стек отмены после чужой записи ведёт в снятое состояние – его чистят.");
        });

        time.Advance(DashboardEditSession.WriteDelay);

        Assert.That(store.SaveCount, Is.EqualTo(1), "Отложенная запись снята вместе с черновиком, лишней записи нет.");
    }

    [Test]
    public void A_split_past_the_grid_ceiling_is_refused()
    {
        var layout = SideBySide();
        var session = default(DashboardEditSession);

        try
        {
            session = new(new(new FakeLayoutStore(layout)), new ManualTimeProvider());

            var added = new[] { "broadcast-status", "broadcast-profiles", "polls-control", "obs-info", "chat-overlay-preview", "stream-history" };
            var target = "twitch-chat";

            foreach (var typeId in added)
            {
                Assert.That(session.Add(typeId, target, PaneSide.Right), Is.EqualTo(DashboardEditStatus.Applied), $"Плитка {typeId} ещё помещается в восемь колонок.");
                target = typeId;
            }

            Assert.That(session.Add("chat-overlay-preview", target, PaneSide.Right), Is.EqualTo(DashboardEditStatus.Rejected),
                "Плитка, уже стоящая в дереве, вторым листом не становится, и упор тут не в сетку.");
            Assert.That(session.Add("audience-tracker", target, PaneSide.Right), Is.EqualTo(DashboardEditStatus.GridFull),
                "Девятая колонка пробила бы потолок сетки, а проекция при чтении зажимается до восьми.");
        }
        finally
        {
            session?.Dispose();
        }
    }

    [TestCase(false, false, Description = "Добавление в черновик без дерева разрезов")]
    [TestCase(true, false, Description = "Добавление в закрытую сессию")]
    [TestCase(false, true, Description = "Перенос в черновике без дерева разрезов")]
    [TestCase(true, true, Description = "Перенос в закрытой сессии")]
    public void An_edit_without_a_live_tree_is_not_reported_as_a_failed_split(bool disposeFirst, bool byMove)
    {
        var layout = SideBySide();

        if (!disposeFirst)
        {
            layout.Root = null;
        }

        var session = new DashboardEditSession(new(new FakeLayoutStore(layout)), new ManualTimeProvider());

        if (disposeFirst)
        {
            session.Dispose();
        }

        var status = byMove
            ? session.Split("stream-info", "twitch-chat", PaneSide.Bottom)
            : session.Add("audience-tracker", "twitch-chat", PaneSide.Right);

        if (!disposeFirst)
        {
            session.Dispose();
        }

        Assert.That(status, Is.EqualTo(DashboardEditStatus.Unavailable),
            "Разреза здесь не было вовсе, и валить отказ на раскладку – значит звать пользователя чинить то, что цело.");
    }

    [TestCase(false, Description = "Девятая строка от новой плитки")]
    [TestCase(true, Description = "Девятая строка от переноса соседней плитки")]
    public void A_ninth_row_is_refused_as_the_grid_ceiling(bool byMove)
    {
        var session = new DashboardEditSession(new(new FakeLayoutStore(SideBySide())), new ManualTimeProvider());

        try
        {
            var bottom = "stream-info";

            foreach (var typeId in new[] { "audience-tracker", "broadcast-status", "broadcast-profiles", "polls-control", "obs-info", "chat-overlay-preview", "stream-history" })
            {
                Assert.That(session.Add(typeId, bottom, PaneSide.Bottom), Is.EqualTo(DashboardEditStatus.Applied),
                    $"Плитка {typeId} ещё помещается в восемь строк.");

                bottom = typeId;
            }

            var status = byMove
                ? session.Split("twitch-chat", bottom, PaneSide.Bottom)
                : session.Add("logs", bottom, PaneSide.Bottom);

            Assert.That(status, Is.EqualTo(DashboardEditStatus.GridFull),
                "Девятая строка упирается в тот же потолок, что и девятая колонка, и на перетаскивании он назван так же.");
        }
        finally
        {
            session.Dispose();
        }
    }

    [TestCase("stream-info", Description = "Плитку переносят саму на себя")]
    [TestCase("audience-tracker", Description = "Целевой плитки в дереве нет")]
    public void A_move_that_the_tree_refuses_is_not_reported_as_a_full_grid(string target)
    {
        var store = new FakeLayoutStore(SideBySide());

        using var session = new DashboardEditSession(new(store), new ManualTimeProvider());

        Assert.That(session.Split("stream-info", target, PaneSide.Bottom), Is.EqualTo(DashboardEditStatus.Rejected),
            "Сетка здесь свободна, и звать пользователя освобождать место было бы ложью.");
    }

    [Test]
    public void Removing_the_last_leaf_is_refused()
    {
        var layout = SideBySide();

        layout.Root = new TilePane("stream-info");
        layout.Tiles.Single(tile => tile.TypeId == "twitch-chat").IsVisible = false;

        var store = new FakeLayoutStore(layout);

        using var session = new DashboardEditSession(new(store), new ManualTimeProvider());

        Assert.Multiple(() =>
        {
            Assert.That(session.Remove("stream-info"), Is.EqualTo(DashboardRemoveStatus.LastTile),
                "Пустая панель закрывает сам режим правки, и вернуть плитку оттуда уже нечем – последний лист не удаляется.");
            Assert.That(session.Draft.Tiles.Single(tile => tile.TypeId == "stream-info").IsVisible, Is.True);
        });
    }

    [Test]
    public void A_refused_operation_leaves_the_draft_untouched()
    {
        var store = new FakeLayoutStore(SideBySide());
        var time = new ManualTimeProvider();

        using var session = new DashboardEditSession(new(store), time);

        var before = session.Draft.Tiles.Count;

        Assert.That(session.Add("audience-tracker", "twitch-chat", PaneSide.Right), Is.EqualTo(DashboardEditStatus.Applied));

        var target = "audience-tracker";

        foreach (var typeId in new[] { "broadcast-status", "broadcast-profiles", "polls-control", "obs-info", "chat-overlay-preview" })
        {
            Assert.That(session.Add(typeId, target, PaneSide.Right), Is.EqualTo(DashboardEditStatus.Applied));
            target = typeId;
        }

        var placed = session.Draft.Tiles.Count;

        Assert.That(session.Add("stream-history", target, PaneSide.Right), Is.EqualTo(DashboardEditStatus.GridFull), "Девятая колонка пробила бы потолок сетки.");

        Assert.Multiple(() =>
        {
            Assert.That(session.Draft.Tiles, Has.Count.EqualTo(placed),
                "Отказ обязан откатить и запись, которую операция завела для новой плитки, иначе черновик остаётся наполовину применённым.");
            Assert.That(placed, Is.GreaterThan(before));
        });
    }

    [Test]
    public void The_edit_entry_is_offered_for_a_tree_layout()
    {
        using var dashboard = new DashboardViewModel(
            [new FakeTile("stream-info"), new FakeTile("twitch-chat", fills: true)],
            new(new FakeLayoutStore(SideBySide())),
            TimeProvider.System);

        Assert.Multiple(() =>
        {
            Assert.That(dashboard.Pane, Is.Not.Null);
            Assert.That(dashboard.CanEdit, Is.True, "Дерево есть и стопки нет – кнопка входа в режим правки обязана быть доступна.");
            Assert.That(dashboard.IsEditing, Is.False);
        });

        dashboard.ToggleEditCommand.Execute(null);

        Assert.That(dashboard.IsEditing, Is.True);

        dashboard.SetStacked(true);

        Assert.Multiple(() =>
        {
            Assert.That(dashboard.IsEditing, Is.False, "Стопка закрывает режим: долей там нет.");
            Assert.That(dashboard.CanEdit, Is.False);
        });
    }

    [Test]
    public void A_resize_next_to_a_hole_reaches_the_file_and_keeps_the_hole()
    {
        var store = new FakeLayoutStore(ColumnWithAHole());
        var time = new ManualTimeProvider();

        using var session = new DashboardEditSession(new(store), time);

        Assert.That(session.Resize([], [0.75, 0.25]), Is.True, "Дыра – законный лист, и узел с ней поддаётся разделителю.");

        session.Flush();

        Assert.Multiple(() =>
        {
            Assert.That(Weights(store.Saved)[0], Is.EqualTo(0.75).Within(0.001));
            Assert.That(store.Saved!.Tiles.Where(tile => tile.IsVisible).Select(tile => tile.TypeId),
                Is.EqualTo(new[] { "stream-info" }), "Пустышка не заводит себе записи в Tiles.");
            Assert.That(store.Saved.Tiles.Single().RowSpan, Is.EqualTo(3), "Доля 0.75 от четырёх строк отдаёт плитке три, дыре – одну.");
        });
    }

    private static DashboardLayoutSettings ColumnWithAHole()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 1,
            RowCount = 4,
            Root = new SplitPane(SplitOrientation.Rows,
            [
                new(new TilePane("stream-info"), 0.5),
                new(new TilePane(DashboardLayoutTree.EmptySlotTypeId), 0.5),
            ]),
        };

        layout.Tiles.Add(new() { Id = "stream-info", TypeId = "stream-info", Order = 0, Row = 0, Column = 0, ColumnSpan = 1, RowSpan = 2, IsVisible = true });

        return layout;
    }

    private static DashboardLayoutSettings SideBySide()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 4,
            RowCount = 1,
            Root = new SplitPane(SplitOrientation.Columns,
            [
                new(new TilePane("stream-info"), 0.5),
                new(new TilePane("twitch-chat"), 0.5),
            ]),
        };

        layout.Tiles.Add(new() { Id = "stream-info", TypeId = "stream-info", Order = 0, Row = 0, Column = 0, ColumnSpan = 2, RowSpan = 1, IsVisible = true });
        layout.Tiles.Add(new() { Id = "twitch-chat", TypeId = "twitch-chat", Order = 1, Row = 0, Column = 2, ColumnSpan = 2, RowSpan = 1, IsVisible = true });

        return layout;
    }

    private static double?[] Weights(DashboardLayoutSettings? layout)
    {
        return layout?.Root is SplitPane split
            ? split.Children.Select(slot => slot.Weight).ToArray()
            : [];
    }
}
