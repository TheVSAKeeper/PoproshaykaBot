using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class GameBoxArtProvider
{
    public const int DefaultMaxLoadedImages = 256;

    private readonly IGameBoxArtCache _cache;
    private readonly ILogger<GameBoxArtProvider> _logger;
    private readonly int _maxLoadedImages;
    private readonly Lock _sync = new();
    private readonly Lock _applyGate = new();
    private readonly Dictionary<string, GameBoxArtViewModel> _handles = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new(StringComparer.OrdinalIgnoreCase);

    public GameBoxArtProvider(IGameBoxArtCache cache, ILogger<GameBoxArtProvider> logger)
        : this(cache, logger, DefaultMaxLoadedImages)
    {
    }

    internal GameBoxArtProvider(IGameBoxArtCache cache, ILogger<GameBoxArtProvider> logger, int maxLoadedImages)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLoadedImages, 1);

        _cache = cache;
        _logger = logger;
        _maxLoadedImages = maxLoadedImages;
    }

    public GameBoxArtViewModel For(string? game)
    {
        if (game is not { Length: > 0 })
        {
            return GameBoxArtViewModel.None;
        }

        var name = game.Trim();

        if (name.Length == 0)
        {
            return GameBoxArtViewModel.None;
        }

        lock (_sync)
        {
            return GetOrCreateHandleCore(name);
        }
    }

    public async Task LoadAsync(IReadOnlyCollection<string> games, CancellationToken cancellationToken)
    {
        var missing = Missing(games);

        if (missing.Count == 0)
        {
            return;
        }

        try
        {
            var cached = await Task.Run(() => Decode(_cache.GetCachedPaths(missing)), cancellationToken);
            Apply(cached, cancellationToken);

            var cachedSet = cached.Select(static item => item.Game).ToHashSet(StringComparer.OrdinalIgnoreCase);
            missing = Missing(missing.Where(game => !cachedSet.Contains(game)));

            if (missing.Count == 0)
            {
                return;
            }

            var loaded = await Task.Run(
                async () => Decode(await _cache.GetPathsAsync(missing, cancellationToken).ConfigureAwait(false)),
                cancellationToken);

            Apply(loaded, cancellationToken);
        }
        catch (Exception exception)
        {
            if (exception is not OperationCanceledException)
            {
                _logger.BoxArtLoadFailed(exception);
            }
        }
    }

    private List<string> Missing(IEnumerable<string> games)
    {
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (_sync)
        {
            foreach (var game in games)
            {
                if (game is not { Length: > 0 })
                {
                    continue;
                }

                var name = game.Trim();
                if (name.Length == 0 || !seen.Add(name))
                {
                    continue;
                }

                var handle = GetOrCreateHandleCore(name);
                if (handle.HasImage && _lruNodes.ContainsKey(handle.Game))
                {
                    TouchLruCore(handle.Game);
                }
                else if (!handle.HasImage)
                {
                    missing.Add(handle.Game);
                }
            }
        }

        return missing;
    }

    private GameBoxArtViewModel GetOrCreateHandleCore(string name)
    {
        if (!_handles.TryGetValue(name, out var handle))
        {
            handle = new(name);
            _handles[name] = handle;
        }

        return handle;
    }

    private List<(string Game, ImageSource Image)> Decode(IReadOnlyDictionary<string, string> paths)
    {
        var images = new List<(string, ImageSource)>(paths.Count);

        foreach (var (game, path) in paths)
        {
            if (TryDecode(path) is { } image)
            {
                images.Add((game, image));
            }
            else
            {
                _cache.Invalidate(game);
            }
        }

        return images;
    }

    private void Apply(List<(string Game, ImageSource Image)> images, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || images.Count == 0)
        {
            return;
        }

        lock (_applyGate)
        {
            foreach (var (game, image) in images)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                GameBoxArtViewModel handle;
                bool alreadyLoaded;

                lock (_sync)
                {
                    handle = GetOrCreateHandleCore(game);
                    alreadyLoaded = handle.HasImage;
                    TouchLruCore(handle.Game);
                }

                if (!alreadyLoaded)
                {
                    handle.Image = image;
                }
            }

            EvictExcessLocked();
        }
    }

    private void TouchLruCore(string game)
    {
        if (_lruNodes.TryGetValue(game, out var existing))
        {
            _lruOrder.Remove(existing);
            _lruOrder.AddLast(existing);
            return;
        }

        _lruNodes[game] = _lruOrder.AddLast(game);
    }

    private void EvictExcessLocked()
    {
        while (true)
        {
            GameBoxArtViewModel? victim;

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

    private ImageSource? TryDecode(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new(path, UriKind.Absolute);
            bitmap.DecodePixelWidth = GameBoxArtCacheOptions.ImageWidth;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception exception)
        {
            _logger.BoxArtDecodeFailed(exception, path);

            return null;
        }
    }
}
