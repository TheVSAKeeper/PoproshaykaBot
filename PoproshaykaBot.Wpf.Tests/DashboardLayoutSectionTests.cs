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
}
