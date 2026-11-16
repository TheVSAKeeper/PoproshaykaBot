using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Tests.YouTube.Fixtures;
using PoproshaykaBot.Core.YouTube.Chat;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using PoproshaykaBot.Core.YouTube.Client;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
internal sealed class CompositeYouTubeLiveChatReaderTests
{
    [Test]
    public async Task SelectTransport_And_Cooldown_Fallback_And_Recovery_Work_As_Expected()
    {
        using var errorsDoc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("errors_429_403_consent.json"));
        var watchHtml = InnerTubeFixtureLoader.ReadText("watch_live_chat_selector.html");
        var initialJson = InnerTubeFixtureLoader.ReadText("initial_mixed_with_ticker.json");
        var body429 = errorsDoc.RootElement.GetProperty("rateLimited429Body").GetString()!;

        var call = 0;
        using var handler = new QueueHttpMessageHandler((_, _) =>
        {
            call++;
            return call switch
            {
                1 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
                2 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.TooManyRequests, body429, "application/json"),
                3 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
                _ => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, initialJson, "application/json"),
            };
        });

        var clock = new TestTimeProvider { UtcNow = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero) };
        var innerTube = new InnerTubeYouTubeLiveChatReader(
            new InnerTubeLiveChatClient(new SingleHandlerHttpClientFactory(handler)),
            timeProvider: clock);

        var grpcCalls = 0;
        var grpc = new OfficialGrpcYouTubeLiveChatReader(new StubStreamClient((_, ct) =>
        {
            grpcCalls++;
            return SingleBatch(new([CreateOfficialItem("off-1", clock.UtcNow)], "off-tok-1", TimeSpan.FromSeconds(5), null), ct);
        }));
        var rest = new OfficialRestYouTubeLiveChatReader(new StubPollingClient((_, _) =>
            Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null))), clock);

        var composite = new CompositeYouTubeLiveChatReader(
            innerTube,
            grpc,
            rest,
            clock,
            NullLogger<CompositeYouTubeLiveChatReader>.Instance);

        var target = new YouTubeChatTarget("SYNTHVID001", "chat-1");
        Assert.That(composite.SelectTransport(target), Is.EqualTo(YouTubeChatTransportKind.InnerTube));

        var received = new List<YouTubeLiveChatBatch>();

        await foreach (var batch in composite.ReadBatchesAsync(target, cursor: null, maxBatches: 1, CancellationToken.None))
        {
            received.Add(batch);
        }

        Assert.Multiple(() =>
        {
            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0].Transport, Is.EqualTo(YouTubeChatTransportKind.OfficialGrpc));
            Assert.That(grpcCalls, Is.EqualTo(1));
            Assert.That(composite.SelectTransport(target), Is.EqualTo(YouTubeChatTransportKind.OfficialGrpc));
        });

        clock.UtcNow = clock.UtcNow.AddMinutes(10);

        Assert.That(composite.SelectTransport(target), Is.EqualTo(YouTubeChatTransportKind.InnerTube));
    }

    [Test]
    public async Task SelectTransport_And_ReadBatchesAsync_Skip_InnerTube_When_RequiresOfficialMessageIds()
    {
        using var handler = new QueueHttpMessageHandler((_, _) => throw new InvalidOperationException());
        var clock = new TestTimeProvider();
        var innerTube = new InnerTubeYouTubeLiveChatReader(new InnerTubeLiveChatClient(new SingleHandlerHttpClientFactory(handler)), timeProvider: clock);
        var grpc = new OfficialGrpcYouTubeLiveChatReader(new StubStreamClient((_, ct) =>
            SingleBatch(new([CreateOfficialItem("LCC.1", clock.UtcNow)], "off-1", TimeSpan.FromSeconds(5), null), ct)));
        var rest = new OfficialRestYouTubeLiveChatReader(new StubPollingClient((_, _) => Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null))), clock);

        var composite = new CompositeYouTubeLiveChatReader(innerTube, grpc, rest, clock);
        var sharedTarget = new YouTubeChatTarget("SYNTHVID001", "chat-1", RequiresOfficialMessageIds: true);

        Assert.That(composite.SelectTransport(sharedTarget), Is.EqualTo(YouTubeChatTransportKind.OfficialGrpc));

        var items = new List<YouTubeLiveChatBatch>();
        await foreach (var b in composite.ReadBatchesAsync(sharedTarget, cursor: null, maxBatches: 1, CancellationToken.None))
        {
            items.Add(b);
        }

        Assert.Multiple(() =>
        {
            Assert.That(items, Has.Count.EqualTo(1));
            Assert.That(items[0].Transport, Is.EqualTo(YouTubeChatTransportKind.OfficialGrpc));
        });
    }

    [Test]
    public void Transient_Failure_Requires_Two_Consecutive_Errors_Before_Cooldown_And_VideoBlock_Is_Scoped()
    {
        using var handler = new QueueHttpMessageHandler((_, _) => throw new InvalidOperationException());
        var clock = new TestTimeProvider();
        var innerTube = new InnerTubeYouTubeLiveChatReader(new InnerTubeLiveChatClient(new SingleHandlerHttpClientFactory(handler)), timeProvider: clock);
        var grpc = new OfficialGrpcYouTubeLiveChatReader(new StubStreamClient((_, ct) => SingleBatch(new([], null, TimeSpan.FromSeconds(5), null), ct)));
        var rest = new OfficialRestYouTubeLiveChatReader(new StubPollingClient((_, _) => Task.FromResult(new YouTubeLiveChatBatch([], null, TimeSpan.FromSeconds(5), null))), clock);

        var composite = new CompositeYouTubeLiveChatReader(innerTube, grpc, rest, clock);
        var vid1 = new YouTubeChatTarget("VID00000001", "chat-1");
        var vid2 = new YouTubeChatTarget("VID00000002", "chat-2");

        var firstTransient = composite.HandleInnerTubeFailure(
            vid1,
            new InnerTubeChatException(InnerTubeErrorKind.TransientNetwork, "timeout #1"));
        Assert.Multiple(() =>
        {
            Assert.That(firstTransient.SwitchedToFallback, Is.False);
            Assert.That(firstTransient.ShouldUseTransientBackoff, Is.True);
            Assert.That(composite.SelectTransport(vid1), Is.EqualTo(YouTubeChatTransportKind.InnerTube));
        });

        var secondTransient = composite.HandleInnerTubeFailure(
            vid1,
            new InnerTubeChatException(InnerTubeErrorKind.ParseFailure, "broken json #2"));
        Assert.Multiple(() =>
        {
            Assert.That(secondTransient.SwitchedToFallback, Is.True);
            Assert.That(composite.SelectTransport(vid1), Is.EqualTo(YouTubeChatTransportKind.OfficialGrpc));
        });

        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        Assert.That(composite.SelectTransport(vid1), Is.EqualTo(YouTubeChatTransportKind.InnerTube));

        composite.HandleInnerTubeFailure(vid1, new InnerTubeChatException(InnerTubeErrorKind.MembersOnly, "sponsors only"));

        Assert.Multiple(() =>
        {
            Assert.That(composite.SelectTransport(vid1), Is.EqualTo(YouTubeChatTransportKind.OfficialGrpc));
            Assert.That(composite.SelectTransport(vid2), Is.EqualTo(YouTubeChatTransportKind.InnerTube));
        });
    }

    [Test]
    public void SessionCursor_PrepareForTransport_Handles_Incompatible_Reset_And_Official_Preservation()
    {
        var t0 = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
        var cursor = new YouTubeChatSessionCursor("chat-1", t0);

        Assert.That(cursor.PrepareForTransport(YouTubeChatTransportKind.InnerTube, YouTubeChatMode.LiveChat, t0), Is.False);

        var coldSwitchGap = cursor.PrepareForTransport(YouTubeChatTransportKind.OfficialGrpc, YouTubeChatMode.LiveChat, t0.AddSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(coldSwitchGap, Is.False);
            Assert.That(cursor.BoundaryUtc, Is.EqualTo(t0));
        });

        var officialBatch = new YouTubeLiveChatBatch(
            [CreateOfficialItem("LCC.msg-1", t0.AddSeconds(6))],
            "off-tok-1",
            TimeSpan.FromSeconds(5),
            null,
            new YouTubeChatCursorToken(YouTubeChatTransportKind.OfficialGrpc, YouTubeChatMode.LiveChat, "off-tok-1"),
            YouTubeChatTransportKind.OfficialGrpc);
        cursor.ProcessBatch(officialBatch);

        var restSwitchGap = cursor.PrepareForTransport(YouTubeChatTransportKind.OfficialRest, YouTubeChatMode.LiveChat, t0.AddSeconds(7));
        Assert.Multiple(() =>
        {
            Assert.That(restSwitchGap, Is.False);
            Assert.That(cursor.SeenCount, Is.EqualTo(1));
            Assert.That(cursor.CursorToken?.Transport, Is.EqualTo(YouTubeChatTransportKind.OfficialRest));
            Assert.That(cursor.CursorToken?.Value, Is.EqualTo("off-tok-1"));
        });

        var backToInnerTubeGap = cursor.PrepareForTransport(YouTubeChatTransportKind.InnerTube, YouTubeChatMode.LiveChat, t0.AddMinutes(10));
        Assert.Multiple(() =>
        {
            Assert.That(backToInnerTubeGap, Is.True);
            Assert.That(cursor.SeenCount, Is.EqualTo(0));
            Assert.That(cursor.CursorToken, Is.Null);
            Assert.That(cursor.BoundaryUtc, Is.EqualTo(t0.AddMinutes(10)));
        });
    }

    private static YouTubeLiveChatItem CreateOfficialItem(string id, DateTimeOffset publishedAt) =>
        new(
            id,
            YouTubeLiveChatItemKind.TextMessage,
            publishedAt,
            "Author",
            "hello",
            new("UC_AUTH", "Author", false, false, false, false),
            "textMessageEvent");

    private static async IAsyncEnumerable<YouTubeLiveChatBatch> SingleBatch(
        YouTubeLiveChatBatch batch,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        yield return batch;
    }

    private sealed class StubStreamClient(
        Func<string?, CancellationToken, IAsyncEnumerable<YouTubeLiveChatBatch>> handler) : IYouTubeLiveChatStreamClient
    {
        public IAsyncEnumerable<YouTubeLiveChatBatch> StreamBatchesAsync(
            string liveChatId,
            string? pageToken,
            CancellationToken cancellationToken) => handler(pageToken, cancellationToken);
    }

    private sealed class StubPollingClient(
        Func<string?, CancellationToken, Task<YouTubeLiveChatBatch>> handler) : IYouTubeLiveChatPollingClient
    {
        public Task<YouTubeLiveChatBatch> ListMessagesAsync(
            string liveChatId,
            string? pageToken,
            CancellationToken cancellationToken) => handler(pageToken, cancellationToken);
    }
}
