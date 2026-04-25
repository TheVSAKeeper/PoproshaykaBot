using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Polling;
using PoproshaykaBot.WinForms.Polls;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tests.Polls;

[TestFixture]
public class PollProfilesManagerTests
{
    [SetUp]
    public void SetUp()
    {
        _settings = new();
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance,
            Substitute.For<IEventBus>());

        _settingsManager.Current.Returns(_settings);
        _eventBus = Substitute.For<IEventBus>();
        _manager = new(_settingsManager, _eventBus, NullLogger<PollProfilesManager>.Instance);
    }

    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;
    private IEventBus _eventBus = null!;
    private PollProfilesManager _manager = null!;

    private static PollProfile ValidProfile(string name = "First")
    {
        return new()
        {
            Name = name,
            Title = "Вопрос?",
            Choices = ["A", "B"],
            DurationSeconds = 60,
        };
    }

    [Test]
    public void Upsert_SavesProfile_AndRaisesChanged()
    {
        var profile = ValidProfile();

        _manager.Upsert(profile);

        Assert.That(_settings.Twitch.Polls.Profiles, Has.Count.EqualTo(1));
        _settingsManager.Received(1).SaveSettings(_settings);
        _eventBus.Received(1).PublishAsync(Arg.Any<PollProfilesChanged>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Upsert_EmptyName_Throws()
    {
        var profile = ValidProfile();
        profile.Name = "   ";

        Assert.Throws<InvalidOperationException>(() => _manager.Upsert(profile));
    }

    [Test]
    public void Upsert_DuplicateName_CaseInsensitive_Throws()
    {
        _manager.Upsert(ValidProfile("X"));

        var duplicate = ValidProfile("x");

        Assert.Throws<InvalidOperationException>(() => _manager.Upsert(duplicate));
    }

    [Test]
    public void Upsert_SameIdEdited_Succeeds()
    {
        var profile = ValidProfile("X");
        _manager.Upsert(profile);

        profile.Title = "Новый вопрос";
        _manager.Upsert(profile);

        Assert.That(_settings.Twitch.Polls.Profiles, Has.Count.EqualTo(1));
        Assert.That(_settings.Twitch.Polls.Profiles[0].Title, Is.EqualTo("Новый вопрос"));
    }

    [Test]
    public void Upsert_TooFewChoices_Throws()
    {
        var profile = ValidProfile();
        profile.Choices = ["OnlyOne"];

        Assert.Throws<InvalidOperationException>(() => _manager.Upsert(profile));
    }

    [Test]
    public void Upsert_TooManyChoices_Throws()
    {
        var profile = ValidProfile();
        profile.Choices = ["1", "2", "3", "4", "5", "6"];

        Assert.Throws<InvalidOperationException>(() => _manager.Upsert(profile));
    }

    [Test]
    public void Upsert_EmptyChoice_Throws()
    {
        var profile = ValidProfile();
        profile.Choices = ["A", "  "];

        Assert.Throws<InvalidOperationException>(() => _manager.Upsert(profile));
    }

    [Test]
    public void Upsert_DurationBelowMin_Throws()
    {
        var profile = ValidProfile();
        profile.DurationSeconds = 14;

        Assert.Throws<InvalidOperationException>(() => _manager.Upsert(profile));
    }

    [Test]
    public void Upsert_DurationAboveMax_Throws()
    {
        var profile = ValidProfile();
        profile.DurationSeconds = 1801;

        Assert.Throws<InvalidOperationException>(() => _manager.Upsert(profile));
    }

    [Test]
    public void Upsert_ChannelPointsEnabledWithZeroCost_Throws()
    {
        var profile = ValidProfile();
        profile.ChannelPointsVotingEnabled = true;
        profile.ChannelPointsPerVote = 0;

        Assert.Throws<InvalidOperationException>(() => _manager.Upsert(profile));
    }

    [Test]
    public void Upsert_ChannelPointsDisabled_ZeroCostAllowed()
    {
        var profile = ValidProfile();
        profile.ChannelPointsVotingEnabled = false;
        profile.ChannelPointsPerVote = 0;

        Assert.DoesNotThrow(() => _manager.Upsert(profile));
    }

    [Test]
    public void Remove_DeletesProfile()
    {
        var profile = ValidProfile();
        _manager.Upsert(profile);
        _eventBus.ClearReceivedCalls();

        _manager.Remove(profile.Id);

        Assert.That(_settings.Twitch.Polls.Profiles, Is.Empty);
        _eventBus.Received(1).PublishAsync(Arg.Any<PollProfilesChanged>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void FindByName_CaseInsensitive()
    {
        var profile = ValidProfile("MyPoll");
        _manager.Upsert(profile);

        var found = _manager.FindByName("mypoll");

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Id, Is.EqualTo(profile.Id));
    }

    [Test]
    public void FindByName_NotFound_ReturnsNull()
    {
        Assert.That(_manager.FindByName("nope"), Is.Null);
    }

    [Test]
    public void Find_ById_ReturnsProfile()
    {
        var profile = ValidProfile();
        _manager.Upsert(profile);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_manager.Find(profile.Id), Is.Not.Null);
            Assert.That(_manager.Find(Guid.NewGuid()), Is.Null);
        }
    }

    [Test]
    public void GetAll_ReturnsSnapshotCopy()
    {
        _manager.Upsert(ValidProfile("A"));
        _manager.Upsert(ValidProfile("B"));

        var list = _manager.GetAll();

        Assert.That(list, Has.Count.EqualTo(2));

        // Mutating returned list must not affect underlying storage
        ((List<PollProfile>)list).Clear();
        Assert.That(_settings.Twitch.Polls.Profiles, Has.Count.EqualTo(2));
    }
}
