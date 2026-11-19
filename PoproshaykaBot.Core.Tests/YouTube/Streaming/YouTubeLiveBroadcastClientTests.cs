using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Core.YouTube.Streaming;
using System.Net;
using System.Text;

namespace PoproshaykaBot.Core.Tests.YouTube.Streaming;

[TestFixture]
public class YouTubeLiveBroadcastClientTests
{
    private string _directory = null!;
    private YouTubeSettingsStore _settingsStore = null!;
    private YouTubeQuotaStore _quotaStore = null!;
    private YouTubeQuotaGovernor _governor = null!;
    private List<HttpRequestMessage> _requests = null!;
    private List<HttpRequestMessage> _broadcasterRequests = null!;
    private List<HttpRequestMessage> _botRequests = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-broadcast-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _requests = [];
        _broadcasterRequests = [];
        _botRequests = [];

        var clock = new TestTimeProvider { UtcNow = new DateTimeOffset(2026, 11, 15, 19, 0, 0, TimeSpan.Zero) };
        _settingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        _settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj", DailyQuotaBudget = 10_000 });
        _quotaStore = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, Path.Combine(_directory, "quota.json"));
        _governor = new YouTubeQuotaGovernor(_quotaStore, _settingsStore, clock, NullLogger<YouTubeQuotaGovernor>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _quotaStore.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public async Task ListActiveBroadcasts_Follows_Pagination_And_Omits_Forbidden_Query_Params()
    {
        var page = 0;
        var client = CreateClient(req =>
        {
            page++;
            var json = page == 1
                ? """
                  {
                    "nextPageToken": "page-2",
                    "items": [
                      {
                        "id": "vid-1",
                        "snippet": {
                          "channelId": "UC0000000000000000000001",
                          "channelTitle": "MainChannel",
                          "title": "Stream One",
                          "liveChatId": "chat-1",
                          "actualStartTime": "2026-11-15T18:00:00Z"
                        }
                      },
                      {
                        "id": "vid-ended",
                        "snippet": {
                          "channelId": "UC0000000000000000000001",
                          "channelTitle": "MainChannel",
                          "title": "Ended Stream",
                          "liveChatId": "chat-ended",
                          "actualStartTime": "2026-11-15T16:00:00Z",
                          "actualEndTime": "2026-11-15T17:00:00Z"
                        }
                      }
                    ]
                  }
                  """
                : """
                  {
                    "items": [
                      {
                        "id": "vid-2",
                        "snippet": {
                          "channelId": "UC0000000000000000000002",
                          "channelTitle": "Other",
                          "title": "Stream Two",
                          "liveChatId": "chat-2",
                          "publishedAt": "2026-11-15T18:30:00Z"
                        }
                      }
                    ]
                  }
                  """;
            return JsonResponse(json);
        });

        var broadcasts = await client.ListActiveBroadcastsAsync(CancellationToken.None);
        var quota = _governor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(broadcasts, Has.Count.EqualTo(2));
            Assert.That(broadcasts.Select(static b => b.VideoId), Is.EqualTo(new[] { "vid-1", "vid-2" }));
            Assert.That(_requests[0].RequestUri!.Query, Does.Contain("broadcastStatus=active"));
            Assert.That(_requests[0].RequestUri!.Query, Does.Not.Contain("mine="));
            Assert.That(_requests[0].RequestUri!.Query, Does.Not.Contain("&id="));
            Assert.That(_requests[1].RequestUri!.Query, Does.Contain("pageToken=page-2"));
            Assert.That(quota.BroadcasterCommittedUnits, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task GetVideoLiveSample_Parses_Viewers_And_Null_When_Hidden()
    {
        var call = 0;
        var client = CreateClient(_ =>
        {
            call++;
            return call == 1
                ? JsonResponse("""
                  {
                    "items": [
                      {
                        "id": "vid-1",
                        "snippet": { "title": "Live Title" },
                        "liveStreamingDetails": {
                          "actualStartTime": "2026-11-15T18:00:00Z",
                          "concurrentViewers": "345"
                        }
                      }
                    ]
                  }
                  """)
                : JsonResponse("""
                  {
                    "items": [
                      {
                        "id": "vid-1",
                        "snippet": { "title": "Hidden Viewers" },
                        "liveStreamingDetails": {
                          "actualStartTime": "2026-11-15T18:00:00Z"
                        }
                      }
                    ]
                  }
                  """);
        });

        var first = await client.GetVideoLiveSampleAsync("vid-1", CancellationToken.None);
        var second = await client.GetVideoLiveSampleAsync("vid-1", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(first!.ConcurrentViewers, Is.EqualTo(345));
            Assert.That(first.Title, Is.EqualTo("Live Title"));
            Assert.That(first.HasLiveStreamingDetails, Is.True);
            Assert.That(second, Is.Not.Null);
            Assert.That(second!.ConcurrentViewers, Is.Null);
            Assert.That(second.HasLiveStreamingDetails, Is.True);
        });
    }

    [Test]
    public void Unauthorized_Refunds_Quota_Lease()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("""{"error":{"code":401}}""", Encoding.UTF8, "application/json"),
        });

        var ex = Assert.ThrowsAsync<YouTubeApiException>(() => client.ResolveChannelIdByHandleAsync("@streamer", CancellationToken.None));
        var quota = _governor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Kind, Is.EqualTo(YouTubeApiErrorKind.Unauthorized));
            Assert.That(quota.CommittedUnits, Is.EqualTo(0));
            Assert.That(quota.ReservedUnits, Is.EqualTo(0));
        });
    }

    [Test]
    public void LocalBudgetExhausted_Throws_LocalQuotaBudgetReached_And_Timeout_Or_BadJson_Throw_Transient()
    {
        var timeoutClient = CreateClient(_ => throw new TaskCanceledException("HTTP timeout"));
        var timeoutEx = Assert.ThrowsAsync<YouTubeApiException>(() => timeoutClient.ListActiveBroadcastsAsync(CancellationToken.None));

        var badJsonClient = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{invalid-json", Encoding.UTF8, "application/json"),
        });
        var jsonEx = Assert.ThrowsAsync<YouTubeApiException>(() => badJsonClient.GetVideoLiveSampleAsync("vid-1", CancellationToken.None));

        _settingsStore.Mutate(s => s.DailyQuotaBudget = YouTubeQuotaPolicy.MinimumDailyBudget);
        var remaining = _governor.CaptureDiagnostics().Tier1RemainingUnits;
        Assert.That(
            _governor.TryReserve(
                PlatformAuthRole.Bot,
                YouTubeQuotaPriority.CriticalReadAndMonitor,
                remaining,
                out var lease),
            Is.True);
        lease.Commit();
        var budgetClient = CreateClient(_ => JsonResponse("""{"items":[]}"""));
        var budgetEx = Assert.ThrowsAsync<YouTubeApiException>(() => budgetClient.ListActiveBroadcastsAsync(CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(timeoutEx!.Kind, Is.EqualTo(YouTubeApiErrorKind.Transient));
            Assert.That(jsonEx!.Kind, Is.EqualTo(YouTubeApiErrorKind.Transient));
            Assert.That(budgetEx!.Kind, Is.EqualTo(YouTubeApiErrorKind.LocalQuotaBudgetReached));
            Assert.That(budgetEx.Reason, Is.EqualTo("localBudgetReached"));
            Assert.That(_governor.CaptureDiagnostics().ExhaustedByServer, Is.False);
        });
    }

    [Test]
    public async Task Bot_Role_Uses_BotApiHttpClient_Reserves_Bot_Quota_And_Maps_Sample_Metadata()
    {
        var client = CreateClient(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("channels", StringComparison.Ordinal))
            {
                return JsonResponse("""{"items":[{"id":"UC_BOT_RESOLVED"}]}""");
            }

            return JsonResponse("""
                {
                  "items": [
                    {
                      "id": "vid-bot-1",
                      "snippet": {
                        "channelId": "UC_TARGET",
                        "channelTitle": "Target Channel",
                        "title": "Bot Sample Title"
                      },
                      "liveStreamingDetails": {
                        "actualStartTime": "2026-11-15T18:00:00Z",
                        "concurrentViewers": "412",
                        "activeLiveChatId": "lc-bot-1"
                      }
                    }
                  ]
                }
                """);
        });

        var resolvedId = await client.ResolveChannelIdByHandleAsync("@target", PlatformAuthRole.Bot, "yt-bot-1", CancellationToken.None);
        var sample = await client.GetVideoLiveSampleAsync("vid-bot-1", PlatformAuthRole.Bot, "yt-bot-1", CancellationToken.None);
        var quota = _governor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(resolvedId, Is.EqualTo("UC_BOT_RESOLVED"));
            Assert.That(sample, Is.Not.Null);
            Assert.That(sample!.ChannelId, Is.EqualTo("UC_TARGET"));
            Assert.That(sample.ChannelTitle, Is.EqualTo("Target Channel"));
            Assert.That(sample.Title, Is.EqualTo("Bot Sample Title"));
            Assert.That(sample.ConcurrentViewers, Is.EqualTo(412));
            Assert.That(sample.ActiveLiveChatId, Is.EqualTo("lc-bot-1"));
            Assert.That(quota.BotCommittedUnits, Is.EqualTo(2));
            Assert.That(quota.BroadcasterCommittedUnits, Is.EqualTo(0));
            Assert.That(_botRequests, Has.Count.EqualTo(2));
            Assert.That(_broadcasterRequests, Is.Empty);
        });
    }

    [Test]
    public void QuotaExceeded_Marks_QuotaGovernor_Exhausted_From_Server()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(
                """{"error":{"code":403,"errors":[{"reason":"quotaExceeded"}]}}""",
                Encoding.UTF8,
                "application/json"),
        });

        var ex = Assert.ThrowsAsync<YouTubeApiException>(() =>
            client.GetVideoLiveSampleAsync("vid-1", PlatformAuthRole.Bot, "yt-bot-1", CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Kind, Is.EqualTo(YouTubeApiErrorKind.QuotaExceeded));
            Assert.That(_governor.CaptureDiagnostics().ExhaustedByServer, Is.True);
        });
    }

    [Test]
    public async Task Two_Arg_Broadcaster_Overloads_Preserve_Ambient_ContextId()
    {
        string? observedResolveContext = null;
        string? observedListContext = null;
        string? observedSampleContext = null;

        var client = CreateClient(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("channels", StringComparison.Ordinal))
            {
                observedResolveContext = PlatformAuthRequestOptions.CurrentContextId;
                return JsonResponse("""{"items":[{"id":"UC_NAMED"}]}""");
            }

            if (path.EndsWith("liveBroadcasts", StringComparison.Ordinal))
            {
                observedListContext = PlatformAuthRequestOptions.CurrentContextId;
                return JsonResponse("""{"items":[]}""");
            }

            observedSampleContext = PlatformAuthRequestOptions.CurrentContextId;
            return JsonResponse("""{"items":[{"id":"vid-1","snippet":{"title":"T"}}]}""");
        });

        using (PlatformAuthRequestOptions.UseContext("yt-named-broadcaster"))
        {
            var channelId = await client.ResolveChannelIdByHandleAsync("@named", CancellationToken.None);
            var broadcasts = await client.ListActiveBroadcastsAsync(CancellationToken.None);
            var sample = await client.GetVideoLiveSampleAsync("vid-1", CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(channelId, Is.EqualTo("UC_NAMED"));
                Assert.That(broadcasts, Is.Empty);
                Assert.That(sample, Is.Not.Null);
                Assert.That(sample!.HasLiveStreamingDetails, Is.False);
                Assert.That(observedResolveContext, Is.EqualTo("yt-named-broadcaster"));
                Assert.That(observedListContext, Is.EqualTo("yt-named-broadcaster"));
                Assert.That(observedSampleContext, Is.EqualTo("yt-named-broadcaster"));
            });
        }
    }

    private YouTubeLiveBroadcastClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(YouTubeEndpoints.BroadcasterApiHttpClient)
            .Returns(_ => new HttpClient(new RecordingHandler(_requests, _broadcasterRequests, handler))
            {
                BaseAddress = YouTubeEndpoints.RestApiBaseUri,
            });
        httpClientFactory.CreateClient(YouTubeEndpoints.BotApiHttpClient)
            .Returns(_ => new HttpClient(new RecordingHandler(_requests, _botRequests, handler))
            {
                BaseAddress = YouTubeEndpoints.RestApiBaseUri,
            });

        return new YouTubeLiveBroadcastClient(httpClientFactory, _governor, NullLogger<YouTubeLiveBroadcastClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class RecordingHandler(
        List<HttpRequestMessage> allRequests,
        List<HttpRequestMessage> roleRequests,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            allRequests.Add(request);
            roleRequests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
