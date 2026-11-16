using System.Text.Json;
using PoproshaykaBot.Core.Tests.YouTube.Fixtures;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube.Models;
using PoproshaykaBot.Core.YouTube.Chat.Transport;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
internal sealed class InnerTubeLiveChatParserTests
{
    [Test]
    public void ParseWatchBootstrap_Extracts_ApiKey_ClientVersion_And_Continuation()
    {
        var html = InnerTubeFixtureLoader.ReadText("watch_live_chat_selector.html");

        var result = InnerTubeLiveChatParser.ParseWatchBootstrap(html, "SYNTHVID001");

        Assert.Multiple(() =>
        {
            Assert.That(result.VideoId, Is.EqualTo("SYNTHVID001"));
            Assert.That(result.ApiKey, Is.EqualTo("SYNTHETIC_API_KEY_001"));
            Assert.That(result.ClientVersion, Is.EqualTo("2.20261116.01.00"));
            Assert.That(result.InitialContinuation, Is.EqualTo("SYNTHETIC_BOOTSTRAP_TOP_CHAT_CONT_001"));
        });
    }

    [Test]
    public void ParseWatchBootstrap_Classifies_Consent_MembersOnly_And_LoginRequired()
    {
        using var doc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("errors_429_403_consent.json"));
        var root = doc.RootElement;

        var consentEx = Assert.Throws<InnerTubeChatException>(() =>
            InnerTubeLiveChatParser.ParseWatchBootstrap(root.GetProperty("consentHtml").GetString()!, "SYNTHVID001"));
        var membersEx = Assert.Throws<InnerTubeChatException>(() =>
            InnerTubeLiveChatParser.ParseWatchBootstrap(root.GetProperty("membersOnlyHtml").GetString()!, "SYNTHVID001"));
        var loginEx = Assert.Throws<InnerTubeChatException>(() =>
            InnerTubeLiveChatParser.ParseWatchBootstrap(root.GetProperty("loginRequiredHtml").GetString()!, "SYNTHVID001"));

