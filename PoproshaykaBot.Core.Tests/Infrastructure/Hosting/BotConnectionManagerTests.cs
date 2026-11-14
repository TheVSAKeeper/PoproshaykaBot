using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Core.Tests.Infrastructure.Hosting;

[TestFixture]
public sealed class BotConnectionManagerTests
{
    public enum Interruption
    {
        None = 0,
        CancelDuringStart = 1,
        FailDuringStart = 2,
        CancelOnJoined = 3,
    }

    [TestCase(Interruption.CancelDuringStart, BotLifecyclePhase.Cancelled, new[] { "start:sender", "start:slow", "stop:sender" })]
    [TestCase(Interruption.FailDuringStart, BotLifecyclePhase.Failed, new[] { "start:sender", "start:slow", "stop:sender" })]
    [TestCase(Interruption.CancelOnJoined, BotLifecyclePhase.Cancelled, new[] { "start:sender", "start:slow", "stop:slow", "stop:sender" })]
    public async Task Connect_Interrupted_StopsStartedComponentsAndAllowsReconnect(Interruption interruption, BotLifecyclePhase interruptedPhase, string[] expectedTrace)
    {
        var trace = new List<string>();
        BotConnectionManager? manager = null;
        var active = interruption;

        var eventBus = Substitute.For<IEventBus>();
        eventBus.PublishAsync(Arg.Any<BotJoinedChannel>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (active == Interruption.CancelOnJoined)
                {
                    manager!.CancelConnection();
                    call.ArgAt<CancellationToken>(1).ThrowIfCancellationRequested();
                }

                return Task.CompletedTask;
            });

        var components = new IHostedComponent[]
        {
            new RecordingComponent("sender", 50, trace, () => { }),
            new RecordingComponent("slow", 200, trace, () =>
            {
                switch (active)
                {
                    case Interruption.CancelDuringStart:
                        manager!.CancelConnection();
                        break;

                    case Interruption.FailDuringStart:
                        throw new InvalidOperationException("сбой запуска компонента");
                }
            }),
        };

        var tokenService = Substitute.For<ITwitchOAuthService>();
        tokenService.GetAccessTokenAsync(TwitchOAuthRole.Bot, Arg.Any<CancellationToken>()).Returns("token");

        var targetChannel = Substitute.For<ITargetChannelProvider>();
        targetChannel.Current.Returns(new TargetChannelState("channel", "channel", false, true));

        var twitchHost = new PlatformHost(StreamingPlatform.Twitch, components, NullLogger<PlatformHost>.Instance);
        var twitchAdapter = new TwitchPlatformConnectionAdapter(
            tokenService,
            targetChannel,
            twitchHost,
            NullLogger<TwitchPlatformConnectionAdapter>.Instance);

        manager = new(
            new([], NullLogger<AppHost>.Instance),
            [twitchAdapter],
            null,
            eventBus,
            NullLogger<BotConnectionManager>.Instance);

        manager.StartConnection();
        await manager.WaitForConnectionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(manager.CurrentPhase, Is.EqualTo(interruptedPhase));
            Assert.That(trace, Is.EqualTo(expectedTrace), "Прерванное подключение останавливает уже запущенные компоненты");
        });

        active = Interruption.None;
        trace.Clear();
        manager.StartConnection();
        await manager.WaitForConnectionAsync();

        Assert.Multiple(() =>
        {
            Assert.That(manager.CurrentPhase, Is.EqualTo(BotLifecyclePhase.Connected));
            Assert.That(trace, Is.EqualTo(["start:sender", "start:slow"]));
        });
    }

    [TestCase(1)]
    [TestCase(2)]
    public async Task ForcedStop_DuringGracefulStop_CancelsItAndBothFinish(int forcedStops)
    {
        var component = new BlockingStopComponent();

        var eventBus = Substitute.For<IEventBus>();
        var tokenService = Substitute.For<ITwitchOAuthService>();
        tokenService.GetAccessTokenAsync(TwitchOAuthRole.Bot, Arg.Any<CancellationToken>()).Returns("token");

        var targetChannel = Substitute.For<ITargetChannelProvider>();
        targetChannel.Current.Returns(new TargetChannelState("channel", "channel", false, true));

        var chatHandler = new ChatPipelineHandler(new ChatPlatformRegistry([]),
            new PlatformChannelRegistry(),
            null!,
            null!,
            null!,
            new(null!),
            null!,
            new(TimeProvider.System),
            eventBus,
            NullLogger<ChatPipelineHandler>.Instance);

        var twitchHost = new PlatformHost(StreamingPlatform.Twitch, [component], NullLogger<PlatformHost>.Instance);
        var twitchAdapter = new TwitchPlatformConnectionAdapter(
            tokenService,
            targetChannel,
            twitchHost,
            NullLogger<TwitchPlatformConnectionAdapter>.Instance);

        var manager = new BotConnectionManager(
            new([], NullLogger<AppHost>.Instance),
            [twitchAdapter],
            chatHandler,
            eventBus,
            NullLogger<BotConnectionManager>.Instance);

        manager.StartConnection();
        await manager.WaitForConnectionAsync();

        var graceful = manager.StopAsync();
        var gracefulToken = await component.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var forced = Enumerable.Range(0, forcedStops)
            .Select(_ => Task.Run(() => manager.StopAsync(BotStopMode.Forced)))
            .ToArray();

        await Task.WhenAll([graceful, .. forced]).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(gracefulToken.IsCancellationRequested, Is.True, "Принудительная остановка отменяет идущую штатную");
            Assert.That(component.StopCalls, Is.EqualTo(1));
            Assert.That(manager.CurrentPhase, Is.EqualTo(BotLifecyclePhase.Disconnected));
        });
    }

    private sealed class BlockingStopComponent : IHostedComponent
    {
        private int _stopCalls;

        public string Name => "blocking";

        public int StartOrder => 100;

        public int StopCalls => Volatile.Read(ref _stopCalls);

        public TaskCompletionSource<CancellationToken> StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _stopCalls);
            StopEntered.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class RecordingComponent(string name, int order, List<string> trace, Action onStart) : IHostedComponent
    {
        public string Name { get; } = name;

        public int StartOrder { get; } = order;

        public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            trace.Add($"start:{Name}");
            onStart();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
        {
            trace.Add($"stop:{Name}");
            return Task.CompletedTask;
        }
    }
}
