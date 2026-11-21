using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Tests.Chat;

[TestFixture]
public sealed class ChatHistoryChannelIsolationTests
{
    private DirectoryInfo _dir = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Directory.CreateTempSubdirectory("chat-history-iso");
    }

    [TearDown]
    public void TearDown()
    {
        _dir.Delete(recursive: true);
    }

    [Test]
    public async Task ActiveChannelChanged_SwitchesActiveHistory_AndIgnoresLateMessagesFromPreviousChannel()
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_dir.FullName, "settings.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(_dir.FullName, "channels.json"));
        catalog.Save(new()
        {
            Channels =
            [
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "100", DisplayName = "chanA", IsConfigured = true },
                new() { Platform = StreamingPlatform.Twitch, ChannelId = "200", DisplayName = "chanB", IsConfigured = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_A", DisplayName = "@ytA", IsConfigured = true },
                new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC_B", DisplayName = "@ytB", IsConfigured = true },
            ],
            Platforms =
            [
                new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "100" },
                new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = "UC_A" },
            ],
        });

        var t0 = new DateTime(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
        using var manager = new ChatHistoryManager(settings, bus, channelCatalogStore: catalog);

        manager.AddMessage(MakeMessage(StreamingPlatform.Twitch, "100", "chanA", "tw-a1", t0, "A-msg"));
        manager.AddMessage(MakeMessage(StreamingPlatform.YouTube, "UC_A", "@ytA", "yt-a1", t0.AddSeconds(1), "YTA-msg", "live-a"));
        manager.AddMessage(MakeMessage(StreamingPlatform.Twitch, "200", "chanB", "tw-b1", t0.AddSeconds(2), "B-msg"));

        Assert.That(manager.GetActiveHistory().Select(static m => m.Message), Is.EqualTo(new[] { "A-msg", "YTA-msg" }));

        await bus.PublishAsync(new ActiveChannelChanged(
            StreamingPlatform.Twitch,
            new ChannelKey(StreamingPlatform.Twitch, "100"),
            new ChannelKey(StreamingPlatform.Twitch, "200"),
            "chanB",
            ChannelSwitchMode.Manual));

        Assert.That(manager.GetActiveHistory().Select(static m => m.Message), Is.EqualTo(new[] { "YTA-msg", "B-msg" }));

        manager.AddMessage(MakeMessage(StreamingPlatform.Twitch, "100", "chanA", "tw-a-late", t0.AddSeconds(5), "A-late"));

        Assert.Multiple(() =>
        {
            Assert.That(manager.GetActiveHistory().Select(static m => m.Message), Is.EqualTo(new[] { "YTA-msg", "B-msg" }));
            Assert.That(manager.IsActiveScope(ChatHistoryScope.ForTwitch("100", "chanA")), Is.False);
            Assert.That(manager.IsActiveScope(ChatHistoryScope.ForTwitch("200", "chanB")), Is.True);
        });

        await bus.PublishAsync(new ActiveChannelChanged(
            StreamingPlatform.YouTube,
            new ChannelKey(StreamingPlatform.YouTube, "UC_A"),
            new ChannelKey(StreamingPlatform.YouTube, "UC_B"),
            "@ytB",
            ChannelSwitchMode.Manual));

        Assert.That(manager.GetActiveHistory().Select(static m => m.Message), Is.EqualTo(new[] { "B-msg" }));
    }

    private static ChatMessageData MakeMessage(
        StreamingPlatform platform,
        string channelId,
        string channel,
        string id,
        DateTime timestampUtc,
        string text,
        string? chatSessionId = null)
    {
        return new()
        {
            Platform = platform,
            Channel = channel,
            ChannelId = channelId,
            ChatSessionId = chatSessionId,
            MessageId = id,
            Timestamp = timestampUtc,
            UserId = "u1",
            DisplayName = "User",
            Message = text,
        };
    }
}
