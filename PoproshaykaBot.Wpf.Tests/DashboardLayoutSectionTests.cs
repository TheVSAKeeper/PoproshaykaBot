using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class DashboardLayoutSectionTests
{
    private sealed class FakeTile(string typeId) : DashboardTileViewModel(typeId, typeId);

    private static DashboardLayoutSectionViewModel CreateSection()
    {
        return new([new FakeTile("stream-info"), new FakeTile("broadcast-status")]);
    }

    private static DashboardLayoutSettings SingleCellLayout()
    {
        return new()
        {
            ColumnCount = 1,
            RowCount = 1,
            Tiles =
            [
                new() { Id = "stream-info", TypeId = "stream-info", Row = 0, Column = 0, ColumnSpan = 1, RowSpan = 1, IsVisible = true },
                new() { Id = "broadcast-status", TypeId = "broadcast-status", Row = 0, Column = 1, ColumnSpan = 1, RowSpan = 1, IsVisible = true },
            ],
        };
    }

    [Test]
    public void Keeps_tiles_unknown_to_this_host()
    {
        var section = CreateSection();

        section.LoadSettings(new()
        {
            ColumnCount = 4,
            RowCount = 3,
            Tiles =
            [
                new() { Id = "stream-info", TypeId = "stream-info", Row = 0, Column = 0, ColumnSpan = 1, RowSpan = 1, IsVisible = true },
                new() { Id = "logs", TypeId = "logs", Row = 1, Column = 2, ColumnSpan = 2, RowSpan = 1, IsVisible = true },
            ],
        });

        var layout = section.BuildLayout();
        var logs = layout.Tiles.SingleOrDefault(tile => tile.TypeId == "logs");

        Assert.That(logs, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(logs!.Row, Is.EqualTo(1));
            Assert.That(logs.Column, Is.EqualTo(2));
            Assert.That(logs.ColumnSpan, Is.EqualTo(2));
        });
    }

    [Test]
    public void Keeps_hidden_tiles()
    {
        var section = CreateSection();

        section.LoadSettings(new()
        {
            Tiles =
            [
                new() { Id = "stream-info", TypeId = "stream-info", IsVisible = true },
                new() { Id = "broadcast-status", TypeId = "broadcast-status", IsVisible = false },
            ],
        });

        var layout = section.BuildLayout();

        Assert.That(layout.Tiles.Count(tile => tile.TypeId == "broadcast-status"), Is.EqualTo(1));
    }

    [Test]
    public void Tile_that_no_longer_fits_the_grid_is_kept_hidden()
    {
        var section = CreateSection();

        section.LoadSettings(SingleCellLayout());

        var layout = section.BuildLayout();
        var lost = layout.Tiles.Where(tile => tile.TypeId == "broadcast-status").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(lost, Has.Count.EqualTo(1), "Не поместившаяся плитка должна остаться в файле ровно один раз.");
            Assert.That(lost[0].IsVisible, Is.False, "Не поместившуюся плитку выключают, а не стирают из файла.");
            Assert.That(layout.Tiles.Count(tile => tile.IsVisible), Is.EqualTo(1));
            Assert.That(section.Notice, Does.Contain("broadcast-status"), "Пользователю нужно сказать, какую плитку выключили.");
            Assert.That(section.AvailablePalette.Select(meta => meta.TypeId), Does.Contain("broadcast-status"));
        });
    }

    [Test]
    public void Tile_hidden_by_a_shrunk_grid_survives_a_round_trip()
    {
        var section = CreateSection();

        section.LoadSettings(SingleCellLayout());
        section.LoadSettings(section.BuildLayout());

        var reloaded = section.BuildLayout().Tiles.Where(tile => tile.TypeId == "broadcast-status").ToList();

        Assert.Multiple(() =>
        {
            Assert.That(reloaded, Has.Count.EqualTo(1));
            Assert.That(reloaded[0].IsVisible, Is.False);
            Assert.That(section.Notice, Is.Empty, "Повторная загрузка ничего не выключает, значит и предупреждать не о чем.");
        });
    }

    [Test]
    public void Placing_a_hidden_tile_does_not_duplicate_it()
    {
        var section = CreateSection();

        section.LoadSettings(new()
        {
            Tiles =
            [
                new() { Id = "broadcast-status", TypeId = "broadcast-status", IsVisible = false },
            ],
        });

        section.PlaceOrMove("broadcast-status", 0, 0);

        var layout = section.BuildLayout();
        var tiles = layout.Tiles.Where(tile => tile.TypeId == "broadcast-status").ToList();

        Assert.That(tiles, Has.Count.EqualTo(1));
        Assert.That(tiles[0].IsVisible, Is.True);
    }

    [Test]
    public void The_same_type_twice_in_the_file_is_written_back_once()
    {
        var section = CreateSection();

        section.LoadSettings(new()
        {
            Tiles =
            [
                new() { Id = "logs", TypeId = "logs", Row = 1, Column = 2, ColumnSpan = 1, RowSpan = 1, IsVisible = true },
                new() { Id = "logs", TypeId = "logs", Row = 2, Column = 0, ColumnSpan = 1, RowSpan = 1, IsVisible = false },
            ],
        });

        var layout = section.BuildLayout();

        Assert.Multiple(() =>
        {
            Assert.That(layout.Tiles.Count(tile => string.Equals(tile.TypeId, "logs", StringComparison.Ordinal)), Is.EqualTo(1),
                "Задвоенная запись из файла не должна размножаться при каждом сохранении.");
            Assert.That(layout.Tiles.Select(tile => tile.Order), Is.Unique);
        });
    }
}
