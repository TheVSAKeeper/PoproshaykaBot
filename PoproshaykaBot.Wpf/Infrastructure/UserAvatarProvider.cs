using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Users.Avatars;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class UserAvatarProvider(IUserAvatarCache cache, ILogger<UserAvatarProvider> logger)
{
    public const int DecodePixelSize = 96;

    private readonly Lock _sync = new();
    private readonly Dictionary<string, UserAvatarViewModel> _handles = new(StringComparer.Ordinal);
    private readonly HashSet<string> _queue = new(StringComparer.Ordinal);
    private readonly HashSet<string> _requested = new(StringComparer.Ordinal);

    private string[] _tracked = [];
    private bool _resolveNeeded;
    private bool _pumping;
    private Task _loading = Task.CompletedTask;

    public Task Loading
    {
        get
        {
            lock (_sync)
            {
                return _loading;
            }
        }
    }

    public UserAvatarViewModel For(string? userId, string? name)
    {
        var id = userId?.Trim() ?? string.Empty;
        var display = name?.Trim() ?? string.Empty;

        if (id.Length == 0)
        {
            return new(id, display);
        }

        lock (_sync)
        {
            if (_handles.TryGetValue(id, out var known) && string.Equals(known.Name, display, StringComparison.Ordinal))
            {
                return known;
            }

            var handle = new UserAvatarViewModel(id, display)
            {
                Image = known?.Image,
                IsOutdated = known?.IsOutdated ?? false,
            };

            _handles[id] = handle;

            return handle;
        }
    }

    public void Track(IEnumerable<string> userIds)
    {
        var ids = userIds
            .Where(id => id is { Length: > 0 })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        lock (_sync)
        {
            _tracked = ids;
            _resolveNeeded = true;

            foreach (var id in ids)
            {
                if (_requested.Contains(id)
                    && _handles.TryGetValue(id, out var handle)
                    && (!handle.HasImage || handle.IsOutdated))
                {
                    _queue.Add(id);
                }
            }

            StartPump();
        }
    }

    public void Request(UserAvatarViewModel handle)
    {
        lock (_sync)
        {
            if (handle.UserId.Length == 0)
            {
                return;
            }

            _requested.Add(handle.UserId);

            if (handle.HasImage && !handle.IsOutdated)
            {
                return;
            }

            _queue.Add(handle.UserId);
            StartPump();
        }
    }

    private void StartPump()
    {
        if (_pumping)
        {
            return;
        }

        _pumping = true;
        _loading = PumpAsync();
    }

    private async Task PumpAsync()
    {
        await Task.Yield();

        while (TakeBatch() is { } batch)
        {
            try
            {
                await LoadAsync(batch);
            }
            catch (Exception exception)
            {
                logger.UserAvatarLoadFailed(exception);
            }
        }
    }

    private List<string>? TakeBatch()
    {
        lock (_sync)
        {
            if (_queue.Count == 0 && !_resolveNeeded)
            {
                _pumping = false;

                return null;
            }

            var batch = _queue.ToList();
            _queue.Clear();

            return batch;
        }
    }

    private async Task LoadAsync(List<string> ids)
    {
        var unreadable = new HashSet<string>(StringComparer.Ordinal);

        if (ids.Count > 0)
        {
            var cached = await Task.Run(() => Decode(cache.GetCachedPaths(ids), unreadable));
            Apply(cached);
        }

        if (TakeResolve() is { } tracked)
        {
            var changed = await Task.Run(() => cache.ResolveAsync(tracked));
            MarkOutdated(changed);
        }

        var missing = Missing(ids);

        if (missing.Count == 0)
        {
            return;
        }

        var loaded = await Task.Run(async () => Decode(await cache.GetPathsAsync(missing).ConfigureAwait(false), unreadable));
        Apply(loaded);

        var outdated = Outdated(missing);

        if (outdated.Count == 0)
        {
            return;
        }

        var unavailable = await Task.Run(() => cache.GetUnavailable(outdated));
        Discard(outdated.Where(id => unreadable.Contains(id) || unavailable.Contains(id, StringComparer.Ordinal)));
    }

    private string[]? TakeResolve()
    {
        lock (_sync)
        {
            if (!_resolveNeeded)
            {
                return null;
            }

            _resolveNeeded = false;

            return _tracked;
        }
    }

    private void MarkOutdated(IReadOnlyCollection<string> changed)
    {
        lock (_sync)
        {
            foreach (var id in changed)
            {
                if (_handles.TryGetValue(id, out var handle) && handle.HasImage)
                {
                    handle.IsOutdated = true;
                    _queue.Add(id);
                }
            }
        }
    }

    private List<string> Missing(List<string> ids)
    {
        lock (_sync)
        {
            return ids
                .Where(id => _handles.TryGetValue(id, out var handle) && (!handle.HasImage || handle.IsOutdated))
                .ToList();
        }
    }

    private List<string> Outdated(List<string> ids)
    {
        lock (_sync)
        {
            return ids
                .Where(id => _handles.TryGetValue(id, out var handle) && handle.IsOutdated)
                .ToList();
        }
    }

    private List<(string UserId, ImageSource Image)> Decode(IReadOnlyDictionary<string, string> paths, ISet<string> unreadable)
    {
        var images = new List<(string, ImageSource)>(paths.Count);

        foreach (var (userId, path) in paths)
        {
            if (TryDecode(path) is { } image)
            {
                images.Add((userId, image));
            }
            else
            {
                cache.Invalidate(userId);
                unreadable.Add(userId);
            }
        }

        return images;
    }

    private void Discard(IEnumerable<string> ids)
    {
        foreach (var userId in ids)
        {
            UserAvatarViewModel? handle;

            lock (_sync)
            {
                if (!_handles.TryGetValue(userId, out handle) || !handle.IsOutdated)
                {
                    continue;
                }

                handle.IsOutdated = false;
            }

            handle.Image = null;
        }
    }

    private void Apply(List<(string UserId, ImageSource Image)> images)
    {
        foreach (var (userId, image) in images)
        {
            UserAvatarViewModel? handle;

            lock (_sync)
            {
                if (!_handles.TryGetValue(userId, out handle) || (handle.HasImage && !handle.IsOutdated))
                {
                    continue;
                }

                handle.IsOutdated = false;
            }

            handle.Image = image;
        }
    }

    private ImageSource? TryDecode(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new(path, UriKind.Absolute);
            bitmap.DecodePixelWidth = DecodePixelSize;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception exception)
        {
            logger.UserAvatarDecodeFailed(exception, path);

            return null;
        }
    }
}
