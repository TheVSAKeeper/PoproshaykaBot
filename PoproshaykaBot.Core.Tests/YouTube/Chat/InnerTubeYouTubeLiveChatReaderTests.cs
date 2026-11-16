using System.Net;
using System.Text.Json;
using PoproshaykaBot.Core.Tests.Polls;
using PoproshaykaBot.Core.Tests.YouTube.Fixtures;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube;
using PoproshaykaBot.Core.YouTube.Chat.Transport;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
internal sealed class InnerTubeYouTubeLiveChatReaderTests
{
    [Test]
    public async Task ReadBatchesAsync_Switches_TopChat_To_LiveChat_Receives_More_Messages_And_Sends_No_Cookies()
    {
        var watchHtml = InnerTubeFixtureLoader.ReadText("watch_live_chat_selector.html");
        using var comparisonDoc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("top_vs_live_window_comparison.json"));
        var topChatJson = comparisonDoc.RootElement.GetProperty("topChatResponse").GetRawText();
        var liveChatJson = comparisonDoc.RootElement.GetProperty("liveChatResponse").GetRawText();

        using var handler = new QueueHttpMessageHandler((_, call) => call switch
        {
            0 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
            1 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, topChatJson, "application/json"),
            2 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, liveChatJson, "application/json"),
            _ => throw new InvalidOperationException($"Неожиданный HTTP-запрос #{call}"),
        });

        var client = new InnerTubeLiveChatClient(new SingleHandlerHttpClientFactory(handler));
        var reader = new InnerTubeYouTubeLiveChatReader(client, timeProvider: new TestTimeProvider());
        var target = new YouTubeChatTarget("SYNTHVID001", LiveChatId: "chat-1", Mode: YouTubeChatMode.LiveChat);

        var batches = new List<YouTubeLiveChatBatch>();

        await foreach (var batch in reader.ReadBatchesAsync(target, cursor: null, maxBatches: 1, CancellationToken.None))
        {
            batches.Add(batch);
        }

        Assert.Multiple(() =>
        {
            Assert.That(handler.RequestedUris, Has.Count.EqualTo(3));
            Assert.That(handler.RequestBodies[2], Does.Contain("SYNTHETIC_SWITCH_TO_ALL_LIVE_CHAT"));
            Assert.That(batches, Has.Count.EqualTo(1));
            Assert.That(batches[0].Items.Select(static i => i.MessageText), Does.Contain("!join command missed by top chat"));
            Assert.That(batches[0].PollingInterval, Is.EqualTo(TimeSpan.FromMilliseconds(2000)));
            Assert.That(batches[0].NextCursor?.Value, Is.EqualTo("SYNTHETIC_LIVE_NEXT_002"));
            Assert.That(batches[0].Items[0].RawPayloadJson, Is.Null);
            Assert.That(handler.RequestHeaders.All(static h =>
                h.ContainsKey("User-Agent")
                && h.ContainsKey("Accept-Language")
                && !h.ContainsKey("Cookie")
                && !h.ContainsKey("Authorization")), Is.True);
        });
    }

    [Test]
    public void ReadBatchesAsync_Throws_Typed_RateLimited_And_Forbidden_Exceptions()
    {
        using var errorsDoc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("errors_429_403_consent.json"));
        var watchHtml = InnerTubeFixtureLoader.ReadText("watch_live_chat_selector.html");
        var body429 = errorsDoc.RootElement.GetProperty("rateLimited429Body").GetString()!;
        var body403 = errorsDoc.RootElement.GetProperty("forbidden403Body").GetString()!;

        using var handler429 = new QueueHttpMessageHandler((_, call) => call switch
        {
            0 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
            _ => QueueHttpMessageHandler.TextResponse(HttpStatusCode.TooManyRequests, body429, "application/json"),
        });

        using var handler403 = new QueueHttpMessageHandler((_, call) => call switch
        {
            0 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
            _ => QueueHttpMessageHandler.TextResponse(HttpStatusCode.Forbidden, body403, "application/json"),
        });

        var target = new YouTubeChatTarget("SYNTHVID001", LiveChatId: "chat-1");

        var ex429 = Assert.ThrowsAsync<InnerTubeChatException>(async () =>
        {
            var reader = new InnerTubeYouTubeLiveChatReader(new InnerTubeLiveChatClient(new SingleHandlerHttpClientFactory(handler429)));
            await foreach (var _ in reader.ReadBatchesAsync(target, cursor: null, maxBatches: 1, CancellationToken.None))
            {
            }
        });

        var ex403 = Assert.ThrowsAsync<InnerTubeChatException>(async () =>
        {
            var reader = new InnerTubeYouTubeLiveChatReader(new InnerTubeLiveChatClient(new SingleHandlerHttpClientFactory(handler403)));
            await foreach (var _ in reader.ReadBatchesAsync(target, cursor: null, maxBatches: 1, CancellationToken.None))
            {
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(ex429!.Kind, Is.EqualTo(InnerTubeErrorKind.RateLimited));
            Assert.That(ex403!.Kind, Is.EqualTo(InnerTubeErrorKind.Forbidden));
        });
    }

    [Test]
    public async Task ReadBatchesAsync_ActiveCursor_With_ReloadContinuation_Throws_ContinuationInvalidated()
    {
        var watchHtml = InnerTubeFixtureLoader.ReadText("watch_live_chat_selector.html");
        var initialJson = InnerTubeFixtureLoader.ReadText("initial_mixed_with_ticker.json");
        using var reloadDoc = JsonDocument.Parse(InnerTubeFixtureLoader.ReadText("reload_and_ended.json"));
        var reloadJson = reloadDoc.RootElement.GetProperty("reloadResponse").GetRawText();

        using var handler = new QueueHttpMessageHandler((_, call) => call switch
        {
            0 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, watchHtml, "text/html"),
            1 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, initialJson, "application/json"),
            2 => QueueHttpMessageHandler.TextResponse(HttpStatusCode.OK, reloadJson, "application/json"),
            _ => throw new InvalidOperationException(),
        });

        var reader = new InnerTubeYouTubeLiveChatReader(
            new InnerTubeLiveChatClient(new SingleHandlerHttpClientFactory(handler)),
            timeProvider: new TestTimeProvider());
        var target = new YouTubeChatTarget("SYNTHVID001", LiveChatId: "chat-1", Mode: YouTubeChatMode.TopChat);

        YouTubeChatCursorToken? cursor = null;

        await foreach (var batch in reader.ReadBatchesAsync(target, cursor: null, maxBatches: 1, CancellationToken.None))
        {
            cursor = batch.NextCursor;
        }

        Assert.That(cursor, Is.Not.Null);

        var ex = Assert.ThrowsAsync<InnerTubeChatException>(async () =>
        {
            await foreach (var _ in reader.ReadBatchesAsync(target, cursor, maxBatches: 1, CancellationToken.None))
            {
            }
        });

        Assert.That(ex!.Kind, Is.EqualTo(InnerTubeErrorKind.ContinuationInvalidated));
    }

    [Test]
    public void Client_Wraps_IOException_During_Body_Read_As_TransientNetwork()
    {
        using var handler = new QueueHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ThrowingStreamContent(),
        });

        var client = new InnerTubeLiveChatClient(new SingleHandlerHttpClientFactory(handler));

        var ex = Assert.ThrowsAsync<InnerTubeChatException>(() =>
            client.FetchChatPageAsync(
                "SYNTHETIC_KEY",
                new PoproshaykaBot.Core.YouTube.Chat.InnerTube.Models.LiveChatRequest
                {
                    Context = new()
                    {
                        Client = new()
                        {
                            ClientName = InnerTubeEndpoints.WebClientName,
                            ClientVersion = "2.20261116.01.00",
                            Hl = "en",
                            Gl = "US",
                        },
                    },
                    Continuation = "SYNTHETIC_CONT",
                },
                CancellationToken.None));

        Assert.That(ex!.Kind, Is.EqualTo(InnerTubeErrorKind.TransientNetwork));
    }

    private sealed class ThrowingStreamContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.CompletedTask;

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            Task.FromException<Stream>(new IOException("premature EOF"));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
