using MahApps.Metro.IconPacks;
using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class SettingsSectionTests
{
    [TestCase("obs")]
    [TestCase("websocket")]
    [TestCase("ОВЕРЛЕЙ")]
    [TestCase("порт оверлей")]
    public void Раздел_находится_по_заголовку_и_ключевым_словам_без_учёта_регистра(string query)
    {
        var sections = List();

        sections.Filter(query);

        Assert.That(sections["obs"].IsVisible, Is.True);
    }

    [TestCase("токен")]
    [TestCase("оверлей токен")]
    public void Несовпавший_раздел_прячется_а_слова_запроса_складываются_по_И(string query)
    {
        var sections = List();

        sections.Filter(query);

        Assert.That(sections["obs"].IsVisible, Is.False);
    }

    [Test]
    public void Первый_раздел_выбран_сразу_после_создания()
    {
        var sections = List();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sections.Selected, Is.SameAs(sections["basic"]));
            Assert.That(sections["basic"].IsSelected, Is.True);
        }
    }

    [Test]
    public void Выбор_другого_раздела_снимает_отметку_с_прежнего()
    {
        var sections = List();

        sections.Selected = sections["obs"];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sections["basic"].IsSelected, Is.False);
            Assert.That(sections["obs"].IsSelected, Is.True);
        }
    }

    [Test]
    public void Поиск_переводит_выбор_на_первый_найденный_раздел_если_прежний_скрыт()
    {
        var sections = List();

        sections.Filter("websocket");

        Assert.That(sections.Selected, Is.SameAs(sections["obs"]));
    }

    [Test]
    public void Ничего_не_найдено_снимает_выбор_и_поднимает_флаг()
    {
        var sections = List();

        sections.Filter("гидропоника");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sections.NoMatches, Is.True);
            Assert.That(sections.Selected, Is.Null);
        }
    }

    [Test]
    public void Пустой_запрос_возвращает_видимость_всем_разделам()
    {
        var sections = List();
        sections.Filter("websocket");

        sections.Filter(string.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sections.Items.All(section => section.IsVisible), Is.True);
            Assert.That(sections.Selected, Is.SameAs(sections["obs"]));
        }
    }

    [TestCase("update", "update")]
    [TestCase("несуществующий", "basic")]
    [TestCase("", "basic")]
    public void Запомненный_раздел_восстанавливается_а_неизвестный_ключ_оставляет_первый(string stored, string expected)
    {
        var sections = List();

        sections.Restore(stored);

        Assert.That(sections.Selected, Is.SameAs(sections[expected]));
    }

    private static SettingsSectionList List()
    {
        return new(
            new SettingsSection("basic", "Основные", PackIconLucideKind.Settings2, "канал лимиты сообщения"),
            new SettingsSection("obs", "OBS", PackIconLucideKind.MonitorPlay, "websocket подключение оверлей порт"),
            new SettingsSection("update", "Обновления", PackIconLucideKind.Download, "github релизы версия"));
    }
}
