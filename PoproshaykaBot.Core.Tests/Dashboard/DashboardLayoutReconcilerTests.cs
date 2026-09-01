using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Settings.Ui;

namespace PoproshaykaBot.Core.Tests.Dashboard;

[TestFixture]
public sealed class DashboardLayoutReconcilerTests
{
    private static readonly string[] Catalog = ["stream-info", "polls-control", "obs-info"];

    [Test]
    public void AppendMissingTypes_AddsCatalogTypeAbsentFromFileAsHidden()
    {
        var layout = LayoutWith(Tile("stream-info", order: 0, isVisible: true));

        var appended = DashboardLayoutReconciler.AppendMissingTypes(layout, Catalog);

        var polls = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "polls-control", StringComparison.Ordinal));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(appended, Is.True);
            Assert.That(layout.Tiles, Has.Count.EqualTo(3));
            Assert.That(polls.IsVisible, Is.False, "Отсутствующая в файле плитка не должна появляться на дашборде сама.");
            Assert.That(polls.Id, Is.EqualTo("polls-control"));
            Assert.That(layout.Tiles.Select(tile => tile.Order), Is.Unique);
        }
    }

    [Test]
    public void AppendMissingTypes_SecondRun_ChangesNothing()
    {
        var layout = LayoutWith(Tile("stream-info", order: 0, isVisible: true));

        DashboardLayoutReconciler.AppendMissingTypes(layout, Catalog);

        var snapshot = layout.Tiles.Select(tile => (tile.TypeId, tile.Order, tile.IsVisible)).ToList();

        var appended = DashboardLayoutReconciler.AppendMissingTypes(layout, Catalog);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(appended, Is.False, "Повторная загрузка не должна дописывать те же плитки второй раз.");
            Assert.That(layout.Tiles.Select(tile => (tile.TypeId, tile.Order, tile.IsVisible)), Is.EqualTo(snapshot));
        }
    }

    [Test]
    public void AppendMissingTypes_KeepsForeignRecordAndItsOrder()
    {
        var layout = LayoutWith(
            Tile("stream-info", order: 4, isVisible: true),
            Tile("logs", order: 7, isVisible: true));

        DashboardLayoutReconciler.AppendMissingTypes(layout, Catalog);

        var logs = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "logs", StringComparison.Ordinal));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(logs.Order, Is.EqualTo(7), "Чужая запись не должна перенумеровываться.");
            Assert.That(logs.IsVisible, Is.True);
            Assert.That(layout.Tiles.Single(tile => string.Equals(tile.TypeId, "stream-info", StringComparison.Ordinal)).Order, Is.EqualTo(4));
            Assert.That(layout.Tiles.Where(tile => !tile.IsVisible).Select(tile => tile.Order), Is.EquivalentTo(new[] { 8, 9 }));
        }
    }

    [Test]
    public void AppendPreserved_SameTypeTwiceInPreserved_IsWrittenOnce()
    {
        var layout = LayoutWith(Tile("stream-info", order: 0, isVisible: true));

        DashboardLayoutReconciler.AppendPreserved(layout,
        [
            Tile("logs", order: 0, isVisible: false),
            Tile("logs", order: 1, isVisible: true),
        ]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(layout.Tiles.Count(tile => string.Equals(tile.TypeId, "logs", StringComparison.Ordinal)), Is.EqualTo(1),
                "Задвоенная запись из файла не должна размножаться при каждом сохранении.");
            Assert.That(layout.Tiles.Select(tile => tile.Order), Is.Unique);
        }
    }

    [Test]
    public void ResetToDefaults_KeepsForeignAndHiddenRecordsFromDisk()
    {
        var persisted = LayoutWith(
            Tile("stream-info", order: 0, isVisible: false),
            Tile("logs", order: 1, isVisible: true),
            Tile("chat-blockers", order: 2, isVisible: false));

        persisted.Tiles[1].Row = 2;
        persisted.Tiles[1].Column = 1;

        var layout = DashboardLayoutReconciler.ResetToDefaults(Defaults(), persisted);

        var logs = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "logs", StringComparison.Ordinal));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(logs.Row, Is.EqualTo(2), "Сброс настроек не должен трогать раскладку чужого хоста.");
            Assert.That(logs.Column, Is.EqualTo(1));
            Assert.That(logs.IsVisible, Is.True);
            Assert.That(layout.Tiles.Single(tile => string.Equals(tile.TypeId, "chat-blockers", StringComparison.Ordinal)).IsVisible, Is.False);
            Assert.That(layout.Tiles.Single(tile => string.Equals(tile.TypeId, "stream-info", StringComparison.Ordinal)).IsVisible, Is.True,
                "Известная плитка возвращается к дефолту, а не остаётся выключенной.");
            Assert.That(layout.Tiles.Select(tile => tile.TypeId), Is.Unique);
            Assert.That(layout.Tiles.Select(tile => tile.Order), Is.Unique);
        }
    }

    [Test]
    public void ResetToDefaults_DoesNotShareTilesWithItsArguments()
    {
        var defaults = Defaults();
        var persisted = LayoutWith(Tile("logs", order: 0, isVisible: true));

        var layout = DashboardLayoutReconciler.ResetToDefaults(defaults, persisted);

        foreach (var tile in layout.Tiles)
        {
            tile.Row = 7;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(defaults.Tiles.Select(tile => tile.Row), Has.None.EqualTo(7));
            Assert.That(persisted.Tiles.Select(tile => tile.Row), Has.None.EqualTo(7));
        }
    }

    [Test]
    public void ResetToDefaults_NoLayoutOnDisk_ReturnsDefaults()
    {
        var layout = DashboardLayoutReconciler.ResetToDefaults(Defaults(), null);

        Assert.That(layout.Tiles.Select(tile => tile.TypeId), Is.EqualTo(new[] { "stream-info", "polls-control" }));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void MergeCollapseState_TakesCollapseFromDisk(bool collapsedOnDisk)
    {
        var draft = LayoutWith(Tile("stream-info", order: 0, isVisible: true));
        draft.Tiles[0].IsCollapsed = !collapsedOnDisk;

        var persisted = LayoutWith(Tile("stream-info", order: 0, isVisible: true));
        persisted.Tiles[0].IsCollapsed = collapsedOnDisk;

        DashboardLayoutReconciler.MergeCollapseState(draft, persisted);

        Assert.That(draft.Tiles[0].IsCollapsed, Is.EqualTo(collapsedOnDisk),
            "Свёртка плитки, сделанная на дашборде, не должна откатываться устаревшим черновиком настроек.");
    }

    [Test]
    public void MergeCollapseState_LeavesTilesMissingOnDiskAlone()
    {
        var draft = LayoutWith(Tile("stream-info", order: 0, isVisible: true));
        draft.Tiles[0].IsCollapsed = true;

        DashboardLayoutReconciler.MergeCollapseState(draft, LayoutWith(Tile("logs", order: 0, isVisible: true)));
        DashboardLayoutReconciler.MergeCollapseState(draft, null);

        Assert.That(draft.Tiles[0].IsCollapsed, Is.True);
    }

    private static DashboardLayoutSettings Defaults()
    {
        return LayoutWith(
            Tile("stream-info", order: 0, isVisible: true),
            Tile("polls-control", order: 1, isVisible: true));
    }

    private static DashboardLayoutSettings LayoutWith(params DashboardTileSettings[] tiles)
    {
        return new()
        {
            ColumnCount = 3,
            RowCount = 3,
            Tiles = [.. tiles],
        };
    }

    private static DashboardTileSettings Tile(string typeId, int order, bool isVisible)
    {
        return new()
        {
            Id = typeId,
            TypeId = typeId,
            Order = order,
            IsVisible = isVisible,
        };
    }
}
