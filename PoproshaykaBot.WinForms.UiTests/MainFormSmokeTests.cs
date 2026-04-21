using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using FlaUIApplication = FlaUI.Core.Application;

namespace PoproshaykaBot.WinForms.UiTests;

[TestFixture]
[NonParallelizable]
public class MainFormSmokeTests
{
    [SetUp]
    public void Setup()
    {
        var appPath = ResolveAppExePath();

        if (!File.Exists(appPath))
        {
            Assert.Ignore($"Исполняемый файл приложения не найден: {appPath}. Соберите PoproshaykaBot.WinForms перед запуском UI-тестов.");
        }

        _automation = new();
        _app = FlaUIApplication.Launch(new()
        {
            FileName = appPath,
            Arguments = "--ui-smoke",
            WorkingDirectory = Path.GetDirectoryName(appPath)!,
            UseShellExecute = false,
        });

        _mainWindow = _app.GetMainWindow(_automation, WindowAppearTimeout);
        Assert.That(_mainWindow, Is.Not.Null, $"Не удалось получить главное окно приложения в течение {WindowAppearTimeout}");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            _app?.Close();
            _app?.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(2));
        }
        catch
        {
            _app?.Kill();
        }
        finally
        {
            _app?.Dispose();
            _automation?.Dispose();
        }
    }

    private const string AppExeName = "PoproshaykaBot.WinForms.exe";
    private const string ExpectedTitlePrefix = "Попрощайка Бот";
    private static readonly TimeSpan WindowAppearTimeout = TimeSpan.FromSeconds(30);

    private FlaUIApplication? _app;
    private UIA3Automation? _automation;
    private Window? _mainWindow;

    [Test]
    public void MainWindow_ShouldLaunch()
    {
        Assert.That(_mainWindow!.IsAvailable, Is.True, "Главное окно должно быть доступно после запуска");
    }

    [Test]
    public void MainWindow_ShouldHaveBotTitle()
    {
        var title = _mainWindow!.Title;
        Assert.That(title, Does.StartWith(ExpectedTitlePrefix),
            $"Заголовок окна должен начинаться с '{ExpectedTitlePrefix}', получено: '{title}'");
    }

    [Test]
    public void MainWindow_ShouldBeResizable()
    {
        Assert.That(_mainWindow!.Patterns.Transform.IsSupported, Is.True,
            "Главное окно должно поддерживать Transform pattern (resize/move)");
    }

    [Test]
    public void MainWindow_ShouldHaveNonZeroBounds()
    {
        var bounds = _mainWindow!.BoundingRectangle;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(bounds.Width, Is.GreaterThan(0), "Ширина окна должна быть положительной");
            Assert.That(bounds.Height, Is.GreaterThan(0), "Высота окна должна быть положительной");
        }
    }

    private static string ResolveAppExePath()
    {
        var envOverride = Environment.GetEnvironmentVariable("POPROSHAYKA_APP_EXE");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            return Path.GetFullPath(envOverride);
        }

        var testAssemblyDir = Path.GetDirectoryName(typeof(MainFormSmokeTests).Assembly.Location)
                              ?? throw new InvalidOperationException("Не удалось определить каталог тестовой сборки");

        var configuration = new DirectoryInfo(testAssemblyDir).Parent?.Name
                            ?? throw new InvalidOperationException("Не удалось определить конфигурацию сборки");

        var candidate = Path.GetFullPath(Path.Combine(testAssemblyDir,
            "..", "..", "..", "..",
            "PoproshaykaBot.WinForms",
            "bin", configuration, "net8.0-windows",
            AppExeName));

        return candidate;
    }
}
