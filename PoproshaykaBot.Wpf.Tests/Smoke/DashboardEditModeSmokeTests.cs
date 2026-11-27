using FlaUI.Core.AutomationElements;

namespace PoproshaykaBot.Wpf.Tests.Smoke;

[TestFixture]
[NonParallelizable]
public class DashboardEditModeSmokeTests
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

    private static readonly TimeSpan ContentAppearTimeout = TimeSpan.FromSeconds(15);

    private SmokeTestSession? _session;

    [Test]
    public void EditMode_OpensAndCloses_FromTheDashboard()
    {
        var entry = WaitForDescendantByName("Настроить панель");
        Assert.That(entry, Is.Not.Null, "Кнопка входа в режим правки должна быть на панели.");
        entry!.AsButton().Invoke();

        var done = WaitForDescendantByName("Выйти из режима правки панели");
        var horizontal = WaitForDescendantByName("Разделитель по горизонтали");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(done, Is.Not.Null, "В режиме правки над панелью появляется полоска с кнопкой выхода.");
            Assert.That(horizontal, Is.Not.Null, "Внутри компактной панели плитки делит разделитель по горизонтали.");
            Assert.That(WaitForDescendantByName("Добавить плитку"), Is.Not.Null);
        }

        done!.AsButton().Invoke();
        Assert.That(WaitForDescendantByName("Настроить панель"), Is.Not.Null,
            "После выхода панель возвращается в обычный вид с кнопкой входа.");

        var presetButton = WaitForDescendantByName("Пресет панели");
        Assert.That(presetButton, Is.Not.Null, "Переключатель пресетов должен быть доступен в шапке обзора.");
        presetButton!.AsButton().Invoke();

        var wideItem = WaitForDesktopDescendantByName("Широкий");
        Assert.That(wideItem, Is.Not.Null, "В меню пресетов должен быть пункт «Широкий».");
        wideItem!.AsMenuItem().Invoke();

        var wideEntry = WaitForDescendantByName("Настроить панель");
        Assert.That(wideEntry, Is.Not.Null, "После переключения в широкий пресет кнопка правки должна остаться доступной.");
        wideEntry!.AsButton().Invoke();

        var wideDone = WaitForDescendantByName("Выйти из режима правки панели");
        var wideVertical = WaitForDescendantByName("Разделитель по вертикали");
        var wideHorizontal = WaitForDescendantByName("Разделитель по горизонтали");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wideDone, Is.Not.Null, "В режиме правки широкого пресета появляется кнопка выхода.");
            Assert.That(wideVertical, Is.Not.Null, "Между колонками широкого пресета обязан появиться разделитель по вертикали.");
            Assert.That(wideHorizontal, Is.Not.Null, "Внутри колонки широкого пресета плитки делит разделитель по горизонтали.");
        }

        wideDone!.AsButton().Invoke();
        Assert.That(
            WaitForDescendantByName("Настроить панель"),
            Is.Not.Null,
            "После выхода панель возвращается в обычный вид с кнопкой входа.");
    }

    private AutomationElement? WaitForDescendantByName(string name)
    {
        var deadline = DateTime.UtcNow + ContentAppearTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var match = _session!.MainWindow.FindFirstDescendant(cf => cf.ByName(name));

            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(150);
        }

        return null;
    }

    private AutomationElement? WaitForDesktopDescendantByName(string name)
    {
        var deadline = DateTime.UtcNow + ContentAppearTimeout;
        var processId = _session!.App.ProcessId;

        while (DateTime.UtcNow < deadline)
        {
            var match = _session.Automation.GetDesktop().FindFirstDescendant(
                cf => cf.ByProcessId(processId).And(cf.ByName(name)));

            if (match is not null)
            {
                return match;
            }

            Thread.Sleep(150);
        }

        return null;
    }
}
