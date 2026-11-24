using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.YouTube.Chat;
using PoproshaykaBot.Core.YouTube.Chat.Transport;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
public class YouTubeChatMessageMapperTests
{
    [Test]
    public void Maps_TextMessage_Normalizes_Whitespace_Roles_Badges_And_Preserves_Emoji()
    {
        var mapper = new YouTubeChatMessageMapper();
        var item = new YouTubeLiveChatItem(
            "msg-1",
            YouTubeLiveChatItemKind.TextMessage,
            DateTimeOffset.UtcNow,
            "  Привет\r\nчат\u0001 \U0001F525 :custom_emote:  ",
            string.Empty,
            new("UCviewer1", "@StreamerFan", IsChatOwner: false, IsChatModerator: true, IsChatSponsor: true, IsVerified: true),
            "textMessageEvent");

        var result = mapper.Map(item, "UCchannel", "UCbot", "UCbroadcaster");

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(YouTubeChatMapKind.ChatMessage));
            Assert.That(result.Message, Is.Not.Null);
            Assert.That(result.Message!.Platform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(result.Message.Message, Is.EqualTo("Привет чат \U0001F525 :custom_emote:"));
            Assert.That(result.Message.Username, Is.EqualTo("StreamerFan"));
            Assert.That(result.Message.DisplayName, Is.EqualTo("@StreamerFan"));
            Assert.That(result.Message.IsBroadcaster, Is.False);
            Assert.That(result.Message.IsModerator, Is.True);
            Assert.That(result.Message.IsSubscriber, Is.True);
            Assert.That(result.Message.IsVip, Is.False);
            Assert.That(result.Message.IsBot, Is.False);
            Assert.That(result.Message.Badges, Is.EqualTo(new[] { ("moderator", "1"), ("subscriber", "1") }));
        });
    }

    [TestCase(nameof(YouTubeLiveChatItemKind.SuperChat), "superChatEvent")]
    [TestCase(nameof(YouTubeLiveChatItemKind.SuperSticker), "superStickerEvent")]
    [TestCase(nameof(YouTubeLiveChatItemKind.NewSponsor), "newSponsorEvent")]
    [TestCase(nameof(YouTubeLiveChatItemKind.Gift), "giftEvent")]
    public void Ignores_Monetization_And_Membership_Events_Without_ChatMessage(
        string kindName,
        string rawType)
    {
        var kind = Enum.Parse<YouTubeLiveChatItemKind>(kindName);
        var mapper = new YouTubeChatMessageMapper();
        var item = new YouTubeLiveChatItem(
            "evt-1",
            kind,
            DateTimeOffset.UtcNow,
            "super message",
            "super display",
            new("UCdonor", "Donor", false, false, false, false),
            rawType);

        var result = mapper.Map(item, "UCchannel", "UCbot", "UCbroadcaster");

        Assert.Multiple(() =>
        {
            Assert.That(result.Kind, Is.EqualTo(YouTubeChatMapKind.IgnoredMonetizationOrMembership));
            Assert.That(result.Message, Is.Null);
            Assert.That(result.DiagnosticEventType, Is.EqualTo(rawType));
        });
    }

    [Test]
    public void Dedicated_Bot_Is_Tagged_IsBot_While_Shared_Broadcaster_Is_Not_Unless_Tracked()
    {
        var tracker = new StubTracker();
        var mapper = new YouTubeChatMessageMapper(tracker);
        var botItem = new YouTubeLiveChatItem(
            "bot-msg-1",
            YouTubeLiveChatItemKind.TextMessage,
            DateTimeOffset.UtcNow,
            "!hello",
            "!hello",
            new("UCbot", "MyBot", IsChatOwner: false, IsChatModerator: true, IsChatSponsor: false, IsVerified: false),
            "textMessageEvent");

        var dedicated = mapper.Map(botItem, "UCbroadcaster", "UCbot", "UCbroadcaster");
        var sharedUntracked = mapper.Map(
            botItem with { Author = new("UCbroadcaster", "Owner", true, false, false, false) },
            "UCbroadcaster",
            "UCbroadcaster",
            "UCbroadcaster");

        tracker.TrackSent("bot-msg-1");
        var sharedTracked = mapper.Map(
            botItem with { Author = new("UCbroadcaster", "Owner", true, false, false, false) },
            "UCbroadcaster",
            "UCbroadcaster",
            "UCbroadcaster");

        Assert.Multiple(() =>
        {
            Assert.That(dedicated.Message!.IsBot, Is.True);
            Assert.That(sharedUntracked.Message!.IsBot, Is.False);
            Assert.That(sharedTracked.Message!.IsBot, Is.True);
        });
    }

    private sealed class StubTracker : IYouTubeSentMessageTracker
    {
        private readonly HashSet<string> _ids = new(StringComparer.Ordinal);

        public bool Contains(string messageId, string? liveChatId = null) => _ids.Contains(messageId);

        public void TrackSent(string messageId, string? liveChatId = null) => _ids.Add(messageId);

        public YouTubeInFlightSendScope BeginInFlightSend(string liveChatId, string? expectedNormalizedText) => new();

        public long CaptureInFlightWatermark(string liveChatId) => 0;

        public Task WaitUntilCompletedOrMatchedAsync(
            string liveChatId,
            long watermarkSeq,
            string candidateMessageId,
            TimeSpan timeout,
            TimeProvider timeProvider,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
