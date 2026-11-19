using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Helix;
using System.Text.Json;

namespace PoproshaykaBot.Core.Users.Avatars;

public sealed class UserAvatarCache : IUserAvatarCache
{
    public const int ResolveBatchSize = TwitchHelixClient.MaxUsersPerRequest;
    public const string IndexFileName = "index.json";
    public const string ImageExtension = ".img";
    public const string FullSizeMarker = "-300x300.";
    public const string CacheSizeMarker = "-150x150.";

    private const int MaxUserIdLength = 32;
    private const int MaxConsecutiveDownloadFailures = 3;

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    private static readonly TimeSpan UseWriteInterval = TimeSpan.FromHours(1);

    private readonly ITwitchHelixClient _helix;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;
    private readonly UserAvatarCacheOptions _options;
    private readonly ILogger<UserAvatarCache> _logger;

    private readonly Lock _lock = new();
    private readonly Dictionary<string, AvatarEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task<AvatarUrlLookupResult>> _inflight = new(StringComparer.Ordinal);
    private readonly HashSet<string> _changedOutsidePass = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _resolveGate = new(1, 1);
    private readonly SemaphoreSlim _downloadGate = new(1, 1);

    private HashSet<string> _pinned = new(StringComparer.Ordinal);
    private bool _indexLoaded;
    private bool _indexDirty;
    private bool _sweepRequested;
    private bool _offlineReported;
    private bool _writeFailureReported;

    public UserAvatarCache(
        [FromKeyedServices(TwitchEndpoints.HelixBotClient)]
        ITwitchHelixClient helix,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        UserAvatarCacheOptions options,
        ILogger<UserAvatarCache> logger)
    {
        _helix = helix;
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<string?> GetUrlAsync(string userId, CancellationToken cancellationToken = default)
    {
        var result = await ResolveUrlAsync(userId, cancellationToken).ConfigureAwait(false);
        return result.Url;
    }

    public async Task<AvatarUrlLookupResult> ResolveUrlAsync(string userId, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeId(userId, out var id))
        {
            return AvatarUrlLookupResult.NotFound;
        }

        EnsureIndexLoaded();

        TaskCompletionSource<AvatarUrlLookupResult>? owner = null;
        Task<AvatarUrlLookupResult>? pending;

        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();
            _entries.TryGetValue(id, out var entry);

            if (entry is not null && IsUrlFresh(entry, now))
            {
                Touch(entry, now);

                return AvatarUrlLookupResult.Found(entry.Url!);
            }

            if (_options.CacheOnly)
            {
                return entry?.Url is { } cachedUrl
                    ? AvatarUrlLookupResult.Found(cachedUrl)
                    : AvatarUrlLookupResult.NotFound;
            }

            if (entry is not null && IsMissFresh(entry, now))
            {
                return AvatarUrlLookupResult.NotFound;
            }

            if (!_inflight.TryGetValue(id, out pending))
            {
                owner = new(TaskCreationOptions.RunContinuationsAsynchronously);
                pending = owner.Task;
                _inflight[id] = pending;
            }
        }

        if (owner is not null)
        {
            _ = CompleteResolveAsync(id, owner);
        }

        return await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<string>> ResolveAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        EnsureIndexLoaded();

        var changed = new HashSet<string>(StringComparer.Ordinal);

        lock (_lock)
        {
            _pinned = Normalize(userIds).ToHashSet(StringComparer.Ordinal);

            foreach (var id in _pinned)
            {
                if (_changedOutsidePass.Remove(id))
                {
                    changed.Add(id);
                }
            }
        }

        if (_options.CacheOnly || CollectUnresolved(userIds).Count == 0)
        {
            return changed;
        }

        await _resolveGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            foreach (var batch in CollectUnresolved(userIds).Chunk(ResolveBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<UserInfo> users;

                try
                {
                    users = await _helix.GetUsersByIdsAsync(batch, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    ReportOffline(DescribeFailure(exception));

                    break;
                }

                lock (_lock)
                {
                    var now = _timeProvider.GetUtcNow();

                    foreach (var id in batch)
                    {
                        Apply(id, Find(users, id), now, changed);
                    }

                    TrimEntries();
                }
            }
        }
        finally
        {
            _resolveGate.Release();
            SaveIndexIfDirty();
        }

        return changed;
    }

