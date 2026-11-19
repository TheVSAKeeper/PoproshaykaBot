using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Tests.YouTube.Streaming;

[TestFixture]
public class InnerTubeLiveDiscoveryParserTests
{
    private const string ChannelId = "UC0000000000000000000001";

    [Test]
    public void Active_Live_Stream_Is_Parsed_With_Metadata()
    {
        var html = """
            <html><head><script>
            var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": {
                "videoId": "vid-live-1",
                "channelId": "UC0000000000000000000001",
                "author": "Streamer Name",
                "title": "Live Stream Title",
                "isLive": true
              },
              "microformat": {
                "playerMicroformatRenderer": {
                  "liveBroadcastDetails": {
                    "isLiveNow": true,
                    "startTimestamp": "2026-11-19T18:00:00+00:00"
                  }
                }
              }
            };
            </script></head></html>
            """;

        var result = InnerTubeLiveDiscoveryParser.ParseChannelLivePage(html, ChannelId);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.VideoId, Is.EqualTo("vid-live-1"));
            Assert.That(result.ChannelId, Is.EqualTo(ChannelId));
            Assert.That(result.ChannelTitle, Is.EqualTo("Streamer Name"));
            Assert.That(result.Title, Is.EqualTo("Live Stream Title"));
            Assert.That(result.StartedAtUtc, Is.EqualTo(new DateTimeOffset(2026, 11, 19, 18, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public void Offline_Channel_Page_Without_PlayerResponse_Returns_Null()
    {
        var html = """
            <html><head><script>var ytInitialData = {"metadata":{"channelMetadataRenderer":{"externalId":"UC0000000000000000000001"}}};</script></head></html>
            """;

        var result = InnerTubeLiveDiscoveryParser.ParseChannelLivePage(html, ChannelId);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Scheduled_Upcoming_And_Ended_Streams_Return_Null()
    {
        var upcomingHtml = """
            <script>window["ytInitialPlayerResponse"] = {
              "playabilityStatus": { "status": "LIVE_STREAM_OFFLINE" },
              "videoDetails": { "videoId": "vid-up", "channelId": "UC0000000000000000000001", "isLive": false }
            };</script>
            """;

        var isLiveNowFalseHtml = """
            <script>ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": { "videoId": "vid-up2", "channelId": "UC0000000000000000000001", "isLive": true },
              "microformat": { "playerMicroformatRenderer": { "liveBroadcastDetails": { "isLiveNow": false } } }
            };</script>
            """;

        var endedHtml = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": { "videoId": "vid-end", "channelId": "UC0000000000000000000001", "isLive": false },
              "microformat": { "playerMicroformatRenderer": { "liveBroadcastDetails": { "isLiveNow": true, "endTimestamp": "2026-11-19T19:00:00+00:00" } } }
            };</script>
            """;

        Assert.Multiple(() =>
        {
            Assert.That(InnerTubeLiveDiscoveryParser.ParseChannelLivePage(upcomingHtml, ChannelId), Is.Null);
            Assert.That(InnerTubeLiveDiscoveryParser.ParseChannelLivePage(isLiveNowFalseHtml, ChannelId), Is.Null);
            Assert.That(InnerTubeLiveDiscoveryParser.ParseChannelLivePage(endedHtml, ChannelId), Is.Null);
        });
    }

    [Test]
    public void Channel_Id_Mismatch_Returns_Null()
    {
        var html = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": { "videoId": "vid-other", "channelId": "UC9999999999999999999999", "isLive": true }
            };</script>
            """;

        var result = InnerTubeLiveDiscoveryParser.ParseChannelLivePage(html, ChannelId);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void Consent_AntiBot_And_Invalid_Responses_Throw_Transient()
    {
        var consentHtml = "<html><a href=\"https://consent.youtube.com/m\">Before you continue to YouTube</a></html>";
        var antiBotHtml = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "LOGIN_REQUIRED", "reason": "Sign in to confirm you're not a bot" }
            };</script>
            """;

        Assert.Multiple(() =>
        {
            var consentEx = Assert.Throws<YouTubeApiException>(() => InnerTubeLiveDiscoveryParser.ParseChannelLivePage(consentHtml, ChannelId));
            var botEx = Assert.Throws<YouTubeApiException>(() => InnerTubeLiveDiscoveryParser.ParseChannelLivePage(antiBotHtml, ChannelId));
            var badEx = Assert.Throws<YouTubeApiException>(() => InnerTubeLiveDiscoveryParser.ParseChannelLivePage("<html><body>502 Bad Gateway</body></html>", ChannelId));
            var emptyEx = Assert.Throws<YouTubeApiException>(() => InnerTubeLiveDiscoveryParser.ParseChannelLivePage("   ", ChannelId));

            Assert.That(consentEx!.Kind, Is.EqualTo(YouTubeApiErrorKind.Transient));
            Assert.That(botEx!.Kind, Is.EqualTo(YouTubeApiErrorKind.Transient));
            Assert.That(badEx!.Kind, Is.EqualTo(YouTubeApiErrorKind.Transient));
            Assert.That(emptyEx!.Kind, Is.EqualTo(YouTubeApiErrorKind.Transient));
        });
    }

    [Test]
    public void Escaped_Quotes_And_Braces_In_Title_Parse_Cleanly()
    {
        var html = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": {
                "videoId": "vid-esc",
                "channelId": "UC0000000000000000000001",
                "author": "Author",
                "title": "Test } \" { \\\" title",
                "isLive": true
              }
            };</script>
            """;

        var result = InnerTubeLiveDiscoveryParser.ParseChannelLivePage(html, ChannelId);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.VideoId, Is.EqualTo("vid-esc"));
            Assert.That(result.Title, Is.EqualTo("Test } \" { \\\" title"));
        });
    }

    [Test]
    public void Decoy_Assignment_Prefix_Identifier_And_Bracket_Whitespace_Parse_Real_PlayerResponse()
    {
        var html = """
            <script>
            var myytInitialPlayerResponse = {"playabilityStatus":{"status":"OK"},"videoDetails":{"videoId":"wrong","channelId":"UC0000000000000000000001","isLive":true}};
            var ytInitialPlayerResponse = {};
            window["ytInitialPlayerResponse" ] = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": {
                "videoId": "vid-real",
                "channelId": "UC0000000000000000000001",
                "author": "RealChannel",
                "title": "Real Stream",
                "isLive": true
              }
            };
            </script>
            """;

        var result = InnerTubeLiveDiscoveryParser.ParseChannelLivePage(html, ChannelId);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.VideoId, Is.EqualTo("vid-real"));
    }

    [Test]
    public void Title_Containing_Consent_Phrase_Still_Parses_And_Localized_LoginRequired_Or_Bare_Ytcfg_Throw_Transient()
    {
        var titleWithConsentPhrase = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": {
                "videoId": "vid-phrase",
                "channelId": "UC0000000000000000000001",
                "title": "Before you continue to YouTube stream",
                "isLive": true
              }
            };</script>
            """;
        var localizedLoginRequired = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "LOGIN_REQUIRED", "reason": "Войдите в аккаунт, чтобы подтвердить, что вы не бот" }
            };</script>
            """;
        var privateVideo = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "LOGIN_REQUIRED", "reason": "Private video" }
            };</script>
            """;
        var bareYtcfgChallenge = "<script>ytcfg.set({'INNERTUBE_API_KEY':'x'});</script>";

        var parsed = InnerTubeLiveDiscoveryParser.ParseChannelLivePage(titleWithConsentPhrase, ChannelId);

        Assert.Multiple(() =>
        {
            Assert.That(parsed?.VideoId, Is.EqualTo("vid-phrase"));
            Assert.That(InnerTubeLiveDiscoveryParser.ParseChannelLivePage(privateVideo, ChannelId), Is.Null);
            Assert.That(
                Assert.Throws<YouTubeApiException>(() => InnerTubeLiveDiscoveryParser.ParseChannelLivePage(localizedLoginRequired, ChannelId))!.Kind,
                Is.EqualTo(YouTubeApiErrorKind.Transient));
            Assert.That(
                Assert.Throws<YouTubeApiException>(() => InnerTubeLiveDiscoveryParser.ParseChannelLivePage(bareYtcfgChallenge, ChannelId))!.Kind,
                Is.EqualTo(YouTubeApiErrorKind.Transient));
        });
    }

    [Test]
    public void Missing_ChannelId_IsUpcoming_Or_IsReplay_Return_Null()
    {
        var missingChannelId = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": { "videoId": "vid-1", "isLive": true }
            };</script>
            """;
        var isUpcoming = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": { "videoId": "vid-2", "channelId": "UC0000000000000000000001", "isLive": true, "isUpcoming": true }
            };</script>
            """;
        var isReplay = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": { "videoId": "vid-3", "channelId": "UC0000000000000000000001", "isLive": true, "isReplay": true }
            };</script>
            """;

        Assert.Multiple(() =>
        {
            Assert.That(InnerTubeLiveDiscoveryParser.ParseChannelLivePage(missingChannelId, ChannelId), Is.Null);
            Assert.That(InnerTubeLiveDiscoveryParser.ParseChannelLivePage(isUpcoming, ChannelId), Is.Null);
            Assert.That(InnerTubeLiveDiscoveryParser.ParseChannelLivePage(isReplay, ChannelId), Is.Null);
        });
    }

    [Test]
    public void Html_Comment_Decoy_Wrong_Channel_Browse_Unpaired_Surrogate_And_Repeated_Malformed_Are_Handled()
    {
        var htmlWithCommentDecoy = """
            <!-- <script>var ytInitialPlayerResponse = {"playabilityStatus":{"status":"LIVE_STREAM_OFFLINE"},"videoDetails":{"videoId":"fake","channelId":"UC0000000000000000000001"}};</script> -->
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": { "videoId": "vid-after-comment", "channelId": "UC0000000000000000000001", "title": "Real", "isLive": true }
            };</script>
            """;

        var wrongChannelBrowse = """
            <html><script>var ytInitialData = {"metadata":{"channelMetadataRenderer":{"externalId":"UC9999999999999999999999"}}};</script></html>
            """;

        var unpairedSurrogate = """
            <script>var ytInitialPlayerResponse = {
              "playabilityStatus": { "status": "OK" },
              "videoDetails": { "videoId": "vid-bad-utf", "channelId": "UC0000000000000000000001", "title": "\uD800", "isLive": true }
            };</script>
            """;

        var repeatedMalformed = "<script>" + string.Concat(Enumerable.Repeat("var ytInitialPlayerResponse = {", 200)) + "</script>";

        var afterComment = InnerTubeLiveDiscoveryParser.ParseChannelLivePage(htmlWithCommentDecoy, ChannelId);

        Assert.Multiple(() =>
        {
            Assert.That(afterComment?.VideoId, Is.EqualTo("vid-after-comment"));
            Assert.That(
                Assert.Throws<YouTubeApiException>(() => InnerTubeLiveDiscoveryParser.ParseChannelLivePage(wrongChannelBrowse, ChannelId))!.Kind,
                Is.EqualTo(YouTubeApiErrorKind.Transient));
            Assert.That(
                Assert.Throws<YouTubeApiException>(() => InnerTubeLiveDiscoveryParser.ParseChannelLivePage(unpairedSurrogate, ChannelId))!.Kind,
                Is.EqualTo(YouTubeApiErrorKind.Transient));
            Assert.That(
                Assert.Throws<YouTubeApiException>(() => InnerTubeLiveDiscoveryParser.ParseChannelLivePage(repeatedMalformed, ChannelId))!.Kind,
                Is.EqualTo(YouTubeApiErrorKind.Transient));
        });
    }
}
