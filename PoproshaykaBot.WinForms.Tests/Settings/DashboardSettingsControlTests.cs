using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Tiles;

namespace PoproshaykaBot.WinForms.Tests.Settings;

[TestFixture]
public sealed class DashboardSettingsControlTests
{
    [SetUp]
    public void SetUp()
    {
        _catalog = Substitute.For<IDashboardTileCatalog>();
        _catalog.All.Returns([]);
        _control = new()
        {
            TileCatalog = _catalog,
        };
    }

    [TearDown]
    public void TearDown()
    {
        _control.Dispose();
    }

    private IDashboardTileCatalog _catalog = null!;
    private DashboardSettingsControl _control = null!;

    [Test]
    public void SaveSettings_NotInitialized_PreservesPendingLayout()
    {
        var initialLayout = new DashboardLayoutSettings
        {
            ColumnCount = 5,
            RowCount = 4,
            Tiles =
            [
                new()
                {
                    Id = "chat",
                    TypeId = "chat",
                    Row = 0,
                    Column = 0,
                    IsVisible = true,
                },
                new()
                {
                    Id = "stream",
                    TypeId = "stream",
                    Row = 1,
                    Column = 2,
                    ColumnSpan = 2,
                    IsVisible = true,
                },
            ],
        };

        var ui = new UiSettings { Dashboard = initialLayout };

        _control.LoadSettings(ui);

        var target = new UiSettings { Dashboard = new() { ColumnCount = 99 } };
        _control.SaveSettings(target);

        Assert.That(target.Dashboard, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Dashboard!.Tiles, Has.Count.EqualTo(2),
                "Контрол не открывался — должен сохранить layout, который мы загрузили, а не пустоту.");

            Assert.That(target.Dashboard.ColumnCount, Is.EqualTo(5));
            Assert.That(target.Dashboard.RowCount, Is.EqualTo(4));
        }
    }

    [Test]
    public void SaveSettings_NotInitialized_NullDashboard_LeavesTargetUntouched()
    {
        var ui = new UiSettings { Dashboard = null };

        _control.LoadSettings(ui);

        var target = new UiSettings();
        _control.SaveSettings(target);

        Assert.That(target.Dashboard, Is.Not.Null);
        Assert.That(target.Dashboard!.Tiles, Is.Not.Empty,
            "Если LoadSettings подсунул defaults, SaveSettings без инициализации должен их сохранить.");
    }
}
