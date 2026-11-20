using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;

namespace PoproshaykaBot.Wpf.Tests.Smoke;

[TestFixture]
[NonParallelizable]
public class MainWindowSmokeTests
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

    private const string ExpectedTitle = "PoproshaykaBot";
    private static readonly TimeSpan ContentAppearTimeout = TimeSpan.FromSeconds(15);

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
        Assert.That(title, Is.EqualTo(ExpectedTitle),
            $"Заголовок окна должен быть '{ExpectedTitle}', получено: '{title}'");
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
    public void MainWindow_ShouldRenderShellNavigation()
    {
        var navItem = WaitForDescendantByName("Обзор");

        Assert.That(navItem, Is.Not.Null,
            "Навигация оболочки должна содержать раздел 'Обзор' после запуска");
    }

    [Test]
    public void MainWindow_ShouldShowDashboard_WithTiles()
    {
        var streamTile = WaitForDescendantByName("Информация о стриме");
        var broadcastTile = WaitForDescendantByName("Рассылка");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(streamTile, Is.Not.Null, "Заголовок тайла стрима должен быть в дереве UI");
            Assert.That(broadcastTile, Is.Not.Null, "Заголовок тайла рассылки должен быть в дереве UI");
        }
    }

    [Test]
    public void MultistreamTile_ShouldOpenTheSelectedPlatform()
    {
        var setup = WaitForDescendant(cf => cf.ByAutomationId("MultistreamSettingsYouTube"));
        Assert.That(setup, Is.Not.Null, "Настройка YouTube должна быть доступна из плитки на Обзоре.");
        setup!.AsButton().Invoke();

        var platform = WaitForDescendant(cf => cf.ByAutomationId("MultistreamEditorPlatform").And(cf.ByName("Каналы YouTube")));
        var enable = WaitForDescendant(cf => cf.ByAutomationId("MultistreamEnableYouTube"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(platform, Is.Not.Null, "Кнопка плитки должна открыть редактор каналов YouTube.");
            Assert.That(enable, Is.Not.Null, "Включение YouTube должно быть видно в карточке площадки.");
            Assert.That(_session!.MainWindow.FindFirstDescendant(cf => cf.ByName("Управление подключением по площадкам")), Is.Null,
                "Управление отдельными площадками больше не должно находиться в меню шапки.");
        }
    }

    [Test]
    public void PlatformSettingsLink_ShouldOpenMultistreamEditor()
    {
        InvokeNamedButton("Настройки");
        InvokeNamedButton("Площадки");
        InvokeNamedButton("YouTube");

        var link = WaitForDescendant(cf => cf.ByAutomationId("MultistreamSettingsLink-youtube"));
        Assert.That(link, Is.Not.Null, "Старый раздел YouTube должен вести на вкладку «Мультистрим».");
        link!.AsButton().Invoke();

        var editor = WaitForDescendant(cf => cf.ByAutomationId("MultistreamEditorPlatform").And(cf.ByName("Каналы YouTube")));
        Assert.That(editor, Is.Not.Null, "Ссылка должна открывать каналы YouTube.");

        var automation = WaitForDescendant(cf => cf.ByAutomationId("MultistreamOpenAutomation"));
        Assert.That(automation, Is.Not.Null, "Автоматика и профили должны быть доступны на той же вкладке.");
        automation!.AsButton().Invoke();

        var section = WaitForDescendant(cf => cf.ByAutomationId("MultistreamAutomationSection"));
        Assert.That(section, Is.Not.Null);
        Assert.That(section!.Patterns.ExpandCollapse.Pattern.ExpandCollapseState.Value, Is.EqualTo(ExpandCollapseState.Expanded));
    }

    private void InvokeNamedButton(string name)
    {
        var button = WaitForDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)));
        Assert.That(button, Is.Not.Null, $"Кнопка «{name}» должна быть доступна.");
        button!.AsButton().Invoke();
    }

    private AutomationElement? WaitForDescendantByName(string name) => WaitForDescendant(cf => cf.ByName(name));

    private AutomationElement? WaitForDescendant(Func<ConditionFactory, ConditionBase> condition)
    {
        var deadline = DateTime.UtcNow + ContentAppearTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var match = _session!.MainWindow.FindFirstDescendant(condition);
            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(150);
        }

        return null;
    }
}
