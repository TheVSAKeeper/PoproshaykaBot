using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Tests.Polls;

namespace PoproshaykaBot.WinForms.Tests.Broadcast.Profiles;

[TestFixture]
public class StreamEpisodeNumbererTests
{
    [SetUp]
    public void SetUp()
    {
        _settings = new();
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance,
            Substitute.For<IEventBus>());

        _settingsManager.Current.Returns(_settings);

        _profilesManager = Substitute.For<BroadcastProfilesManager>(_settingsManager,
            Substitute.For<IChannelInformationApplier>(),
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            NullLogger<BroadcastProfilesManager>.Instance);

        _profilesManager.AdvanceCurrentNumber(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<DateTimeOffset>())
            .Returns(call =>
            {
                var id = call.Arg<Guid>();
                var nextValue = call.Arg<int>();
                var appliedAt = call.Arg<DateTimeOffset>();
                var stored = _settings.Twitch.BroadcastProfiles.Profiles.FirstOrDefault(p => p.Id == id);

                if (stored == null)
                {
                    return false;
                }

                stored.CurrentNumber = nextValue;
                stored.LastApplyAt = appliedAt;
                return true;
            });

        _applier = Substitute.For<IChannelInformationApplier>();
        _applier.ApplyPatchAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(true);

        _eventBus = Substitute.For<IEventBus>();
        _clock = new() { UtcNow = new(2026, 4, 30, 12, 0, 0, TimeSpan.Zero) };

