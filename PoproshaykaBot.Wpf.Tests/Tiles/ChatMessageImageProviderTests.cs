using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Server.Images;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Wpf.Infrastructure;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
public sealed class ChatMessageImageProviderTests
{
    [Test]
    public async Task Request_DeduplicatesConcurrentRequests_AndCachesFrozenBitmap()
    {
        var fetchCalls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pngBytes = CreatePngBytes();

        var provider = new ChatMessageImageProvider(
            async (_, _, _) =>
            {
                Interlocked.Increment(ref fetchCalls);
                await gate.Task;
                return new MessageImageContent(pngBytes, "image/png");
            },
            NullLogger<ChatMessageImageProvider>.Instance);

        var uri = new Uri("https://i.imgur.com/test.png");
        var settings = new ObsChatSettings { ShowMessageImages = true };

        var first = provider.Request(uri, 160, settings);
        var second = provider.Request(uri, 160, settings);

        gate.SetResult();
        await provider.WaitForPendingAsync();

        var third = provider.Request(uri, 160, settings);
        await provider.WaitForPendingAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first, Is.SameAs(second));
            Assert.That(first, Is.SameAs(third));
            Assert.That(fetchCalls, Is.EqualTo(1));
            Assert.That(first.HasImage, Is.True);
            Assert.That(first.Image, Is.Not.Null);
            Assert.That(first.Image!.IsFrozen, Is.True);
        }
    }

    [Test]
    public async Task Request_EvictsLeastRecentlyUsedImage_AndClearsVictimImage()
    {
        var fetchCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var pngBytes = CreatePngBytes();

        var provider = new ChatMessageImageProvider(
            (uri, _, _) =>
            {
                lock (fetchCounts)
                {
                    fetchCounts[uri.AbsoluteUri] = fetchCounts.GetValueOrDefault(uri.AbsoluteUri) + 1;
                }

                return Task.FromResult<MessageImageContent?>(new MessageImageContent(pngBytes, "image/png"));
            },
            NullLogger<ChatMessageImageProvider>.Instance,
            maxLoadedImages: 2);

        var settings = new ObsChatSettings { ShowMessageImages = true };
        var u1 = new Uri("https://i.imgur.com/one.png");
        var u2 = new Uri("https://i.imgur.com/two.png");
        var u3 = new Uri("https://i.imgur.com/three.png");

        var h1 = provider.Request(u1, 160, settings);
        await provider.WaitForPendingAsync();

        var h2 = provider.Request(u2, 160, settings);
        await provider.WaitForPendingAsync();

        _ = provider.Request(u1, 160, settings);

        var h3 = provider.Request(u3, 160, settings);
        await provider.WaitForPendingAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(h1.HasImage, Is.True);
            Assert.That(h3.HasImage, Is.True);
            Assert.That(h2.HasImage, Is.False);
            Assert.That(h2.Image, Is.Null);
            Assert.That(provider.TrackedHandleCount, Is.EqualTo(2));
        }

        var reloadedH2 = provider.Request(u2, 160, settings);
        await provider.WaitForPendingAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reloadedH2, Is.Not.SameAs(h2));
            Assert.That(reloadedH2.HasImage, Is.True);
            Assert.That(h1.HasImage, Is.False);
            Assert.That(provider.TrackedHandleCount, Is.EqualTo(2));
            Assert.That(fetchCounts[u2.AbsoluteUri], Is.EqualTo(2));
        }
    }

    [Test]
    public async Task Request_WhenFetchFailsOrBytesAreInvalid_LeavesImageNullAndDropsHandle()
    {
        var provider = new ChatMessageImageProvider(
            (uri, _, _) =>
            {
                if (uri.AbsoluteUri.EndsWith("broken.png", StringComparison.Ordinal))
                {
                    return Task.FromResult<MessageImageContent?>(new MessageImageContent([1, 2, 3], "image/png"));
                }

                if (uri.AbsoluteUri.EndsWith("throws.png", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("boom");
                }

                return Task.FromResult<MessageImageContent?>(null);
            },
            NullLogger<ChatMessageImageProvider>.Instance);

        var settings = new ObsChatSettings { ShowMessageImages = true };

        var nullHandle = provider.Request(new Uri("https://i.imgur.com/missing.png"), 160, settings);
        var brokenHandle = provider.Request(new Uri("https://i.imgur.com/broken.png"), 160, settings);
        var thrownHandle = provider.Request(new Uri("https://i.imgur.com/throws.png"), 160, settings);
        await provider.WaitForPendingAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nullHandle.HasImage, Is.False);
            Assert.That(brokenHandle.HasImage, Is.False);
            Assert.That(thrownHandle.HasImage, Is.False);
            Assert.That(provider.TrackedHandleCount, Is.EqualTo(0));
        }
    }

    [Test]
    public async Task Request_LimitsConcurrentFetches()
    {
        var inFlight = 0;
        var maxObserved = 0;
        var pngBytes = CreatePngBytes();

        var provider = new ChatMessageImageProvider(
            async (_, _, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref inFlight);
                var snapshot = Volatile.Read(ref maxObserved);
                while (current > snapshot && Interlocked.CompareExchange(ref maxObserved, current, snapshot) != snapshot)
                {
                    snapshot = Volatile.Read(ref maxObserved);
                }

                await Task.Delay(30, cancellationToken);
                Interlocked.Decrement(ref inFlight);
                return new MessageImageContent(pngBytes, "image/png");
            },
            NullLogger<ChatMessageImageProvider>.Instance,
            maxLoadedImages: 16,
            maxConcurrentFetches: 2);

        var settings = new ObsChatSettings { ShowMessageImages = true };

        for (var i = 0; i < 8; i++)
        {
            _ = provider.Request(new Uri($"https://i.imgur.com/img-{i}.png"), 160, settings);
        }

        await provider.WaitForPendingAsync();

        Assert.That(maxObserved, Is.LessThanOrEqualTo(2));
    }

    private static byte[] CreatePngBytes()
    {
        var source = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgr24, null, new byte[4 * 4 * 3], 4 * 3);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
