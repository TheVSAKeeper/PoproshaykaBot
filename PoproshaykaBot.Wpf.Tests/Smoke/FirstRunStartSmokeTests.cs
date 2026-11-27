using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace PoproshaykaBot.Wpf.Tests.Smoke;

[TestFixture]
[NonParallelizable]
public class FirstRunStartSmokeTests
{
    private const string StartWindowTitle = "Первый запуск";
    private const string MainWindowTitle = "PoproshaykaBot";
    private static readonly TimeSpan WindowAppearTimeout = TimeSpan.FromSeconds(15);

    private SmokeTestSession? _session;

    [SetUp]
    public void Setup()
    {
        _session = SmokeTestSession.Launch(new() { SeedConfiguredSettings = false });
    }

    [TearDown]
    public void TearDown()
    {
        _session?.Dispose();
        _session = null;
    }

    [Test]
    public void StartWindow_ShouldOpenOffScreen_InEmptyDirectory_AndOpenMainWindowOnStartFresh()
    {
        var startWindow = _session!.FindAppWindow(
            w => string.Equals(w.Title, StartWindowTitle, StringComparison.Ordinal),
            WindowAppearTimeout);

        Assert.That(startWindow, Is.Not.Null,
            $"Окно '{StartWindowTitle}' должно открыться при первом запуске в пустой папке");

        var bounds = startWindow!.BoundingRectangle;
        var importButton = startWindow.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName("Перенести данные из старой версии")));
        var pickFolderButton = startWindow.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName("Выбрать другую папку")));
        var portableRadio = startWindow.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.RadioButton).And(cf.ByName("Рядом с программой (portable)")));
        var appDataRadio = startWindow.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.RadioButton).And(cf.ByName("В папке пользователя (%APPDATA%\\PoproshaykaBot)")));
        var startFreshButton = startWindow.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName("Начать с нуля")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bounds.Width, Is.GreaterThan(0));
            Assert.That(bounds.Height, Is.GreaterThan(0));
            Assert.That(bounds.Right, Is.LessThanOrEqualTo(0),
                "Под --ui-smoke стартовое окно должно открываться за пределами видимого экрана");
            Assert.That(bounds.Bottom, Is.LessThanOrEqualTo(0),
                "Под --ui-smoke стартовое окно должно открываться за пределами видимого экрана");
            Assert.That(importButton, Is.Not.Null);
            Assert.That(pickFolderButton, Is.Not.Null);
            Assert.That(portableRadio, Is.Not.Null);
            Assert.That(appDataRadio, Is.Not.Null);
            Assert.That(startFreshButton, Is.Not.Null);
        }

        startFreshButton!.AsButton().Invoke();

        var mainWindow = _session.FindAppWindow(
            w => string.Equals(w.Title, MainWindowTitle, StringComparison.Ordinal),
            WindowAppearTimeout);

        Assert.That(mainWindow, Is.Not.Null,
            "После нажатия «Начать с нуля» должно открыться главное окно");
    }
}