    public IReadOnlyDictionary<string, string> GetCachedPaths(IReadOnlyCollection<string> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        EnsureIndexLoaded();
        SweepIfRequested();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        lock (_lock)
        {
            foreach (var id in Normalize(userIds))
            {
                if (_entries.TryGetValue(id, out var entry) && HasCurrentFile(entry))
                {
                    result[id] = PathOf(id);
                }
            }
        }

        DropVanished(result);
        Touch(result.Keys);
        SaveIndexIfDirty();

        return result;
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPathsAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        EnsureIndexLoaded();
        SweepIfRequested();

        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var downloads = CollectDownloads(Normalize(userIds), result);

            if (downloads.Count == 0 || _options.CacheOnly)
            {
                return result;
            }

            await _downloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                await DownloadAsync(downloads, result, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _downloadGate.Release();
            }

            return result;
        }
        finally
        {
            Touch(result.Keys);
            SweepIfRequested();
            DropVanished(result);
            SaveIndexIfDirty();
        }
    }

    public IReadOnlyCollection<string> GetUnavailable(IReadOnlyCollection<string> userIds)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        EnsureIndexLoaded();

        var result = new List<string>();

        lock (_lock)
        {
            foreach (var id in Normalize(userIds))
            {
                if (_entries.TryGetValue(id, out var entry) && entry.Url is null)
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }

    public void Invalidate(string userId)
    {
        if (!TryNormalizeId(userId, out var id))
        {
            return;
        }

        EnsureIndexLoaded();

        try
        {
            File.Delete(PathOf(id));
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Не удалось убрать испорченную аватарку пользователя {UserId} из кеша", id);
        }

        lock (_lock)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                entry.FileUrl = null;
                entry.ImageFailedAt = _timeProvider.GetUtcNow();
                _indexDirty = true;
            }
        }

        SaveIndexIfDirty();
    }

    private static bool TryNormalizeId(string? userId, out string id)
    {
        id = userId?.Trim() ?? string.Empty;

        if (id.Length is 0 or > MaxUserIdLength)
        {
            return false;
        }

        foreach (var symbol in id)
        {
            if (!char.IsAsciiDigit(symbol))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> Normalize(IReadOnlyCollection<string> userIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(userIds.Count);

        foreach (var raw in userIds)
        {
            if (TryNormalizeId(raw, out var id) && seen.Add(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static bool IsOwnFileName(string fileName)
    {
        if (!fileName.EndsWith(ImageExtension, StringComparison.Ordinal))
        {
            return false;
        }

        return TryNormalizeId(fileName[..^ImageExtension.Length], out var id)
               && id.Length + ImageExtension.Length == fileName.Length;
    }

    private static bool IsDownloadable(string? url)
    {
        return url is { Length: > 0 }
               && Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static UserInfo? Find(IReadOnlyList<UserInfo> users, string id)
    {
        return users.FirstOrDefault(user => string.Equals(user.Id, id, StringComparison.Ordinal));
    }

    private static IEnumerable<string> DownloadCandidates(string url)
    {
        var index = url.LastIndexOf(FullSizeMarker, StringComparison.Ordinal);

        if (index >= 0)
        {
            yield return string.Concat(url.AsSpan(0, index), CacheSizeMarker, url.AsSpan(index + FullSizeMarker.Length));
        }

        yield return url;
    }

    private static bool HasCurrentFile(AvatarEntry entry)
    {
        return entry.FileUrl is not null && string.Equals(entry.FileUrl, entry.Url, StringComparison.Ordinal);
    }

    private static bool LooksLikeImage(byte[] bytes)
    {
        ReadOnlySpan<byte> head = bytes;

        return head.StartsWith(JpegSignature)
               || head.StartsWith(PngSignature)
               || head.StartsWith("GIF8"u8)
               || (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8));
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                return buffer.ToArray();
            }

            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string DescribeFailure(Exception exception)
    {
        return exception switch
        {
            TwitchAuthorizationMissingException => "нет авторизации Twitch",
            HelixRequestException helix => $"Twitch ответил кодом {(int)helix.StatusCode}",
            _ => "Twitch недоступен",
        };
    }

    private string PathOf(string id)
    {
        return Path.Combine(_options.Directory, id + ImageExtension);
    }

    private bool IsUrlFresh(AvatarEntry entry, DateTimeOffset now)
    {
        return entry.Url is not null
               && entry.ResolvedAt is { } resolvedAt
               && resolvedAt <= now
               && now - resolvedAt < _options.UrlLifetime;
    }

    private bool IsMissFresh(AvatarEntry entry, DateTimeOffset now)
    {
        return entry.MissedAt is { } missedAt && missedAt <= now && now - missedAt < _options.MissLifetime;
    }

    private bool IsImageFailureFresh(AvatarEntry entry, DateTimeOffset now)
    {
        return entry.ImageFailedAt is { } failedAt && failedAt <= now && now - failedAt < _options.MissLifetime;
    }

    private async Task CompleteResolveAsync(string id, TaskCompletionSource<AvatarUrlLookupResult> owner)
    {
        var result = AvatarUrlLookupResult.TransientFailure;

        try
        {
            result = await ResolveOneAsync(id).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Не удалось получить аватарку пользователя {UserId}", id);
        }
        finally
        {
            lock (_lock)
            {
                _inflight.Remove(id);
            }

            owner.TrySetResult(result);
        }
    }

    private async Task<AvatarUrlLookupResult> ResolveOneAsync(string id)
    {
        IReadOnlyList<UserInfo> users;

        try
        {
            users = await _helix.GetUsersByIdsAsync([id], CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            ReportOffline(DescribeFailure(exception));

            lock (_lock)
            {
                return _entries.TryGetValue(id, out var stale) && stale.Url is not null
                    ? AvatarUrlLookupResult.Found(stale.Url)
                    : AvatarUrlLookupResult.TransientFailure;
            }
        }

        string? url;

        lock (_lock)
        {
            url = Apply(id, Find(users, id), _timeProvider.GetUtcNow(), _changedOutsidePass);
            TrimEntries();
        }

        SaveIndexIfDirty();

        return url is not null
            ? AvatarUrlLookupResult.Found(url)
            : AvatarUrlLookupResult.NotFound;
    }

    private string? Apply(string id, UserInfo? user, DateTimeOffset now, ISet<string> changed)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            entry = new() { UsedAt = now };
            _entries[id] = entry;
        }

        var url = IsDownloadable(user?.ProfileImageUrl) ? user!.ProfileImageUrl : null;

        var knownUrl = entry.FileUrl ?? entry.Url;

        if (knownUrl is not null && !string.Equals(knownUrl, url, StringComparison.Ordinal))
        {
            changed.Add(id);
        }

        if (!string.Equals(entry.Url, url, StringComparison.Ordinal))
        {
            entry.ImageFailedAt = null;
        }

        entry.Url = url;
        entry.ResolvedAt = url is null ? null : now;
        entry.MissedAt = url is null ? now : null;
        _indexDirty = true;

        return url;
    }

    private List<string> CollectUnresolved(IReadOnlyCollection<string> userIds)
    {
        var result = new List<string>();

        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();

            foreach (var id in Normalize(userIds))
            {
                if (!_entries.TryGetValue(id, out var entry) || (!IsUrlFresh(entry, now) && !IsMissFresh(entry, now)))
                {
                    result.Add(id);
                }
            }
        }

        return result;
    }

    private List<string> CollectDownloads(List<string> ids, Dictionary<string, string> result)
    {
        var downloads = new List<string>();

        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();

            foreach (var id in ids)
            {
                if (!_entries.TryGetValue(id, out var entry))
                {
                    continue;
                }

                if (HasCurrentFile(entry) && File.Exists(PathOf(id)))
                {
                    result[id] = PathOf(id);
                }
                else if (entry.Url is not null && !IsImageFailureFresh(entry, now))
                {
                    downloads.Add(id);
                }
            }
        }

        return downloads;
    }

    private async Task DownloadAsync(List<string> ids, Dictionary<string, string> result, CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;

        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string url;

            lock (_lock)
            {
                if (!_entries.TryGetValue(id, out var entry)
                    || entry.Url is null
                    || IsImageFailureFresh(entry, _timeProvider.GetUtcNow()))
                {
                    continue;
                }

                if (HasCurrentFile(entry) && File.Exists(PathOf(id)))
                {
                    result[id] = PathOf(id);

                    continue;
                }

                url = entry.Url;
            }

            var outcome = await TryDownloadAsync(id, url, cancellationToken).ConfigureAwait(false);

            switch (outcome)
            {
                case AvatarDownload.Saved:
                    lock (_lock)
                    {
                        if (_entries.TryGetValue(id, out var entry) && string.Equals(entry.Url, url, StringComparison.Ordinal))
                        {
                            entry.FileUrl = url;
                            entry.ImageFailedAt = null;
                            entry.UsedAt = _timeProvider.GetUtcNow();
                            _indexDirty = true;
                            _sweepRequested = true;
                            result[id] = PathOf(id);
                        }
                    }

                    consecutiveFailures = 0;

                    break;

                case AvatarDownload.Rejected or AvatarDownload.NotSaved:
                    lock (_lock)
                    {
                        if (_entries.TryGetValue(id, out var entry) && string.Equals(entry.Url, url, StringComparison.Ordinal))
                        {
                            entry.ImageFailedAt = _timeProvider.GetUtcNow();
                            _indexDirty = true;
                        }
                    }

                    break;

                case AvatarDownload.NetworkFailed when ++consecutiveFailures >= MaxConsecutiveDownloadFailures:
                    _logger.LogDebug(
                        "Загрузка аватарок прервана: подряд {Count} отказа CDN Twitch на загрузке картинки",
                        consecutiveFailures);

                    return;
            }
        }
    }

    private async Task<AvatarDownload> TryDownloadAsync(string id, string url, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var candidate in DownloadCandidates(url))
            {
                using var client = _httpClientFactory.CreateClient(TwitchEndpoints.AvatarClient);
                using var response = await client
                    .GetAsync(new Uri(candidate, UriKind.Absolute), HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Аватарка пользователя {UserId} не загружена: ответ {Status}", id, (int)response.StatusCode);

                    continue;
                }

                return await SaveResponseAsync(id, response, cancellationToken).ConfigureAwait(false);
            }

            return AvatarDownload.Rejected;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Аватарка пользователя {UserId} не загружена: отказ CDN Twitch", id);
            ReportOffline("CDN Twitch недоступен");

            return AvatarDownload.NetworkFailed;
        }
    }

    private async Task<AvatarDownload> SaveResponseAsync(string id, HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType;

        if (mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Аватарка пользователя {UserId} не загружена: вместо картинки пришло {MediaType}", id, mediaType ?? "–");

            return AvatarDownload.Rejected;
        }

        if (response.Content.Headers.ContentLength > _options.MaxImageBytes)
        {
            _logger.LogDebug("Аватарка пользователя {UserId} не загружена: файл больше допустимого", id);

            return AvatarDownload.Rejected;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var bytes = await ReadBoundedAsync(stream, _options.MaxImageBytes, cancellationToken).ConfigureAwait(false);

        if (bytes is not { Length: > 0 } || !LooksLikeImage(bytes))
        {
            _logger.LogDebug("Аватарка пользователя {UserId} не загружена: пустой, слишком большой или не картинка", id);

            return AvatarDownload.Rejected;
        }

        try
        {
            AtomicFile.Save(PathOf(id), bytes, _logger, keepBackup: false);

            return AvatarDownload.Saved;
        }
        catch (Exception exception)
        {
            ReportWriteFailure(exception, "картинка пользователя " + id);

            return AvatarDownload.NotSaved;
        }
    }

    private void DropVanished(Dictionary<string, string> result)
    {
        List<string>? vanished = null;

        foreach (var (id, path) in result)
        {
            if (!File.Exists(path))
            {
                (vanished ??= []).Add(id);
            }
        }

        if (vanished is null)
        {
            return;
        }

        lock (_lock)
        {
            foreach (var id in vanished)
            {
                result.Remove(id);

                if (_entries.TryGetValue(id, out var entry) && entry.FileUrl is not null)
                {
                    entry.FileUrl = null;
                    _indexDirty = true;
                }
            }
        }

        _logger.LogDebug("Из выдачи кеша аватарок вычеркнуто {Count}: файлов на диске больше нет", vanished.Count);
    }

    private void Touch(IEnumerable<string> ids)
    {
        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();

            foreach (var id in ids)
            {
                if (_entries.TryGetValue(id, out var entry))
                {
                    Touch(entry, now);
                }
            }
        }
    }

    private void Touch(AvatarEntry entry, DateTimeOffset now)
    {
        if (entry.UsedAt <= now && now - entry.UsedAt < UseWriteInterval)
        {
            return;
        }

        entry.UsedAt = now;
        _indexDirty = true;
    }

    // TODO: MaxEntries считает только записи вне набора последнего ResolveAsync, поэтому индекс растёт на размер
    // страницы; появится второй потребитель пакетного разрешения со своим набором – закреплять наборы по потребителю
    private void TrimEntries()
    {
        if (_options.MaxEntries <= 0)
        {
            return;
        }

        var unpinned = _entries.Where(pair => !_pinned.Contains(pair.Key)).ToList();
        var excess = unpinned.Count - _options.MaxEntries;

        if (excess <= 0)
        {
            return;
        }

        foreach (var (id, _) in unpinned.OrderBy(pair => pair.Value.UsedAt).Take(excess).ToList())
        {
            _entries.Remove(id);
            _changedOutsidePass.Remove(id);
            _indexDirty = true;

            try
            {
                File.Delete(PathOf(id));
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Не удалось убрать аватарку вытесненного пользователя {UserId}", id);
            }
        }
    }

    private void EnsureIndexLoaded()
    {
        lock (_lock)
        {
            if (_indexLoaded)
            {
                return;
            }

            _indexLoaded = true;
            _sweepRequested = true;

            var file = Path.Combine(_options.Directory, IndexFileName);

            if (!File.Exists(file))
            {
                return;
            }

            try
            {
                var model = JsonSerializer.Deserialize<AvatarIndex>(File.ReadAllText(file), JsonStoreOptions.Default);

                if (model?.Users is null)
                {
                    _logger.LogWarning("Индекс кеша аватарок пуст или повреждён, он будет собран заново");

                    return;
                }

                foreach (var (rawId, entry) in model.Users)
                {
                    if (entry is null || !TryNormalizeId(rawId, out var id))
                    {
                        continue;
                    }

                    entry.Url = IsDownloadable(entry.Url) ? entry.Url : null;
                    entry.FileUrl = IsDownloadable(entry.FileUrl) ? entry.FileUrl : null;
                    _entries[id] = entry;
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Индекс кеша аватарок не прочитан, он будет собран заново: {File}", file);
            }
        }
    }

    // TODO: уборка каталога идёт под _lock на потоке вызывающего; увести в фон, когда каталог дорастёт
    // до тысяч файлов либо первая выдача аватарок начнёт подвисать
    private void SweepIfRequested()
    {
        lock (_lock)
        {
            if (!_sweepRequested)
            {
                return;
            }

            _sweepRequested = false;

            try
            {
                Sweep();
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Не удалось прибраться в кеше аватарок {Directory}", _options.Directory);
            }
        }

        SaveIndexIfDirty();
    }

    private void Sweep()
    {
        if (!Directory.Exists(_options.Directory))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var kept = new List<CachedImage>();
        var expired = 0;

        foreach (var path in Directory.EnumerateFiles(_options.Directory, "*" + ImageExtension))
        {
            var file = new FileInfo(path);

            if (!IsOwnFileName(file.Name))
            {
                continue;
            }

            var id = file.Name[..^ImageExtension.Length];
            var usedAt = _entries.TryGetValue(id, out var entry)
                ? entry.UsedAt
                : new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);

            if (_options.ImageLifetime > TimeSpan.Zero
                && usedAt <= now
                && now - usedAt >= _options.ImageLifetime
                && TryDiscard(file, id))
            {
                expired++;

                continue;
            }

            kept.Add(new(file, id, usedAt));
        }

        var keptBytes = kept.Sum(image => image.File.Length);
        var crowded = 0;

        if (_options.MaxTotalBytes > 0 && keptBytes > _options.MaxTotalBytes)
        {
            foreach (var image in kept.OrderBy(item => item.UsedAt))
            {
                if (keptBytes <= _options.MaxTotalBytes)
                {
                    break;
                }

                var length = image.File.Length;

                if (TryDiscard(image.File, image.Id))
                {
                    keptBytes -= length;
                    crowded++;
                }
            }
        }

        if (expired > 0 || crowded > 0)
        {
            _logger.LogInformation(
                "Кеш аватарок прибран: убрано по возрасту {Expired}, по потолку размера {Crowded}, осталось {Bytes} Б",
                expired,
                crowded,
                keptBytes);
        }
    }

    private bool TryDiscard(FileInfo file, string id)
    {
        try
        {
            file.Delete();

            if (_entries.TryGetValue(id, out var entry) && entry.FileUrl is not null)
            {
                entry.FileUrl = null;
                _indexDirty = true;
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Не удалось убрать аватарку {File} из кеша", file.Name);

            return false;
        }
    }

    private void SaveIndexIfDirty()
    {
        lock (_lock)
        {
            if (!_indexDirty)
            {
                return;
            }

            _indexDirty = false;

            var model = new AvatarIndex { Users = new(_entries, StringComparer.Ordinal) };

            try
            {
                var file = Path.Combine(_options.Directory, IndexFileName);
                AtomicFile.Save(file, JsonSerializer.Serialize(model, JsonStoreOptions.Default), _logger);
            }
            catch (Exception exception)
            {
                _indexDirty = true;

                ReportWriteFailure(exception, IndexFileName);
            }
        }
    }

    private void ReportWriteFailure(Exception exception, string what)
    {
        bool first;

        lock (_lock)
        {
            first = !_writeFailureReported;
            _writeFailureReported = true;
        }

        _logger.Log(
            first ? LogLevel.Warning : LogLevel.Debug,
            exception,
            "Кеш аватарок не записан на диск ({What}) в {Directory}",
            what,
            _options.Directory);
    }

    private void ReportOffline(string reason)
    {
        bool first;

        lock (_lock)
        {
            first = !_offlineReported;
            _offlineReported = true;
        }

        if (first)
        {
            _logger.LogWarning("Аватарки пользователей не загружаются: {Reason}. Вместо них показываются буквы ников", reason);
        }
        else
        {
            _logger.LogDebug("Аватарки пользователей по-прежнему не загружаются: {Reason}", reason);
        }
    }

    private enum AvatarDownload
    {
        None = 0,
        Saved = 1,
        Rejected = 2,
        NetworkFailed = 3,
        NotSaved = 4,
    }

    private sealed class AvatarIndex
    {
        public Dictionary<string, AvatarEntry> Users { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class AvatarEntry
    {
        public string? Url { get; set; }

        public DateTimeOffset? ResolvedAt { get; set; }

        public DateTimeOffset? MissedAt { get; set; }

        public string? FileUrl { get; set; }

        public DateTimeOffset? ImageFailedAt { get; set; }

        public DateTimeOffset UsedAt { get; set; }
    }

    private readonly record struct CachedImage(FileInfo File, string Id, DateTimeOffset UsedAt);
}
