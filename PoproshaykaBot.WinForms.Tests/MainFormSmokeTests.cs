namespace PoproshaykaBot.WinForms.Tests;

[TestFixture]
[NonParallelizable]
public class MainFormSmokeTests
{
    [SetUp]
    public void Setup()
    {
        _session = SmokeTestSession.Launch(new() { SeedConfiguredSettings = true });
    }

    [TearDown]
    public void TearDown()
    {
        _session?.Dispose();
        _session = null;
    }

    private const string ExpectedTitlePrefix = "Попрощайка Бот";

    private SmokeTestSession? _session;

    [Test]
    public void MainWindow_ShouldLaunch()
    {
        Assert.That(_session!.MainWindow.IsAvailable, Is.True, "Главное окно должно быть доступно после запуска");
    }

    [Test]
    public void MainWindow_ShouldHaveBotTitle()
    {
        var title = _session!.MainWindow.Title;
        Assert.That(title, Does.StartWith(ExpectedTitlePrefix),
            $"Заголовок окна должен начинаться с '{ExpectedTitlePrefix}', получено: '{title}'");
    }

    [Test]
    public void MainWindow_ShouldBeResizable()
    {
        Assert.That(_session!.MainWindow.Patterns.Transform.IsSupported, Is.True,
            "Главное окно должно поддерживать Transform pattern (resize/move)");
    }

    [Test]
    public void MainWindow_ShouldHaveNonZeroBounds()
    {
        var bounds = _session!.MainWindow.BoundingRectangle;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bounds.Width, Is.GreaterThan(0), "Ширина окна должна быть положительной");
            Assert.That(bounds.Height, Is.GreaterThan(0), "Высота окна должна быть положительной");
        }
    }

    [Test]
    public void MainWindow_ShouldShowDashboard_WithDefaultTiles()
    {
        var mainWindow = _session!.MainWindow;

        var dashboard = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("_dashboardControl"));
        var streamTile = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("_stream-infoTile"));
        var broadcastTile = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("_broadcast-statusTile"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dashboard, Is.Not.Null, "Dashboard должен быть в дереве UI");
            Assert.That(streamTile, Is.Not.Null, "Тайл стрима должен быть в дереве UI");
            Assert.That(broadcastTile, Is.Not.Null, "Тайл рассылки должен быть в дереве UI");
        }
    }
}
