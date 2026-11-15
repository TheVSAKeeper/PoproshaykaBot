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

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-broadcast-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _requests = [];

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
            Assert.That(second, Is.Not.Null);
            Assert.That(second!.ConcurrentViewers, Is.Null);
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

    private YouTubeLiveBroadcastClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(YouTubeEndpoints.BroadcasterApiHttpClient)
            .Returns(_ => new HttpClient(new RecordingHandler(_requests, handler))
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
        List<HttpRequestMessage> requests,
        Func<HttpRequestMessage, HttpResponseMessage> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(request);
            return Task.FromResult(responder(request));
        }
    }
}
