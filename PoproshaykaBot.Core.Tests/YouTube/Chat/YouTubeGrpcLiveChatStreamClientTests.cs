using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Chat;
using PoproshaykaBot.Core.YouTube.Chat.Proto;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Quota;
using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using ProtoMessageType = PoproshaykaBot.Core.YouTube.Chat.Proto.LiveChatMessageSnippet.Types.TypeWrapper.Types.Type;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
public class YouTubeGrpcLiveChatStreamClientTests
{
    private string _directory = null!;
    private YouTubeSettingsStore _settingsStore = null!;
    private YouTubeQuotaStore _quotaStore = null!;
    private YouTubeQuotaGovernor _governor = null!;
    private StubGrpcAuthService _authService = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"yt-grpc-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        var clock = new TestTimeProvider { UtcNow = new DateTimeOffset(2026, 11, 15, 20, 0, 0, TimeSpan.Zero) };
        _settingsStore = new YouTubeSettingsStore(NullLogger<YouTubeSettingsStore>.Instance, Path.Combine(_directory, "youtube.json"));
        _settingsStore.Save(new YouTubeSettings { Enabled = true, ProjectId = "proj", DailyQuotaBudget = 10_000 });
        _quotaStore = new YouTubeQuotaStore(NullLogger<YouTubeQuotaStore>.Instance, Path.Combine(_directory, "quota.json"));
        _governor = new YouTubeQuotaGovernor(_quotaStore, _settingsStore, clock, NullLogger<YouTubeQuotaGovernor>.Instance);

        _authService = new StubGrpcAuthService();
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
    public async Task StreamBatchesAsync_Maps_Multiple_Frames_And_Commits_Quota_Once()
    {
        var frame1 = new LiveChatMessageListResponse
        {
            NextPageToken = "tok-1",
            PollingIntervalMillis = 7000,
        };
        frame1.Items.Add(new LiveChatMessage
        {
            Id = "id-1",
            Snippet = new LiveChatMessageSnippet
            {
                Type = ProtoMessageType.TextMessageEvent,
                PublishedAt = "2026-11-15T20:00:01Z",
                TextMessageDetails = new LiveChatTextMessageDetails { MessageText = "hello proto" },
            },
            AuthorDetails = new LiveChatMessageAuthorDetails
            {
                ChannelId = "UCauthor1",
                DisplayName = "ModUser",
                IsChatModerator = true,
                IsChatSponsor = true,
                IsVerified = true,
            },
        });

        var frame2 = new LiveChatMessageListResponse
        {
            NextPageToken = "tok-2",
            PollingIntervalMillis = 3000,
        };
        frame2.Items.Add(new LiveChatMessage
        {
            Id = "id-end",
            Snippet = new LiveChatMessageSnippet
            {
                Type = ProtoMessageType.ChatEndedEvent,
                PublishedAt = "2026-11-15T20:00:05Z",
            },
        });

        var handler = new FakeGrpcHttpHandler((_, _) =>
        {
            var payload = EncodeFrames(frame1, frame2);
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent(payload),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", "0");
            return Task.FromResult(response);
        });

        using var provider = new YouTubeGrpcChannelProvider(handler);
        var client = new YouTubeGrpcLiveChatStreamClient(provider, _authService, _governor, NullLogger<YouTubeGrpcLiveChatStreamClient>.Instance);

        var batches = new List<YouTubeLiveChatBatch>();

        await foreach (var batch in client.StreamBatchesAsync("chat-1", null, CancellationToken.None))
        {
            batches.Add(batch);
        }

        var quota = _governor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(batches, Has.Count.EqualTo(2));
            Assert.That(batches[0].NextPageToken, Is.EqualTo("tok-1"));
            Assert.That(batches[0].PollingInterval, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(batches[0].Items[0].MessageText, Is.EqualTo("hello proto"));
            Assert.That(batches[0].Items[0].Author.IsChatModerator, Is.True);
            Assert.That(batches[0].Items[0].Author.IsChatSponsor, Is.True);
            Assert.That(batches[0].Items[0].Author.IsVerified, Is.True);
            Assert.That(batches[1].Items[0].Kind, Is.EqualTo(YouTubeLiveChatItemKind.ChatEnded));
            Assert.That(quota.BotCommittedUnits, Is.EqualTo(5));
            Assert.That(quota.ReservedUnits, Is.EqualTo(0));
        });
    }

    [Test]
    public void Unauthenticated_Status_Clears_Token_And_Refunds_Quota()
    {
        var handler = new FakeGrpcHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = HttpVersion.Version20,
                Content = new ByteArrayContent([]),
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");
            response.TrailingHeaders.Add("grpc-status", "16");
            response.TrailingHeaders.Add("grpc-message", "Invalid auth token");
            return Task.FromResult(response);
        });

        using var provider = new YouTubeGrpcChannelProvider(handler);
        var client = new YouTubeGrpcLiveChatStreamClient(provider, _authService, _governor, NullLogger<YouTubeGrpcLiveChatStreamClient>.Instance);

        var ex = Assert.ThrowsAsync<YouTubeApiException>(async () =>
        {
            await foreach (var _ in client.StreamBatchesAsync("chat-1", null, CancellationToken.None))
            {
            }
        });

        var quota = _governor.CaptureDiagnostics();

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Kind, Is.EqualTo(YouTubeApiErrorKind.Unauthorized));
            Assert.That(_authService.ClearedTokens, Is.EqualTo(new[] { (PlatformAuthRole.Bot, "bot-grpc-token") }));
            Assert.That(quota.CommittedUnits, Is.EqualTo(0));
            Assert.That(quota.ReservedUnits, Is.EqualTo(0));
        });
    }

    [Test]
    public void Caller_Cancellation_Rethrows_OperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        var handler = new FakeGrpcHttpHandler(async (_, ct) =>
        {
            await cts.CancelAsync().ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return await Task.FromCanceled<HttpResponseMessage>(ct).ConfigureAwait(false);
        });

        using var provider = new YouTubeGrpcChannelProvider(handler);
        var client = new YouTubeGrpcLiveChatStreamClient(provider, _authService, _governor, NullLogger<YouTubeGrpcLiveChatStreamClient>.Instance);

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.StreamBatchesAsync("chat-1", null, cts.Token))
            {
            }
        });

        Assert.That(_governor.CaptureDiagnostics().CommittedUnits, Is.EqualTo(0));
    }

    private static byte[] EncodeFrames(params IMessage[] messages)
    {
        using var ms = new MemoryStream();
        var header = new byte[5];

        foreach (var message in messages)
        {
            var payload = message.ToByteArray();
            header[0] = 0;
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1, 4), (uint)payload.Length);
            ms.Write(header);
            ms.Write(payload);
        }

        return ms.ToArray();
    }

    private sealed class FakeGrpcHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return responder(request, cancellationToken);
        }
    }

    private sealed class StubGrpcAuthService : IPlatformAuthService
    {
        public List<(PlatformAuthRole Role, string Token)> ClearedTokens { get; } = [];

        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public bool HasUsableAccessToken(PlatformAuthRole role) => true;

        public ValueTask<string?> GetAccessTokenAsync(PlatformAuthRole role, bool forceRefresh = false, CancellationToken ct = default)
            => ValueTask.FromResult<string?>("bot-grpc-token");

        public bool TryClearAccessToken(PlatformAuthRole role, string failedAccessToken)
        {
            ClearedTokens.Add((role, failedAccessToken));
            return true;
        }

        public void ResetRetryState(PlatformAuthRole role)
        {
        }
    }
}
