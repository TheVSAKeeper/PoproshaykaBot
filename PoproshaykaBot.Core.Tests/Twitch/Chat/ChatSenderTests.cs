using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Tests.Debugging;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Tests.Server;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Tests.Twitch.Chat;

[TestFixture]
public class ChatSenderTests
{
    [SetUp]
    public void SetUp()
    {
        _helix = Substitute.For<ITwitchHelixClient>();
        _broadcasterIdProvider = Substitute.For<IBroadcasterIdProvider>();
        _botUserIdProvider = Substitute.For<IBotUserIdProvider>();

        _broadcasterIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns("1");
        _botUserIdProvider.GetAsync(Arg.Any<CancellationToken>()).Returns("2");

        _targetChannel = new();
        _time = new()
        {
            UtcNow = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
        };

        _tracker = new(_time);

        _logger = new();
        _sender = new(_helix, _broadcasterIdProvider, _botUserIdProvider, _targetChannel, _tracker, _logger);
    }

    private ITwitchHelixClient _helix = null!;
    private IBroadcasterIdProvider _broadcasterIdProvider = null!;
    private IBotUserIdProvider _botUserIdProvider = null!;
    private FakeTargetChannelProvider _targetChannel = null!;
    private TestTimeProvider _time = null!;
    private CommandResponseTracker _tracker = null!;
    private RecordingLogger<ChatSender> _logger = null!;
    private ChatSender _sender = null!;

    [Test]
    public async Task EnqueueAsync_AfterRestart_DeliversMessage()
    {
        var sent = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        _helix.SendChatMessageAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                sent.TrySetResult(call.ArgAt<string>(2));
                return Task.FromResult<string?>("msg-1");
            });

        var progress = new Progress<string>();

        await _sender.StartAsync(progress, CancellationToken.None);
        await _sender.StopAsync(progress, CancellationToken.None);

        await _sender.StartAsync(progress, CancellationToken.None);
        await _sender.EnqueueAsync("hello", null, null, CancellationToken.None);

        var delivered = await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await _sender.StopAsync(progress, CancellationToken.None);

        Assert.That(delivered, Is.EqualTo("hello"));
    }

    [Test]
    public async Task EnqueueAsync_InObserverMode_DoesNotReachTwitch()
    {
        _targetChannel.IsDebugSession = true;
        _targetChannel.IsSendingAllowed = false;

        var progress = new Progress<string>();

        await _sender.StartAsync(progress, CancellationToken.None);
        await _sender.EnqueueAsync("hello", null, null, CancellationToken.None);
        await _sender.StopAsync(progress, CancellationToken.None);

        await _helix.DidNotReceiveWithAnyArgs().SendChatMessageAsync(default!, default!, default!, default, default);
    }

    [Test]
    public async Task EnqueueAsync_SentMessage_IsLoggedWithChannelAndText()
    {
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _helix.SendChatMessageAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sent.TrySetResult();
                return Task.FromResult<string?>("msg-1");
            });

        var progress = new Progress<string>();

        await _sender.StartAsync(progress, CancellationToken.None);
        await _sender.EnqueueAsync("привет чат", null, null, CancellationToken.None);

        await sent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await _sender.StopAsync(progress, CancellationToken.None);

        var line = _logger.Entries
            .Where(entry => entry.Level == LogLevel.Information)
            .Select(entry => entry.Message)
            .FirstOrDefault(message => message.Contains("привет чат", StringComparison.Ordinal));

        Assert.That(line, Is.Not.Null, "Успешная отправка должна попадать в лог");
        Assert.That(line, Does.Contain("test-channel"));
    }

    [Test]
    public async Task Ответ_команды_кладёт_id_каждого_чанка_в_набор_ожидаемого_эха()
    {
        var sentIds = new List<string>();
        var delivered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _helix.SendChatMessageAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var id = $"msg-{sentIds.Count + 1}";
                sentIds.Add(id);

                if (sentIds.Count == 2)
                {
                    delivered.TrySetResult();
                }

                return Task.FromResult<string?>(id);
            });

        var mark = new CommandResponseMark("помощь", CommandResponseTarget.Chat);
        var progress = new Progress<string>();

        await _sender.StartAsync(progress, CancellationToken.None);
        await _sender.EnqueueAsync(new string('a', 700), null, mark, CancellationToken.None);

        await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _sender.StopAsync(progress, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_tracker.TryConsume("msg-1"), Is.EqualTo(mark), "Первый чанк должен попасть в набор");
            Assert.That(_tracker.TryConsume("msg-2"), Is.EqualTo(mark), "Второй чанк должен попасть в набор");
        });
    }

    [Test]
    public async Task Отклонённое_твичем_сообщение_в_набор_ожидаемого_эха_не_попадает()
    {
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _helix.SendChatMessageAsync(Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ =>
            {
                attempted.TrySetResult();
                throw new HelixMessageDroppedException("duplicate", "дубликат");
            });

        var progress = new Progress<string>();

        await _sender.StartAsync(progress, CancellationToken.None);
        await _sender.EnqueueAsync("дубль", null, new("помощь", CommandResponseTarget.Chat), CancellationToken.None);

        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _sender.StopAsync(progress, CancellationToken.None);

        Assert.That(_tracker.TryConsume("msg-1"), Is.Null);
    }
}
