using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Tests.Settings;

[TestFixture]
public sealed class CommandSettingsStoreTests
{
    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-command-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "commands.json");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private string _root = null!;
    private string _path = null!;

    [Test]
    public void Свежий_профиль_отвечает_чат_и_оверлей_для_любой_команды()
    {
        var settings = new CommandSettingsStore(null, _path).Load();

        Assert.Multiple(() =>
        {
            Assert.That(settings.IsEnabled("ранг"), Is.True);
            Assert.That(settings.ResolveResponseTarget("ранг"),
                Is.EqualTo(CommandResponseTarget.Chat | CommandResponseTarget.Overlay));
        });
    }

    [Test]
    public void Переопределение_переживает_перезапуск_и_читается_без_учёта_регистра()
    {
        var store = new CommandSettingsStore(null, _path);

        store.Mutate(settings =>
        {
            settings.DefaultResponseTarget = CommandResponseTarget.Chat;

            settings.Commands["ранг"] = new()
            {
                Enabled = false,
                ResponseTarget = CommandResponseTarget.Overlay,
            };
        });

        var restored = new CommandSettingsStore(null, _path).Load();

        Assert.Multiple(() =>
        {
            Assert.That(restored.DefaultResponseTarget, Is.EqualTo(CommandResponseTarget.Chat));
            Assert.That(restored.IsEnabled("РАНГ"), Is.False);
            Assert.That(restored.ResolveResponseTarget("Ранг"), Is.EqualTo(CommandResponseTarget.Overlay));
            Assert.That(restored.ResolveResponseTarget("донат"), Is.EqualTo(CommandResponseTarget.Chat));
        });
    }

    [Test]
    public void Неизвестный_бит_цели_из_правленого_руками_файла_отбрасывается()
    {
        File.WriteAllText(_path,
            """
            {
              "defaultResponseTarget": 12,
              "commands": {
                "ранг": { "enabled": true, "responseTarget": 6 }
              }
            }
            """);

        var settings = new CommandSettingsStore(null, _path).Load();

        Assert.Multiple(() =>
        {
            Assert.That(settings.ResolveResponseTarget("донат"), Is.EqualTo(CommandResponseTarget.None));
            Assert.That(settings.ResolveResponseTarget("ранг"), Is.EqualTo(CommandResponseTarget.Overlay));
        });
    }

    [Test]
    public void Пустая_запись_команды_не_роняет_разбор_сообщения()
    {
        File.WriteAllText(_path,
            """
            {
              "commands": {
                "ранг": null
              }
            }
            """);

        var settings = new CommandSettingsStore(null, _path).Load();

        Assert.Multiple(() =>
        {
            Assert.That(settings.IsEnabled("ранг"), Is.True);
            Assert.That(settings.ResolveResponseTarget("ранг"),
                Is.EqualTo(CommandResponseTarget.Chat | CommandResponseTarget.Overlay));
        });
    }

    [Test]
    public void Повреждённый_файл_оставляет_копию_и_даёт_дефолты()
    {
        File.WriteAllText(_path, "{ это не json");

        var settings = new CommandSettingsStore(null, _path).Load();

        Assert.Multiple(() =>
        {
            Assert.That(settings.IsEnabled("ранг"), Is.True);
            Assert.That(Directory.GetFiles(_root, "*invalid*"), Is.Not.Empty);
        });
    }
}
