using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Tests.Polls;

namespace PoproshaykaBot.Core.Tests.Chat;

[TestFixture]
public sealed class ChatCommandProcessorTests
{
    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-commands-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        _settingsStore = new(null, Path.Combine(_root, "commands.json"));

        _time = new()
        {
            UtcNow = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
        };

        _usage = new(_time);
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
    private CommandSettingsStore _settingsStore = null!;
    private TestTimeProvider _time = null!;
    private CommandUsageRepository _usage = null!;

    [Test]
    public async Task Выключенная_команда_не_выполняется_и_не_пишется_в_файл_неизвестных()
    {
        var command = new FakeCommand("ранг");
        var processor = CreateProcessor(command);

        _settingsStore.Mutate(settings => settings.Commands["ранг"] = new()
        {
            Enabled = false,
        });

        var unknownFile = AppPaths.Combine("unknown_commands.txt");
        var sizeBefore = File.Exists(unknownFile) ? new FileInfo(unknownFile).Length : 0;

        var result = await processor.TryProcessAsync("!ранг", CreateContext(), CancellationToken.None);

        var sizeAfter = File.Exists(unknownFile) ? new FileInfo(unknownFile).Length : 0;

        Assert.Multiple(() =>
        {
            Assert.That(result.IsCommand, Is.False);
            Assert.That(command.Calls, Is.Zero);
            Assert.That(sizeAfter, Is.EqualTo(sizeBefore), "Выключенная команда – не неизвестная, в файл она не пишется");
            Assert.That(_usage.GetSnapshot(), Is.Empty);
        });
    }

    [TestCase("?", "?ранг", true)]
    [TestCase("?", "!ранг", false)]
    [TestCase("  ", "!ранг", true)]
    public async Task Префикс_разбора_виден_снаружи_и_решает_судьбу_сообщения(string prefix, string message, bool handled)
    {
        var command = new FakeCommand("ранг");
        var processor = new ChatCommandProcessor(
            [command],
            _settingsStore,
            _usage,
            NullLogger<ChatCommandProcessor>.Instance,
            prefix);

        var result = await processor.TryProcessAsync(message, CreateContext(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(processor.Prefix, Is.EqualTo(string.IsNullOrWhiteSpace(prefix) ? ChatCommandProcessor.DefaultPrefix : prefix));
            Assert.That(message.StartsWith(processor.Prefix, StringComparison.Ordinal), Is.EqualTo(handled));
            Assert.That(result.IsCommand, Is.EqualTo(handled));
        });
    }

