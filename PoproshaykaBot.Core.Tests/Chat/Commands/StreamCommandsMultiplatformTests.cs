using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Tests.Fakes;
using PoproshaykaBot.Core.Tests.Polls;

namespace PoproshaykaBot.Core.Tests.Chat.Commands;

[TestFixture]
public sealed class StreamCommandsMultiplatformTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-stream-cmds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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

    [Test]
    public void StreamStateMachine_ChannelUpdateAndMarkOnline_DoNotFabricateViewerSample()
    {
        var clock = new TestTimeProvider
        {
            UtcNow = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero),
        };
        var machine = new StreamStateMachine(clock);

        machine.MarkOnline("s1", clock.UtcNow.UtcDateTime);
        machine.ApplyChannelUpdate(new(StreamingPlatform.Twitch, "Новое название", "ru", "1", "Just Chatting", []));

        var beforeHelix = machine.GetSnapshot();

        clock.UtcNow += TimeSpan.FromMinutes(1);
        machine.ApplyOnlineSnapshot(new()
        {
            Id = "s1",
            StartedAt = clock.UtcNow.UtcDateTime.AddMinutes(-1),
            Title = "Новое название",
            GameName = "Just Chatting",
            ViewerCount = 0,
        });

        var fresh = machine.GetSnapshot();

        clock.UtcNow += TimeSpan.FromMinutes(10);

        Assert.Multiple(() =>
        {
            Assert.That(beforeHelix.EvaluateViewerSample(clock.UtcNow, StreamStatusSnapshot.DefaultViewerFreshnessWindow),
                Is.EqualTo(ViewerSampleState.None));
            Assert.That(fresh.EvaluateViewerSample(fresh.ViewerSampledAtUtc!.Value, StreamStatusSnapshot.DefaultViewerFreshnessWindow),
                Is.EqualTo(ViewerSampleState.Fresh));
            Assert.That(fresh.EvaluateViewerSample(clock.UtcNow, StreamStatusSnapshot.DefaultViewerFreshnessWindow),
                Is.EqualTo(ViewerSampleState.Stale));
        });
    }

    [Test]
    public async Task StreamInfoCommand_RespondsForCallerPlatform_WhenTwitchOfflineAndYouTubeOnline()
    {
        var clock = new TestTimeProvider
        {
            UtcNow = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero),
        };

        var twitch = new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch");
        twitch.FakeStreamStatus.SetOffline();

        var youtube = new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube");
        youtube.FakeStreamStatus.SetOnline(new()
        {
            Id = "yt-1",
            Title = "Вечерний эфир",
            GameName = "Programming",
            ViewerCount = 45,
            StartedAt = clock.UtcNow.UtcDateTime.AddHours(-1).AddMinutes(-15),
        }, clock.UtcNow);

        var command = new StreamInfoCommand(new ChatPlatformRegistry([twitch, youtube]), clock);

        var ytReply = await command.ExecuteAsync(CreateContext(StreamingPlatform.YouTube), CancellationToken.None);
        var twReply = await command.ExecuteAsync(CreateContext(StreamingPlatform.Twitch), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(ytReply!.Text, Is.EqualTo("🔴 Стрим: Вечерний эфир | Programming | 👥 45 | ⏱ 1ч 15м"));
            Assert.That(twReply!.Text, Is.EqualTo("Сейчас стрим офлайн"));
        });
    }

    [Test]
    public async Task StreamsSummaryCommand_DistinguishesOfflineMissingStaleAndZeroViewers()
    {
        var clock = new TestTimeProvider
        {
            UtcNow = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero),
        };

        var twitch = new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch");
        var youtube = new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube");
        var summary = new StreamsSummaryCommand(new ChatPlatformRegistry([twitch, youtube]), clock);
        var ctx = CreateContext(StreamingPlatform.Twitch);

        twitch.FakeStreamStatus.SetOffline();
        youtube.FakeStreamStatus.SetOnline(new()
        {
            Id = "yt-1",
            Title = "Эфир",
            ViewerCount = 45,
            StartedAt = clock.UtcNow.UtcDateTime.AddMinutes(-30),
        }, clock.UtcNow);
        var offlineAndOnline = await summary.ExecuteAsync(ctx, CancellationToken.None);

        twitch.FakeStreamStatus.SetOnline(new()
        {
            Id = "tw-1",
            Title = "Главный стрим",
            ViewerCount = 120,
            StartedAt = clock.UtcNow.UtcDateTime.AddMinutes(-30),
        }, clock.UtcNow);
        youtube.FakeStreamStatus.SetOnline(new() { Id = "yt-2", ViewerCount = 0 }, null);
        var partial = await summary.ExecuteAsync(ctx, CancellationToken.None);

        twitch.FakeStreamStatus.SetOffline();
        youtube.FakeStreamStatus.SetOnline(new()
        {
            Id = "yt-2",
            Title = "Старый",
            ViewerCount = 30,
            StartedAt = clock.UtcNow.UtcDateTime.AddHours(-1),
        }, clock.UtcNow.AddMinutes(-15));
        var staleOnly = await summary.ExecuteAsync(ctx, CancellationToken.None);

        youtube.FakeStreamStatus.SetOnline(new()
        {
            Id = "yt-3",
            Title = "Без зрителей",
            ViewerCount = 0,
            StartedAt = clock.UtcNow.UtcDateTime.AddMinutes(-10),
        }, clock.UtcNow);
        var zeroFresh = await summary.ExecuteAsync(ctx, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(offlineAndOnline!.Text, Does.Contain("Twitch: офлайн"));
            Assert.That(offlineAndOnline.Text, Does.Contain("YouTube: онлайн"));
            Assert.That(offlineAndOnline.Text, Does.EndWith("Всего по площадкам: 45"));

            Assert.That(partial!.Text, Does.Contain("недоступен"));
            Assert.That(partial.Text, Does.EndWith("Всего по площадкам: от 120 (неполные данные)"));

            Assert.That(staleOnly!.Text, Does.Contain("устарел"));
            Assert.That(staleOnly.Text, Does.EndWith("Всего по площадкам: н/д (неполные данные)"));

            Assert.That(zeroFresh!.Text, Does.Contain("0 зрит."));
            Assert.That(zeroFresh.Text, Does.EndWith("Всего по площадкам: 0"));
        });
    }

    [Test]
    public async Task StreamsAlias_StaysWithStreamsCountCommand_NotStreamsSummaryCommand()
    {
        var clock = new TestTimeProvider
        {
            UtcNow = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero),
        };
        var twitch = new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch");
        twitch.FakeStreamStatus.SetOffline();
        var registry = new ChatPlatformRegistry([twitch]);

        var historyStore = new StreamSessionHistoryStore(NullLogger<StreamSessionHistoryStore>.Instance, Path.Combine(_root, "history.json"));
        var commandSettingsStore = new CommandSettingsStore(null, Path.Combine(_root, "commands.json"));

        var processor = new ChatCommandProcessor(
            [new StreamsSummaryCommand(registry, clock), new StreamsCountCommand(historyStore, clock)],
            commandSettingsStore,
            new(clock),
            NullLogger<ChatCommandProcessor>.Instance,
            platformRegistry: registry);

        var ctx = CreateContext(StreamingPlatform.Twitch);
        var streamsResult = await processor.TryProcessAsync("!streams", ctx, CancellationToken.None);
        var summaryResult = await processor.TryProcessAsync("!стримы", ctx, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(streamsResult.Canonical, Is.EqualTo("стримов"));
            Assert.That(streamsResult.Response!.Text, Is.EqualTo("Истории стримов пока нет"));

            Assert.That(summaryResult.Canonical, Is.EqualTo("стримы"));
            Assert.That(summaryResult.Response!.Text, Is.EqualTo("Twitch: офлайн | Всего по площадкам: офлайн"));
        });
    }

    private static CommandContext CreateContext(StreamingPlatform platform)
    {
        return new()
        {
            Platform = platform,
            Channel = "chan",
            MessageId = "m1",
            UserId = "u1",
            Username = "viewer",
            DisplayName = "Viewer",
        };
    }
}
