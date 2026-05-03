using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.WinForms.Twitch;
using PoproshaykaBot.WinForms.Twitch.Helix;

namespace PoproshaykaBot.WinForms.Users;

public sealed class UserProfileImageProvider(IServiceProvider serviceProvider, ILogger<UserProfileImageProvider> logger)
{
    private const int MaxCacheSize = 5000;
    private static readonly TimeSpan NegativeCacheTtl = TimeSpan.FromMinutes(30);

    private readonly object _syncLock = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _lruOrder = [];
    private readonly Dictionary<string, Task<string?>> _inflight = new(StringComparer.Ordinal);

    public bool TryGetCached(string userId, out string? url)
    {
        url = null;

        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        lock (_syncLock)
        {
            if (!_cache.TryGetValue(userId, out var entry))
            {
                return false;
            }

            if (entry.IsNegative && DateTimeOffset.UtcNow > entry.NegativeExpiresAt)
            {
                RemoveFromCache(userId);
                return false;
            }

            TouchLru(userId);
            url = entry.Url;
            return true;
        }
    }

    public async Task<string?> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        if (TryGetCached(userId, out var cached))
        {
            return cached;
        }

        Task<string?> resolverTask;

        lock (_syncLock)
        {
            if (_cache.TryGetValue(userId, out var entry) && (!entry.IsNegative || DateTimeOffset.UtcNow <= entry.NegativeExpiresAt))
            {
                TouchLru(userId);
                return entry.Url;
            }

            if (!_inflight.TryGetValue(userId, out var existing))
            {
                existing = ResolveAsync(userId, CancellationToken.None);
                _inflight[userId] = existing;
            }

            resolverTask = existing;
        }

        return await resolverTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ResolveAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var helixClient = serviceProvider.GetRequiredKeyedService<ITwitchHelixClient>(TwitchEndpoints.HelixBotClient);
            var users = await helixClient.GetUsersByIdsAsync([userId], cancellationToken).ConfigureAwait(false);

            var url = users.FirstOrDefault()?.ProfileImageUrl;

            lock (_syncLock)
            {
                StoreCacheEntry(userId, url);
                _inflight.Remove(userId);
            }

            return url;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Не удалось получить аватар пользователя {UserId}", userId);

            lock (_syncLock)
            {
                _inflight.Remove(userId);
            }

            return null;
        }
    }

    private void StoreCacheEntry(string userId, string? url)
    {
        var entry = string.IsNullOrEmpty(url)
            ? new CacheEntry(null, true, DateTimeOffset.UtcNow + NegativeCacheTtl)
            : new CacheEntry(url, false, DateTimeOffset.MinValue);

        if (_cache.ContainsKey(userId))
        {
            _cache[userId] = entry;
            TouchLru(userId);
            return;
        }

        _cache[userId] = entry;
        _lruOrder.AddLast(userId);

        while (_cache.Count > MaxCacheSize && _lruOrder.First is { } oldest)
        {
            _cache.Remove(oldest.Value);
            _lruOrder.RemoveFirst();
        }
    }

    private void TouchLru(string userId)
    {
        var node = _lruOrder.Find(userId);
        if (node == null)
        {
            return;
        }

        _lruOrder.Remove(node);
        _lruOrder.AddLast(node);
    }

    private void RemoveFromCache(string userId)
    {
        _cache.Remove(userId);
        var node = _lruOrder.Find(userId);
        if (node != null)
        {
            _lruOrder.Remove(node);
        }
    }

    private sealed record CacheEntry(string? Url, bool IsNegative, DateTimeOffset NegativeExpiresAt);
}
