using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Tests.Chat.Commands;

[TestFixture]
public class ProfileCommandTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "profile-command-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var store = new BroadcastProfilesStore(filePath: Path.Combine(_tempDir, "broadcast-profiles.json"));
        _manager = Substitute.For<BroadcastProfilesManager>(store,
            Substitute.For<IChannelInformationApplier>(),
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            NullLogger<BroadcastProfilesManager>.Instance);

        _command = new(_manager, NullLogger<ProfileCommand>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    private string _tempDir = null!;
    private BroadcastProfilesManager _manager = null!;
    private ProfileCommand _command = null!;

    [Test]
    public void Canonical_IsProfile()
    {
        Assert.That(_command.Canonical, Is.EqualTo("profile"));
    }

    [Test]
    public void CanExecute_NonBroadcasterNonMod_ReturnsFalse()
    {
        var ctx = new CommandContext
        {
            IsBroadcaster = false,
            IsModerator = false,
        };

        Assert.That(_command.CanExecute(ctx), Is.False);
    }

    [Test]
    public void CanExecute_Broadcaster_ReturnsTrue()
    {
        var ctx = new CommandContext
        {
            IsBroadcaster = true,
        };

        Assert.That(_command.CanExecute(ctx), Is.True);
    }

    [Test]
    public void Execute_NoArguments_ReturnsUsage()
    {
        var ctx = new CommandContext
        {
            IsBroadcaster = true,
            Arguments = [],
        };

        var response = _command.Execute(ctx);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Text, Does.Contain("!profile"));
    }

    [Test]
    public void Execute_ProfileNotFound_ReturnsNotFoundMessage()
    {
        _manager.FindByName("unknown").Returns((BroadcastProfile?)null);
        var ctx = new CommandContext
        {
            IsBroadcaster = true,
            MessageId = "m1",
            Arguments = ["unknown"],
        };

        var response = _command.Execute(ctx);

        Assert.That(response, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response!.Text, Does.Contain("unknown"));
            Assert.That(response.Text, Does.Contain("не найден"));
        }
    }

    [Test]
    public async Task Execute_ProfileFound_FiresApplyAndReturnsConfirmation()
    {
        var profile = new BroadcastProfile
        {
            Name = "Dota",
        };

        var applied = new TaskCompletionSource();

        _manager.FindByName("Dota").Returns(profile);
        _manager.ApplyAsync(profile.Id, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                applied.TrySetResult();
                return Task.FromResult<BroadcastProfile?>(profile);
            });

        var ctx = new CommandContext
        {
            IsBroadcaster = true,
            MessageId = "m1",
            Arguments = ["Dota"],
        };

        var response = _command.Execute(ctx);

        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Text, Does.Contain("Dota"));

        await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await _manager.Received(1).ApplyAsync(profile.Id, Arg.Any<CancellationToken>());
    }
}