        _handler = new(_settingsManager,
            _profilesManager,
            _applier,
            _eventBus,
            _clock,
            NullLogger<StreamEpisodeNumberer>.Instance);
    }

    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;
    private BroadcastProfilesManager _profilesManager = null!;
    private IChannelInformationApplier _applier = null!;
    private IEventBus _eventBus = null!;
    private TestTimeProvider _clock = null!;
    private StreamEpisodeNumberer _handler = null!;

    private BroadcastProfile RegisterActiveNumberedProfile(int currentNumber, DateTimeOffset? lastApplyAt = null)
    {
        var profile = new BroadcastProfile
        {
            Id = Guid.NewGuid(),
            Name = "Серия",
            Title = "Серия #{n}",
            CurrentNumber = currentNumber,
            LastApplyAt = lastApplyAt,
        };

        _settings.Twitch.BroadcastProfiles.LastAppliedProfileId = profile.Id;
        _settings.Twitch.BroadcastProfiles.Profiles.Add(profile);
        _profilesManager.Find(profile.Id).Returns(profile);
        return profile;
    }

    [Test]
    public async Task FreshOnline_NeverManuallyApplied_DoesNothing()
    {
        var profile = RegisterActiveNumberedProfile(currentNumber: 14);

        await _handler.HandleAsync(new StreamWentOnline("chan", null, false), CancellationToken.None);

        await _applier.DidNotReceiveWithAnyArgs().ApplyPatchAsync(default, default, default, default);
        Assert.That(profile.CurrentNumber, Is.EqualTo(14));
    }

    [Test]
    public async Task PreviouslyApplied_OnlineAfterWindow_AppliesIncrementedTitle_AndAdvancesCurrentNumber()
    {
        var profile = RegisterActiveNumberedProfile(14, _clock.UtcNow.AddMinutes(-30));

        await _handler.HandleAsync(new StreamWentOnline("chan", null, false), CancellationToken.None);

        await _applier.Received(1).ApplyPatchAsync("Серия #15", null, null, Arg.Any<CancellationToken>());
        Assert.That(profile.CurrentNumber, Is.EqualTo(15));
    }

    [Test]
    public async Task CatchUpOnline_DoesNothing()
    {
        var profile = RegisterActiveNumberedProfile(currentNumber: 14);

        await _handler.HandleAsync(new StreamWentOnline("chan", null, true), CancellationToken.None);

        await _applier.DidNotReceiveWithAnyArgs().ApplyPatchAsync(default, default, default, default);
        Assert.That(profile.CurrentNumber, Is.EqualTo(14));
    }

    [Test]
    public async Task NoActiveProfile_DoesNothing()
    {
        _settings.Twitch.BroadcastProfiles.LastAppliedProfileId = null;

        await _handler.HandleAsync(new StreamWentOnline("chan", null), CancellationToken.None);

        await _applier.DidNotReceiveWithAnyArgs().ApplyPatchAsync(default, default, default, default);
    }

    [Test]
    public async Task ProfileWithoutPlaceholder_DoesNothing()
    {
        var profile = new BroadcastProfile
        {
            Id = Guid.NewGuid(),
            Name = "Plain",
            Title = "Просто стрим",
            CurrentNumber = 5,
        };

        _settings.Twitch.BroadcastProfiles.LastAppliedProfileId = profile.Id;
        _settings.Twitch.BroadcastProfiles.Profiles.Add(profile);
        _profilesManager.Find(profile.Id).Returns(profile);

        await _handler.HandleAsync(new StreamWentOnline("chan", null), CancellationToken.None);

        await _applier.DidNotReceiveWithAnyArgs().ApplyPatchAsync(default, default, default, default);
        Assert.That(profile.CurrentNumber, Is.EqualTo(5));
    }

    [Test]
    public async Task RecentApply_DoesNothing()
    {
        var profile = RegisterActiveNumberedProfile(14, _clock.UtcNow.AddMinutes(-5));

        await _handler.HandleAsync(new StreamWentOnline("chan", null), CancellationToken.None);

        await _applier.DidNotReceiveWithAnyArgs().ApplyPatchAsync(default, default, default, default);
        Assert.That(profile.CurrentNumber, Is.EqualTo(14));
    }

    [Test]
    public async Task ApplyOlderThanWindow_DoesIncrement()
    {
        var profile = RegisterActiveNumberedProfile(14, _clock.UtcNow.AddMinutes(-30));

        await _handler.HandleAsync(new StreamWentOnline("chan", null), CancellationToken.None);

        await _applier.Received(1).ApplyPatchAsync("Серия #15", null, null, Arg.Any<CancellationToken>());
        Assert.That(profile.CurrentNumber, Is.EqualTo(15));
    }

    [Test]
    public async Task RecentOffline_DoesNothing()
    {
        var profile = RegisterActiveNumberedProfile(currentNumber: 14);

        await _handler.HandleAsync(new StreamWentOffline("chan"), CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(3);
        await _handler.HandleAsync(new StreamWentOnline("chan", null), CancellationToken.None);

        await _applier.DidNotReceiveWithAnyArgs().ApplyPatchAsync(default, default, default, default);
        Assert.That(profile.CurrentNumber, Is.EqualTo(14));
    }

    [Test]
    public async Task OldOffline_DoesIncrement()
    {
        var profile = RegisterActiveNumberedProfile(14, _clock.UtcNow.AddMinutes(-30));

        await _handler.HandleAsync(new StreamWentOffline("chan"), CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddMinutes(20);
        await _handler.HandleAsync(new StreamWentOnline("chan", null), CancellationToken.None);

        await _applier.Received(1).ApplyPatchAsync("Серия #15", null, null, Arg.Any<CancellationToken>());
        Assert.That(profile.CurrentNumber, Is.EqualTo(15));
    }

    [Test]
    public async Task ApplierFailure_DoesNotAdvance()
    {
        _applier.ApplyPatchAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var lastApplyAt = _clock.UtcNow.AddMinutes(-30);
        var profile = RegisterActiveNumberedProfile(14, lastApplyAt);

        await _handler.HandleAsync(new StreamWentOnline("chan", null), CancellationToken.None);

        Assert.That(profile.CurrentNumber, Is.EqualTo(14));
        Assert.That(profile.LastApplyAt, Is.EqualTo(lastApplyAt));
    }

    [Test]
    public async Task SuccessfulAdvance_SetsLastApplyAtToNow()
    {
        var profile = RegisterActiveNumberedProfile(14, _clock.UtcNow.AddMinutes(-30));

        await _handler.HandleAsync(new StreamWentOnline("chan", null), CancellationToken.None);

        Assert.That(profile.LastApplyAt, Is.EqualTo(_clock.UtcNow));
    }
}
