using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tests.Broadcast.Profiles;

[TestFixture]
public class BroadcastProfilesManagerTests
{
    [SetUp]
    public void SetUp()
    {
        _settings = new();
        _settingsManager = Substitute.For<SettingsManager>(NullLogger<SettingsManager>.Instance,
            Substitute.For<IEventBus>());

        _settingsManager.Current.Returns(_settings);
        _applier = Substitute.For<IChannelInformationApplier>();
        _eventBus = Substitute.For<IEventBus>();
        _manager = new(_settingsManager,
            _applier,
            _eventBus,
            NullLogger<BroadcastProfilesManager>.Instance);
    }

    private SettingsManager _settingsManager = null!;
    private AppSettings _settings = null!;
    private IChannelInformationApplier _applier = null!;
    private IEventBus _eventBus = null!;
    private BroadcastProfilesManager _manager = null!;

    [Test]
    public void Add_SavesProfile_AndRaisesChanged()
    {
        var profile = new BroadcastProfile
        {
            Name = "First",
        };

        _manager.Upsert(profile);

        Assert.That(_settings.Twitch.BroadcastProfiles.Profiles, Has.Count.EqualTo(1));
        _settingsManager.Received(1).SaveSettings(_settings);
        _eventBus.Received(1).PublishAsync(Arg.Any<BroadcastProfilesChanged>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void Upsert_DuplicateName_Throws()
    {
        var profile = new BroadcastProfile
        {
            Name = "X",
        };

        _manager.Upsert(profile);

        Assert.Throws<InvalidOperationException>(() =>
        {
            var broadcastProfile = new BroadcastProfile
            {
                Name = "x",
            };

            _manager.Upsert(broadcastProfile);
        });
    }

    [Test]
    public void Upsert_SameProfileEdited_Succeeds()
    {
        var profile = new BroadcastProfile
        {
            Name = "X",
            Title = "old",
        };

        _manager.Upsert(profile);
        profile.Title = "new";
        _manager.Upsert(profile);

        Assert.That(_settings.Twitch.BroadcastProfiles.Profiles, Has.Count.EqualTo(1));
        Assert.That(_settings.Twitch.BroadcastProfiles.Profiles[0].Title, Is.EqualTo("new"));
    }

    [Test]
    public void Remove_DeletesProfile()
    {
        var profile = new BroadcastProfile
        {
            Name = "X",
        };

        _manager.Upsert(profile);
        _manager.Remove(profile.Id);

        Assert.That(_settings.Twitch.BroadcastProfiles.Profiles, Is.Empty);
    }

    [Test]
    public async Task ApplyAsync_UpdatesLastAppliedAndCallsApplier()
    {
        var profile = new BroadcastProfile
        {
            Name = "X",
        };

        _manager.Upsert(profile);

        await _manager.ApplyAsync(profile.Id, CancellationToken.None);

        await _applier.Received(1).ApplyAsync(profile, Arg.Any<CancellationToken>());
        Assert.That(_settings.Twitch.BroadcastProfiles.LastAppliedProfileId, Is.EqualTo(profile.Id));
    }

    [Test]
    public async Task ApplyByNameAsync_CaseInsensitive()
    {
        var profile = new BroadcastProfile
        {
            Name = "MyProfile",
        };

        _manager.Upsert(profile);

        var applied = await _manager.ApplyByNameAsync("myprofile", CancellationToken.None);

        Assert.That(applied, Is.Not.Null);
        await _applier.Received(1).ApplyAsync(profile, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ApplyByNameAsync_NotFound_ReturnsNull()
    {
        var applied = await _manager.ApplyByNameAsync("nope", CancellationToken.None);

        Assert.That(applied, Is.Null);
        await _applier.DidNotReceiveWithAnyArgs().ApplyAsync(null!, CancellationToken.None);
    }
}
