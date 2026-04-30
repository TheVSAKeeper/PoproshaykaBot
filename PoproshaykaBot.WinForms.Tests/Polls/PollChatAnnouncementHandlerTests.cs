using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Polling;
using PoproshaykaBot.WinForms.Polls;
using PoproshaykaBot.WinForms.Polls.Handlers;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tests.Polls;

[TestFixture]
public class PollChatAnnouncementHandlerTests
{
    [SetUp]
    public void SetUp()
    {
        _settings = new();
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance, Substitute.For<IEventBus>());
        _settingsManager.Current.Returns(_settings);
        _messenger = Substitute.For<IChatMessenger>();
        _eventBus = Substitute.For<IEventBus>();
        _clock = new() { UtcNow = new(2026, 4, 24, 10, 0, 0, TimeSpan.Zero) };
        _handler = new(_messenger, _settingsManager, _eventBus, _clock);
    }

    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;
    private IChatMessenger _messenger = null!;
    private IEventBus _eventBus = null!;
    private PollChatAnnouncementHandler _handler = null!;
    private TestTimeProvider _clock = null!;

    private static PollSnapshot Active(string pollId = "p1", params (string title, int votes)[] choices)
    {
        var list = choices.Length == 0
            ? (IReadOnlyList<PollChoiceSnapshot>)[new("c1", "A", 0, 0, 0), new("c2", "B", 0, 0, 0)]
            : choices.Select((c, i) => new PollChoiceSnapshot($"c{i}", c.title, c.votes, 0, 0)).ToArray();

        return new(pollId,
            null,
            "Вопрос?",
            list,
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(1),
            false,
            0,
            PollSnapshotStatus.Active,
            null);
    }

    [Test]
    public async Task PollStarted_EnabledTemplate_SendsMessage()
    {
        _settings.Twitch.Polls.ChatTemplates.StartEnabled = true;
        _settings.Twitch.Polls.ChatTemplates.StartTemplate = "Старт: {title}";

        await _handler.HandleAsync(new PollStarted(Active()), CancellationToken.None);

        _messenger.Received(1).Send("Старт: Вопрос?");
    }

    [Test]
    public async Task PollStarted_Disabled_DoesNotSend()
    {
        _settings.Twitch.Polls.ChatTemplates.StartEnabled = false;

        await _handler.HandleAsync(new PollStarted(Active()), CancellationToken.None);

        _messenger.DidNotReceive().Send(Arg.Any<string>());
    }

    [Test]
    public async Task PollFinalized_WithWinner_ExpandsWinnerPlaceholder()
    {
        _settings.Twitch.Polls.ChatTemplates.EndEnabled = true;
        _settings.Twitch.Polls.ChatTemplates.EndTemplate = "Победил: {winner} ({winnerVotes}/{totalVotes})";

        var snapshot = Active(choices: [("A", 5), ("B", 3)]);
        var winner = new PollChoiceSnapshot("c0", "A", 5, 0, 0);

        await _handler.HandleAsync(new PollFinalized(snapshot, winner, false), CancellationToken.None);

        _messenger.Received(1).Send("Победил: A (5/8)");
    }

    [Test]
    public async Task PollFinalized_Tie_ExpandsWinnerAsNichya()
    {
        _settings.Twitch.Polls.ChatTemplates.EndEnabled = true;
        _settings.Twitch.Polls.ChatTemplates.EndTemplate = "Победил: {winner}";

        var snapshot = Active(choices: [("A", 5), ("B", 5)]);

        await _handler.HandleAsync(new PollFinalized(snapshot, null, true), CancellationToken.None);

        _messenger.Received(1).Send("Победил: ничья");
    }

    [Test]
    public async Task PollProgressed_ThrottledWithinInterval_SendsOnlyOnce()
    {
        _settings.Twitch.Polls.ChatTemplates.ProgressEnabled = true;
        _settings.Twitch.Polls.ChatTemplates.ProgressTemplate = "Лидер: {leader}";
        _settings.Twitch.Polls.ChatTemplates.ProgressAnnounceIntervalSeconds = 60;

        var snapshot1 = Active(choices: [("A", 3), ("B", 1)]);
        var snapshot2 = Active(choices: [("A", 5), ("B", 2)]);

        await _handler.HandleAsync(new PollProgressed(snapshot1), CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(10);
        await _handler.HandleAsync(new PollProgressed(snapshot2), CancellationToken.None);

        _messenger.Received(1).Send(Arg.Any<string>());
    }

    [Test]
    public async Task PollProgressed_AfterIntervalPasses_SendsAgain()
    {
        _settings.Twitch.Polls.ChatTemplates.ProgressEnabled = true;
        _settings.Twitch.Polls.ChatTemplates.ProgressTemplate = "Лидер: {leader}";
        _settings.Twitch.Polls.ChatTemplates.ProgressAnnounceIntervalSeconds = 60;

        var snapshot1 = Active(choices: [("A", 3), ("B", 1)]);
        var snapshot2 = Active(choices: [("A", 5), ("B", 2)]);

        await _handler.HandleAsync(new PollProgressed(snapshot1), CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(61);
        await _handler.HandleAsync(new PollProgressed(snapshot2), CancellationToken.None);

        _messenger.Received(2).Send(Arg.Any<string>());
    }

    [Test]
    public async Task PollTerminated_Enabled_SendsMessage()
    {
        _settings.Twitch.Polls.ChatTemplates.TerminatedEnabled = true;
        _settings.Twitch.Polls.ChatTemplates.TerminatedTemplate = "Досрочно: {title}";

        await _handler.HandleAsync(new PollTerminated(Active()), CancellationToken.None);

        _messenger.Received(1).Send("Досрочно: Вопрос?");
    }

    [Test]
    public async Task PollArchived_Disabled_DoesNotSend()
    {
        _settings.Twitch.Polls.ChatTemplates.ArchivedEnabled = false;

        await _handler.HandleAsync(new PollArchived(Active()), CancellationToken.None);

        _messenger.DidNotReceive().Send(Arg.Any<string>());
    }

    [Test]
    public async Task PollStarted_ChoicesPlaceholder_ExpandsNumberedList()
    {
        _settings.Twitch.Polls.ChatTemplates.StartEnabled = true;
        _settings.Twitch.Polls.ChatTemplates.StartTemplate = "Варианты: {choices}";

        var snapshot = Active(choices: [("A", 0), ("B", 0), ("C", 0)]);

        await _handler.HandleAsync(new PollStarted(snapshot), CancellationToken.None);

        _messenger.Received(1).Send("Варианты: 1) A | 2) B | 3) C");
    }
}