        Assert.Multiple(() =>
        {
            Assert.That(consentEx!.Kind, Is.EqualTo(InnerTubeErrorKind.ConsentRequired));
            Assert.That(membersEx!.Kind, Is.EqualTo(InnerTubeErrorKind.MembersOnly));
            Assert.That(loginEx!.Kind, Is.EqualTo(InnerTubeErrorKind.LoginRequired));
        });
    }

    [Test]
    public void ParseBatch_Extracts_LiveChatSwitch_Filters_Ticker_And_Maps_RichRuns()
    {
        var response = JsonSerializer.Deserialize(
            InnerTubeFixtureLoader.ReadText("initial_mixed_with_ticker.json"),
            InnerTubeJsonSerializerContext.Default.LiveChatResponse);

        var parsed = InnerTubeLiveChatParser.ParseBatch(response, retainRawPayloads: false);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.LiveChatSwitchContinuation, Is.EqualTo("SYNTHETIC_LIVE_SWITCH_CONT_001"));
            Assert.That(parsed.NextContinuation, Is.EqualTo("SYNTHETIC_INV_CONT_002"));
            Assert.That(parsed.PollingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(3250)));
            Assert.That(parsed.Items, Has.Count.EqualTo(1));
        });

        var item = parsed.Items[0];
        Assert.Multiple(() =>
        {
            Assert.That(item.Id, Is.EqualTo("ChwSYNTHETIC_TEXT_MSG_001"));
            Assert.That(item.Kind, Is.EqualTo(YouTubeLiveChatItemKind.TextMessage));
            Assert.That(item.Author.IsChatModerator, Is.True);
            Assert.That(item.Author.IsChatSponsor, Is.True);
            Assert.That(item.Author.AvatarUrl, Is.EqualTo("https://yt3.ggpht.com/synthetic/mod-avatar-64.png"));
            Assert.That(item.Runs, Has.Count.EqualTo(2));
            Assert.That(item.Runs![1].Kind, Is.EqualTo(YouTubeChatRunKind.Emoji));
            Assert.That(item.Runs[1].IsCustomEmoji, Is.True);
            Assert.That(item.RawPayloadJson, Is.Null);
        });
    }

    [Test]
    public void ParseBatch_Maps_SuperChat_Sticker_Membership_Gifts_And_Moderation_Actions()
    {
        var response = JsonSerializer.Deserialize(
            InnerTubeFixtureLoader.ReadText("rich_events_superchat_membership_mod.json"),
            InnerTubeJsonSerializerContext.Default.LiveChatResponse);

        var parsed = InnerTubeLiveChatParser.ParseBatch(response, retainRawPayloads: true);

        Assert.Multiple(() =>
        {
            Assert.That(parsed.Items, Has.Count.EqualTo(8));
            Assert.That(parsed.PollingInterval, Is.EqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(parsed.Items[0].Kind, Is.EqualTo(YouTubeLiveChatItemKind.SuperChat));
            Assert.That(parsed.Items[0].Monetary?.Currency, Is.EqualTo("USD"));
            Assert.That(parsed.Items[0].Monetary?.AmountValue, Is.EqualTo(25.00m));
            Assert.That(parsed.Items[1].Kind, Is.EqualTo(YouTubeLiveChatItemKind.SuperSticker));
            Assert.That(parsed.Items[1].Monetary?.StickerAltText, Is.EqualTo("Cheers sticker"));
            Assert.That(parsed.Items[2].Kind, Is.EqualTo(YouTubeLiveChatItemKind.MemberMilestoneChat));
            Assert.That(parsed.Items[2].Membership?.MilestoneMonths, Is.EqualTo(6));
            Assert.That(parsed.Items[3].Kind, Is.EqualTo(YouTubeLiveChatItemKind.MembershipGifting));
            Assert.That(parsed.Items[3].Membership?.GiftCount, Is.EqualTo(5));
            Assert.That(parsed.Items[4].Kind, Is.EqualTo(YouTubeLiveChatItemKind.GiftMembershipReceived));
            Assert.That(parsed.Items[5].Moderation?.ActionKind, Is.EqualTo(YouTubeModerationActionKind.MessageDeleted));
            Assert.That(parsed.Items[6].Moderation?.ActionKind, Is.EqualTo(YouTubeModerationActionKind.AuthorMessagesDeleted));
            Assert.That(parsed.Items[7].Kind, Is.EqualTo(YouTubeLiveChatItemKind.Other));
            Assert.That(parsed.Items[7].Moderation?.ActionKind, Is.EqualTo(YouTubeModerationActionKind.MessageReplaced));
            Assert.That(parsed.Items[7].Moderation?.ReplacementItem?.MessageText, Is.EqualTo("[deleted]"));
            Assert.That(parsed.Items[0].RawPayloadJson, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void ParseBatch_Distinguishes_Reload_From_LiveChatReplay_Ended()
    {
        using var doc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("reload_and_ended.json"));
        var reloadResponse = JsonSerializer.Deserialize(
            doc.RootElement.GetProperty("reloadResponse").GetRawText(),
            InnerTubeJsonSerializerContext.Default.LiveChatResponse);
        var endedResponse = JsonSerializer.Deserialize(
            doc.RootElement.GetProperty("endedResponse").GetRawText(),
            InnerTubeJsonSerializerContext.Default.LiveChatResponse);

        var reloadBatch = InnerTubeLiveChatParser.ParseBatch(reloadResponse);
        var endedBatch = InnerTubeLiveChatParser.ParseBatch(endedResponse);

        Assert.Multiple(() =>
        {
            Assert.That(reloadBatch.ContinuationKind, Is.EqualTo(InnerTubeContinuationKind.Reload));
            Assert.That(reloadBatch.NextContinuation, Is.EqualTo("SYNTHETIC_RELOAD_RESET_TOKEN"));
            Assert.That(endedBatch.ContinuationKind, Is.EqualTo(InnerTubeContinuationKind.Ended));
            Assert.That(endedBatch.NextContinuation, Is.Null);
            Assert.That(endedBatch.Items.Any(static i => i.Kind == YouTubeLiveChatItemKind.ChatEnded), Is.True);
        });
    }
}
