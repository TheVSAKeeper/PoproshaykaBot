using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.EventSub;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Tests.Streaming;

[TestFixture]
public sealed class StreamStatusManagerMultiContextTests
{
    private DirectoryInfo _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateTempSubdirectory("ssm-multi-ctx");
    }

    [TearDown]
    public void TearDown()
    {
        _dir.Delete(recursive: true);
    }

    [Test]
    public async Task CreateEventSubSubscriptions_UsesSingleSessionBotContext_AcrossMultipleMonitoredChannels()
    {
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_dir.FullName, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "177128531",
                    DisplayName = "bobito217",
                    Handle = "bobito217",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "107618204",
                    BotContextId = "tw-bot-107618204",
                    BotVerifiedSubjectId = "107618204",
                },
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "90020006",
                    DisplayName = "tsm_viss",
                    Handle = "tsm_viss",
                    IsConfigured = true,
                    MonitoringEnabled = true,
                    BotAccountId = "1298214074",
                    BotContextId = "default",
                    BotVerifiedSubjectId = "1298214074",
                },
            ],
            Platforms =
            [
                new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "177128531" },
            ],
        });

        var capturedContexts = new List<(string BroadcasterId, string? ContextId)>();
        var helix = Substitute.For<ITwitchHelixClient>();
        helix.CreateEventSubSubscriptionAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var condition = call.ArgAt<IReadOnlyDictionary<string, string>>(2);
                capturedContexts.Add((condition["broadcaster_user_id"], PlatformAuthRequestOptions.CurrentContextId));
                return Task.FromResult($"sub-{capturedContexts.Count}");
            });
        helix.GetStreamsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<HelixStreamInfo>>([]));

        var eventSub = Substitute.For<ITwitchEventSubClient>();
        eventSub.SessionId.Returns("session-1");

        var broadcasterId = Substitute.For<IBroadcasterIdProvider>();
        broadcasterId.GetAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("177128531"));

        var target = Substitute.For<ITargetChannelProvider>();
        target.Current.Returns(new TargetChannelState("bobito217", "bobito217", false, true));

        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "settings.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var registry = new PlatformChannelRegistry();
        var auth = Substitute.For<IPlatformAuthService>();
        auth.HasUsableAccessToken(PlatformAuthRole.Bot).Returns(true);
        auth.HasUsableAccessToken(PlatformAuthRole.Bot, "tw-bot-107618204").Returns(true);
        auth.HasUsableAccessToken(PlatformAuthRole.Bot, "default").Returns(true);

        await using var manager = new StreamStatusManager(
            eventSub,
            helix,
            broadcasterId,
            target,
            settings,
            bus,
            TimeProvider.System,
            NullLogger<StreamStatusManager>.Instance,
            catalog,
            registry,
            auth);

        await manager.StartAsync(new Progress<string>(_ => { }), CancellationToken.None);

        eventSub.OnSessionWelcome += Raise.Event<EventSubAsyncHandler<EventSubSessionWelcomeArgs>>(
            new EventSubSessionWelcomeArgs("session-1", 10),
            CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (capturedContexts.Count < 4 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Multiple(() =>
        {
            Assert.That(capturedContexts, Has.Count.EqualTo(4));
            Assert.That(capturedContexts.Select(static c => c.BroadcasterId).Distinct(), Is.EquivalentTo(new[] { "177128531", "90020006" }));
            Assert.That(capturedContexts.Select(static c => c.ContextId).Distinct(), Is.EqualTo(new[] { "tw-bot-107618204" }));
        });
    }

    [Test]
    public void ChannelCatalogStore_EnsureChannelKnown_CaseOnlyDifference_DoesNotEmitChanged()
    {
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_dir.FullName, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new()
                {
                    Platform = StreamingPlatform.Twitch,
                    ChannelId = "177128531",
                    DisplayName = "bobito217",
                    KnownAliases = ["BOBito217"],
                    IsConfigured = true,
                    MonitoringEnabled = true,
                },
            ],
        });

        var changedCount = 0;
        catalog.Changed += (_, _) => changedCount++;

        catalog.EnsureChannelKnown(new ChannelKey(StreamingPlatform.Twitch, "177128531"), "BOBito217");

        Assert.That(changedCount, Is.Zero);
    }
}
