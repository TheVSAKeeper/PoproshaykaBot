using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Twitch.GameArt;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class GameBoxArtProvider(IGameBoxArtCache cache, ILogger<GameBoxArtProvider> logger)
{
    private readonly Dictionary<string, GameBoxArtViewModel> _handles = new(StringComparer.OrdinalIgnoreCase);

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

        if (!_handles.TryGetValue(name, out var handle))
        {
            handle = new(name);
            _handles[name] = handle;
        }

        return handle;
    }

    public void ApplyCached(IReadOnlyCollection<string> games)
    {
        Apply(cache.GetCachedPaths(games));
    }

    public async Task LoadAsync(IReadOnlyCollection<string> games, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, string> paths;

        try
        {
            paths = await cache.GetPathsAsync(games, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            logger.BoxArtLoadFailed(exception);

            return;
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            Apply(paths);
        }
    }

    private void Apply(IReadOnlyDictionary<string, string> paths)
    {
        foreach (var (game, path) in paths)
        {
            var handle = For(game);

            if (handle.HasImage || !handle.HasGame)
            {
                continue;
            }

            if (TryDecode(path) is { } image)
            {
                handle.Image = image;
            }
            else
            {
                cache.Invalidate(game);
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
            logger.BoxArtDecodeFailed(exception, path);

            return null;
        }
    }
}
