using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Chat.Commands;

namespace PoproshaykaBot.WinForms.Tests.Chat.Commands;

[TestFixture]
public class GameCommandTests
{
    [SetUp]
    public void SetUp()
    {
        _applier = Substitute.For<IChannelInformationApplier>();
        _resolver = Substitute.For<IGameCategoryResolver>();

        _command = new(_applier, _resolver, NullLogger<GameCommand>.Instance);
    }

    private IChannelInformationApplier _applier = null!;
    private IGameCategoryResolver _resolver = null!;
    private GameCommand _command = null!;

    [Test]
    public void CanExecute_OnlyForBroadcasterOrMod()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_command.CanExecute(new()
            {
                IsBroadcaster = true,
            }), Is.True);

            Assert.That(_command.CanExecute(new()), Is.False);
        }
    }

    [Test]
    public void Execute_NoArgs_ReturnsUsage()
    {
        var response = _command.Execute(new()
        {
            IsBroadcaster = true,
            Arguments = [],
        });

        Assert.That(response!.Text, Does.Contain("!game"));
    }

    [Test]
    public void Execute_WithQuery_ReturnsProvisionalReply()
    {
        var ctx = new CommandContext
        {
            IsBroadcaster = true,
            MessageId = "m1",
            Arguments = ["dota"],
        };

        var response = _command.Execute(ctx);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response!.Text, Does.Contain("dota"));
            Assert.That(response.Text, Does.Contain("категорию"));
        }
    }

    [Test]
    public async Task Execute_GameFound_EventuallyAppliesPatch()
    {
        var applied = new TaskCompletionSource();

        _resolver.ResolveAsync("dota", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GameSuggestion?>(new("1", "Dota 2", "")));

        _applier
            .When(x => x.ApplyPatchAsync(null, "1", "Dota 2", Arg.Any<CancellationToken>()))
            .Do(_ => applied.TrySetResult());

        _command.Execute(new()
        {
            IsBroadcaster = true,
            MessageId = "m1",
            Arguments = ["dota"],
        });

        await applied.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await _applier.Received(1)
            .ApplyPatchAsync(null,
                "1",
                "Dota 2",
                Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task Execute_GameNotFound_DoesNotCallApplier()
    {
        var resolved = new TaskCompletionSource();

        _resolver.ResolveAsync("unknown", Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                resolved.TrySetResult();
                await Task.Yield();
                return null;
            });

        _command.Execute(new()
        {
            IsBroadcaster = true,
            MessageId = "m1",
            Arguments = ["unknown"],
        });

        await resolved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Yield();

        await _applier.DidNotReceiveWithAnyArgs().ApplyPatchAsync(null, null, null, CancellationToken.None);
    }
}
