using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Server.Images;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Wpf.Bootstrap;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed partial class ChatMessageImageHandle : ObservableObject
{
    public static readonly ChatMessageImageHandle None = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    private ImageSource? _image;

    public bool HasImage => Image is not null;
}

public sealed class ChatMessageImageProvider
{
    public const int DefaultMaxLoadedImages = 128;

    private readonly Func<Uri, ObsChatSettings, CancellationToken, Task<MessageImageContent?>> _fetchAsync;
    private readonly ILogger<ChatMessageImageProvider> _logger;
    private readonly int _maxLoadedImages;
    private readonly Lock _sync = new();
    private readonly Lock _applyGate = new();
    private readonly Dictionary<CacheKey, ChatMessageImageHandle> _handles = new();
    private readonly Dictionary<CacheKey, Task> _inFlight = new();
    private readonly LinkedList<CacheKey> _lruOrder = new();
    private readonly Dictionary<CacheKey, LinkedListNode<CacheKey>> _lruNodes = new();

    public ChatMessageImageProvider(MessageImageFetcher fetcher, ILogger<ChatMessageImageProvider> logger)
        : this(
            (uri, settings, cancellationToken) => fetcher.FetchAsync(uri, settings, cancellationToken),
            logger,
            DefaultMaxLoadedImages)
    {
        ArgumentNullException.ThrowIfNull(fetcher);
    }

    internal ChatMessageImageProvider(
        Func<Uri, ObsChatSettings, CancellationToken, Task<MessageImageContent?>> fetchAsync,
        ILogger<ChatMessageImageProvider> logger,
        int maxLoadedImages = DefaultMaxLoadedImages)
    {
        ArgumentNullException.ThrowIfNull(fetchAsync);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLoadedImages, 1);

        _fetchAsync = fetchAsync;
        _logger = logger;
        _maxLoadedImages = maxLoadedImages;
    }

    public ChatMessageImageHandle Request(Uri uri, int maxHeightPixels, ObsChatSettings settings)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(settings);

        var key = new CacheKey(uri.AbsoluteUri, maxHeightPixels);

        lock (_sync)
        {
            if (!_handles.TryGetValue(key, out var handle))
            {
                handle = new ChatMessageImageHandle();
                _handles[key] = handle;
            }

            if (handle.HasImage && _lruNodes.ContainsKey(key))
            {
                TouchLruCore(key);
                return handle;
            }

            if (!_inFlight.ContainsKey(key))
            {
                _inFlight[key] = Task.Run(() => LoadAsync(key, uri, maxHeightPixels, settings, handle));
            }

            return handle;
        }
    }

    internal async Task WaitForPendingAsync()
    {
        while (true)
        {
            Task[] pending;

            lock (_sync)
            {
                if (_inFlight.Count == 0)
                {
                    return;
                }

                pending = [.. _inFlight.Values];
            }

            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(
        CacheKey key,
        Uri uri,
        int maxHeightPixels,
        ObsChatSettings settings,
        ChatMessageImageHandle handle)
    {
        try
        {
            var content = await _fetchAsync(uri, settings, CancellationToken.None).ConfigureAwait(false);

            if (content is null)
            {
                return;
            }

            var decoded = TryDecode(content.Content, maxHeightPixels, uri);

            if (decoded is null)
            {
                return;
            }

            lock (_applyGate)
            {
                lock (_sync)
                {
                    TouchLruCore(key);
                }

                handle.Image = decoded;
                EvictExcessLocked();
            }
        }
        catch (Exception exception)
        {
            _logger.ChatMessageImageLoadFailed(exception, uri);
        }
        finally
        {
            lock (_sync)
            {
                _inFlight.Remove(key);
            }
        }
    }

    private void TouchLruCore(CacheKey key)
    {
        if (_lruNodes.TryGetValue(key, out var existing))
        {
            _lruOrder.Remove(existing);
            _lruOrder.AddLast(existing);
            return;
        }

        _lruNodes[key] = _lruOrder.AddLast(key);
    }

    private void EvictExcessLocked()
    {
        while (true)
        {
            ChatMessageImageHandle? victim;

            lock (_sync)
            {
                if (_lruOrder.Count <= _maxLoadedImages || _lruOrder.First is not { } oldest)
                {
                    return;
                }

                var victimKey = oldest.Value;
                _lruOrder.RemoveFirst();
                _lruNodes.Remove(victimKey);
                _handles.TryGetValue(victimKey, out victim);
            }

            if (victim is not null)
            {
                victim.Image = null;
            }
        }
    }

    private ImageSource? TryDecode(byte[] bytes, int maxHeightPixels, Uri uri)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = stream;

            if (maxHeightPixels > 0)
            {
                bitmap.DecodePixelHeight = maxHeightPixels;
            }

            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception exception)
        {
            _logger.ChatMessageImageDecodeFailed(exception, uri);
            return null;
        }
    }

    private readonly record struct CacheKey(string AbsoluteUri, int MaxHeightPixels);
}