    [Test]
    public void Ограничение_особым_списком_объявляет_команда_а_не_хост()
    {
        Assert.Multiple(() =>
        {
            Assert.That(((IChatCommand)new FakeCommand("ранг")).IsRestrictedToAllowedUsers, Is.False, "Обычная команда особым списком не ограничена");
            Assert.That(
                new TrumpCommand(new(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"))).IsRestrictedToAllowedUsers,
                Is.True);
        });
    }

    [Test]
    public async Task Включение_команды_обратно_действует_без_перезапуска()
    {
        var command = new FakeCommand("ранг");
        var processor = CreateProcessor(command);

        _settingsStore.Mutate(settings => settings.Commands["ранг"] = new()
        {
            Enabled = false,
        });

        await processor.TryProcessAsync("!ранг", CreateContext(), CancellationToken.None);

        _settingsStore.Mutate(settings => settings.Commands["ранг"].Enabled = true);

        var result = await processor.TryProcessAsync("!ранг", CreateContext(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsCommand, Is.True);
            Assert.That(command.Calls, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Успешный_вызов_растит_счётчик_и_запоминает_последнего()
    {
        var processor = CreateProcessor(new FakeCommand("ранг"));

        await processor.TryProcessAsync("!ранг", CreateContext(), CancellationToken.None);
        await processor.TryProcessAsync("!ранг", CreateContext(), CancellationToken.None);

        var record = _usage.GetSnapshot().Single();

        Assert.Multiple(() =>
        {
            Assert.That(record.Canonical, Is.EqualTo("ранг"));
            Assert.That(record.TotalCount, Is.EqualTo(2));
            Assert.That(record.StreamCount, Is.EqualTo(2));
            Assert.That(record.LastUsedBy, Is.EqualTo("Алиса"));
            Assert.That(record.LastUsedAt, Is.EqualTo(_time.UtcNow));
        });
    }

    [Test]
    public async Task Упавшая_команда_в_счётчик_не_попадает_и_отличима_от_успеха_без_ответа()
    {
        var processor = CreateProcessor(new FakeCommand("ранг")
        {
            Throws = true,
        });

        var result = await processor.TryProcessAsync("!ранг", CreateContext(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsCommand, Is.True);
            Assert.That(result.Executed, Is.False);
            Assert.That(result.Canonical, Is.EqualTo("ранг"));
            Assert.That(_usage.GetSnapshot(), Is.Empty);
        });
    }

    [Test]
    public async Task Успех_без_ответа_считается_выполнением()
    {
        var processor = CreateProcessor(new FakeCommand("ранг")
        {
            Response = null,
        });

        var result = await processor.TryProcessAsync("!ранг", CreateContext(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Executed, Is.True);
            Assert.That(result.Response, Is.Null);
            Assert.That(_usage.GetSnapshot().Single().TotalCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Выключенная_команда_не_попадает_в_список_доступных()
    {
        var processor = CreateProcessor(new FakeCommand("ранг"), new FakeCommand("донат"));

        _settingsStore.Mutate(settings => settings.Commands["донат"] = new()
        {
            Enabled = false,
        });

        var enabled = processor.GetEnabledCommands().Select(x => x.Canonical).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(enabled, Does.Contain("ранг"));
            Assert.That(enabled, Does.Not.Contain("донат"));
            Assert.That(processor.GetAllCommands(), Has.Count.EqualTo(2));
        });
    }

    [Test]
    public async Task Помощь_перечисляет_только_включённые_команды()
    {
        var processor = CreateProcessor(new FakeCommand("ранг"), new FakeCommand("донат"));
        processor.Register(new HelpCommand(processor.GetEnabledCommands, processor.Prefix));

        _settingsStore.Mutate(settings => settings.Commands["донат"] = new()
        {
            Enabled = false,
        });

        var result = await processor.TryProcessAsync("!помощь", CreateContext(), CancellationToken.None);

        Assert.That(result.Response, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result.Response!.Text, Does.Contain("!ранг"));
            Assert.That(result.Response.Text, Does.Not.Contain("!донат"));
        });
    }

    [TestCase("!", "!помощь ранг")]
    [TestCase("!", "!помощь !ранг")]
    [TestCase("?", "?помощь ранг")]
    [TestCase("?", "?помощь ?ранг")]
    public async Task Помощь_печатает_имена_префиксом_процессора(string prefix, string message)
    {
        var processor = new ChatCommandProcessor(
            [new FakeCommand("ранг")],
            _settingsStore,
            _usage,
            NullLogger<ChatCommandProcessor>.Instance,
            prefix);

        processor.Register(new HelpCommand(processor.GetEnabledCommands, processor.Prefix));

        var list = await processor.TryProcessAsync($"{prefix}помощь", CreateContext(), CancellationToken.None);
        var about = await processor.TryProcessAsync(message, CreateContext(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(list.Response!.Text, Does.Contain($"{prefix}ранг"));
            Assert.That(about.Response!.Text, Does.Contain($"{prefix}ранг: тестовая команда"), "Аргумент разбирается с префиксом и без него");
            Assert.That(
                list.Response.Text.Contains("!ранг", StringComparison.Ordinal),
                Is.EqualTo(prefix == ChatCommandProcessor.DefaultPrefix),
                "При нестандартном префиксе решётки в выводе помощи не остаётся");
        });
    }

    private static CommandContext CreateContext()
    {
        return new()
        {
            Channel = "test-channel",
            MessageId = "msg-1",
            UserId = "u1",
            Username = "alice",
            DisplayName = "Алиса",
        };
    }

    private ChatCommandProcessor CreateProcessor(params IChatCommand[] commands)
    {
        return new(commands, _settingsStore, _usage, NullLogger<ChatCommandProcessor>.Instance);
    }

    private sealed class FakeCommand(string canonical) : IChatCommand
    {
        public string Canonical => canonical;

        public IReadOnlyCollection<string> Aliases => [];

        public string Description => "тестовая команда";

        public bool Throws { get; init; }

        public OutgoingMessage? Response { get; init; } = OutgoingMessage.Normal("ответ");

        public int Calls { get; private set; }

        public bool CanExecute(CommandContext context)
        {
            return true;
        }

        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            Calls++;

            if (Throws)
            {
                throw new InvalidOperationException("команда упала");
            }

            return Task.FromResult(Response);
        }
    }
}
