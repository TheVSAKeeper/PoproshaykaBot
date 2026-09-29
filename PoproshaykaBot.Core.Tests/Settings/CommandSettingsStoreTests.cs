using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Settings;

[TestFixture]
public sealed class CommandSettingsStoreTests
{
    private const CommandResponseTarget LegacyKnownTargets = CommandResponseTarget.Chat | CommandResponseTarget.Overlay;

    private sealed class LegacySettings
    {
        public CommandResponseTarget DefaultResponseTarget { get; set; }

        public Dictionary<string, LegacyOverride> Commands { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class LegacyOverride
    {
        public bool Enabled { get; set; } = true;

        public CommandResponseTarget ResponseTarget { get; set; }
    }

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
              "defaultResponseTarget": 24,
              "commands": {
                "ранг": { "enabled": true, "responseTarget": 10 }
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
    public void Права_команды_переживают_перезапуск_и_снимаются_обратно()
    {
        var store = new CommandSettingsStore(null, _path);

        store.Mutate(settings => settings.Commands["ранг"] = new()
        {
            Access = CommandAccessLevel.Moderators,
        });

        var afterRestart = new CommandSettingsStore(null, _path);

        var saved = afterRestart.Load().ReadAccess("РАНГ");

        afterRestart.Mutate(settings => settings.Commands["ранг"].Access = null);

        var cleared = new CommandSettingsStore(null, _path).Load();

        Assert.Multiple(() =>
        {
            Assert.That(saved, Is.EqualTo(CommandAccessLevel.Moderators), "Право переживает перезапуск");
            Assert.That(cleared.ReadAccess("ранг"), Is.Null, "Снятое право не возвращается само");
            Assert.That(cleared.ResolveAccessLevel("ранг"), Is.EqualTo(CommandAccessLevel.Everyone));
        });
    }

    [TestCase(-3, CommandAccessLevel.Everyone)]
    [TestCase(1, CommandAccessLevel.Moderators)]
    [TestCase(99, CommandAccessLevel.Broadcaster)]
    public void Неизвестный_уровень_прав_из_файла_зажимается_до_известного(int stored, CommandAccessLevel expected)
    {
        File.WriteAllText(_path,
            $$"""
              {
                "commands": {
                  "ранг": { "enabled": true, "access": {{stored}} }
                }
              }
              """);

        var settings = new CommandSettingsStore(null, _path).Load();

        Assert.That(settings.ResolveAccessLevel("ранг"), Is.EqualTo(expected));
    }

    [Test]
    public void Файл_прошлой_сборки_читается_как_прежде()
    {
        File.WriteAllText(_path,
            """
            {
              "defaultResponseTarget": 3,
              "commands": {
                "ранг": { "enabled": false, "responseTarget": 1 }
              }
            }
            """);

        var settings = new CommandSettingsStore(null, _path).Load();

        Assert.Multiple(() =>
        {
            Assert.That(settings.IsEnabled("ранг"), Is.False);
            Assert.That(settings.ResolveResponseTarget("ранг"), Is.EqualTo(CommandResponseTarget.Chat));
            Assert.That(settings.ReadAccess("ранг"), Is.Null, "Поля прав в старом файле нет – это «как в коде»");
            Assert.That(settings.ResolveResponseTarget("донат"), Is.EqualTo(CommandSettings.ChatAndOverlay));
        });
    }

    [Test]
    public void Новый_файл_читается_прошлой_сборкой_без_потери_включения_и_цели()
    {
        var store = new CommandSettingsStore(null, _path);

        store.Mutate(settings =>
        {
            settings.DefaultResponseTarget = CommandSettings.ChatAndOverlay;

            settings.Commands["ранг"] = new()
            {
                Enabled = false,
                ResponseTarget = CommandResponseTarget.Overlay,
                Access = CommandAccessLevel.Broadcaster,
            };

            settings.Commands["донат"] = new()
            {
                ResponseTarget = CommandSettings.CallerOnly,
            };
        });

        var legacy = JsonSerializer.Deserialize<LegacySettings>(File.ReadAllText(_path), JsonStoreOptions.Default)!;

        var rank = legacy.Commands["ранг"];
        var donate = legacy.Commands["донат"];

        Assert.Multiple(() =>
        {
            Assert.That(rank.Enabled, Is.False, "Выключение команды старая сборка читает как раньше");
            Assert.That(rank.ResponseTarget & LegacyKnownTargets, Is.EqualTo(CommandResponseTarget.Overlay));
            Assert.That(donate.ResponseTarget & LegacyKnownTargets,
                Is.EqualTo(CommandResponseTarget.Chat),
                "Цель «только вызвавшему» записана как чат плюс новый бит, поэтому старая сборка отвечает в чат, а не молчит");

            Assert.That(legacy.DefaultResponseTarget & LegacyKnownTargets, Is.EqualTo(CommandSettings.ChatAndOverlay));
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

    [TestCase("""{"commands":{"profile":{"enabled":false}}}""", false, false)]
    [TestCase("""{"commands":{"PROFILE":{"enabled":false}}}""", false, false)]
    [TestCase("""{"commands":{"profile":{"enabled":false},"пресет":{"enabled":true}}}""", true, true)]
    [TestCase("""{"commands":{"ранг":{"enabled":false}}}""", true, false)]
    public void Настройки_прежнего_имени_переезжают_к_пресету_только_когда_своих_у_него_нет(
        string json,
        bool presetEnabled,
        bool profileKept)
    {
        File.WriteAllText(_path, json);

        var store = new CommandSettingsStore(null, _path);
        var settings = store.Load();

        store.Mutate(_ => { });

        using var written = JsonDocument.Parse(File.ReadAllText(_path));
        var keys = written.RootElement.GetProperty("commands").EnumerateObject().Select(property => property.Name).ToList();
        var restored = new CommandSettingsStore(null, _path).Load();

        Assert.Multiple(() =>
        {
            Assert.That(settings.IsEnabled("пресет"), Is.EqualTo(presetEnabled));
            Assert.That(settings.IsEnabled("мойпрофиль"), Is.True, "Прежний ключ не достаётся команде, забравшей токен");
            Assert.That(settings.Commands.ContainsKey("profile"), Is.EqualTo(profileKept));
            Assert.That(keys.Contains("profile", StringComparer.OrdinalIgnoreCase), Is.EqualTo(profileKept), "В файл уходит перенесённый ключ");
            Assert.That(restored.IsEnabled("пресет"), Is.EqualTo(presetEnabled));
            Assert.That(restored.IsEnabled("ранг"), Is.EqualTo(!json.Contains("ранг")));
        });
    }
}
