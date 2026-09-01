using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.WinForms.Forms.Settings;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Tiles;

namespace PoproshaykaBot.Core.Tests.Settings;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class DashboardLayoutEditorContractTests
{
    private const string KnownTypeId = "stream-info";
    private const string SecondKnownTypeId = "broadcast-status";
    private const string ForeignTypeId = "logs";

    [SetUp]
    public void SetUp()
    {
        _types = [new FakeTileType(KnownTypeId), new FakeTileType(SecondKnownTypeId)];

        var catalog = Substitute.For<IDashboardTileCatalog>();
        catalog.All.Returns(_types);
        catalog.Find(Arg.Any<string?>())
            .Returns(call => _types.FirstOrDefault(type => string.Equals(type.Id, call.Arg<string?>(), StringComparison.Ordinal)));

        _control = new()
        {
            TileCatalog = catalog,
        };
    }

    [TearDown]
    public void TearDown()
    {
        _control.Dispose();
    }

    private DashboardSettingsControl _control = null!;
    private DashboardTileType[] _types = null!;

    [Test]
    public void SaveSettings_TileUnknownToThisHost_IsPreserved()
    {
        Load(
            Tile(KnownTypeId, 0, 0),
            Tile(ForeignTypeId, 1, 2, columnSpan: 2));

        var foreign = _control.SaveSettings().Tiles.SingleOrDefault(tile => string.Equals(tile.TypeId, ForeignTypeId, StringComparison.Ordinal));

        Assert.That(foreign, Is.Not.Null, "Плитка чужого хоста должна пережить сохранение из настроек WinForms.");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(foreign!.Row, Is.EqualTo(1));
            Assert.That(foreign.Column, Is.EqualTo(2));
            Assert.That(foreign.ColumnSpan, Is.EqualTo(2));
            Assert.That(foreign.IsVisible, Is.True);
        }
    }

    [Test]
    public void SaveSettings_HiddenTile_IsPreserved()
    {
        var hidden = Tile(SecondKnownTypeId, 1, 0);
        hidden.IsVisible = false;

        Load(Tile(KnownTypeId, 0, 0), hidden);

        var saved = _control.SaveSettings();
        var restored = saved.Tiles.Where(tile => string.Equals(tile.TypeId, SecondKnownTypeId, StringComparison.Ordinal)).ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restored, Has.Count.EqualTo(1), "Скрытая плитка должна остаться в файле ровно один раз.");
            Assert.That(restored[0].IsVisible, Is.False, "Сохранение не должно втихую показывать скрытую плитку.");
        }
    }

    [Test]
    public void SaveSettings_RepeatedSave_KeepsForeignTileOnce()
    {
        Load(Tile(KnownTypeId, 0, 0), Tile(ForeignTypeId, 1, 1));

        _control.SaveSettings();
        var saved = _control.SaveSettings();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.Tiles.Count(tile => string.Equals(tile.TypeId, ForeignTypeId, StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(saved.Tiles.Select(tile => tile.Order), Is.Unique, "Порядок плиток не должен задваиваться после повторного сохранения.");
        }
    }

    [Test]
    public void SaveSettings_AfterTileRemoved_DoesNotRenumberEarlierResult()
    {
        Load(Tile(KnownTypeId, 0, 0), Tile(SecondKnownTypeId, 1, 0), Tile(ForeignTypeId, 2, 1));

        var first = _control.SaveSettings();
        var foreignOrder = first.Tiles.Single(tile => string.Equals(tile.TypeId, ForeignTypeId, StringComparison.Ordinal)).Order;

        ((IDashboardTileCommands)_control).RemoveTile(_types[0]);
        _control.SaveSettings();

        Assert.That(first.Tiles.Single(tile => string.Equals(tile.TypeId, ForeignTypeId, StringComparison.Ordinal)).Order,
            Is.EqualTo(foreignOrder),
            "Повторное сохранение не должно задним числом перенумеровывать раскладку, которую вызывающий уже получил.");
    }

    private static DashboardTileSettings Tile(string typeId, int row, int column, int columnSpan = 1)
    {
        return new()
        {
            Id = typeId,
            TypeId = typeId,
            Row = row,
            Column = column,
            ColumnSpan = columnSpan,
            RowSpan = 1,
            IsVisible = true,
        };
    }

    private void Load(params DashboardTileSettings[] tiles)
    {
        _control.LoadSettings(new()
        {
            ColumnCount = 4,
            RowCount = 3,
            Tiles = [.. tiles],
        });

        _control.CreateControl();

        Assert.That(_control.IsHandleCreated, Is.True, "Без инициализации контрол вернёт загруженный черновик, и контракт останется непроверенным.");
    }

    private sealed class FakeTileType(string typeId) : DashboardTileType(typeId, typeId)
    {
        public override Control CreateBody(IControlFactory factory)
        {
            return new();
        }
    }
}
