using PoproshaykaBot.Core.YouTube.Chat.InnerTube.Models;
using PoproshaykaBot.Core.YouTube.Chat.Transport;
using System.Runtime.CompilerServices;

namespace PoproshaykaBot.Core.YouTube.Chat.InnerTube;

internal sealed class InnerTubeYouTubeLiveChatReader(
    InnerTubeLiveChatClient client,
    YouTubeChatReaderOptions? readerOptions = null,
    TimeProvider? timeProvider = null)
    : IYouTubeLiveChatReader
{
    private const int CredentialCacheCapacity = 8;

    private readonly bool _retainRawPayloads = readerOptions?.RetainRawPayloads == true;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _cacheLock = new();
    private readonly List<CachedCredentialEntry> _credentialsCache = [];

    public YouTubeChatTransportKind TransportKind => YouTubeChatTransportKind.InnerTube;

    public bool IsServerStream => false;

    public bool CanRead(YouTubeChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return !target.RequiresOfficialMessageIds && !string.IsNullOrWhiteSpace(target.VideoId);
    }

    public IAsyncEnumerable<YouTubeLiveChatBatch> ReadBatchesAsync(
        YouTubeChatTarget target,
        YouTubeChatCursorToken? cursor,
        CancellationToken cancellationToken)
    {
        return ReadBatchesAsync(target, cursor, maxBatches: null, cancellationToken);
    }

    public IAsyncEnumerable<YouTubeLiveChatBatch> ReadBatchesAsync(
        YouTubeChatTarget target,
        YouTubeChatCursorToken? cursor,
        int? maxBatches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (string.IsNullOrWhiteSpace(target.VideoId))
        {
            throw new ArgumentException("Требуется VideoId для чтения InnerTube.", nameof(target));
        }

        if (maxBatches is <= 0)
        {
            return EmptyBatchesAsync();
        }

        return ReadBatchesCoreAsync(target, cursor, maxBatches, cancellationToken);
    }

    private async IAsyncEnumerable<YouTubeLiveChatBatch> ReadBatchesCoreAsync(
        YouTubeChatTarget target,
        YouTubeChatCursorToken? cursor,
        int? maxBatches,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var yielded = 0;
        var currentCursor = cursor is { Transport: YouTubeChatTransportKind.InnerTube }
            && cursor.Mode == target.Mode
            && !string.IsNullOrWhiteSpace(cursor.Value)
            ? cursor
            : null;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await FetchSingleBatchAsync(target, currentCursor, cancellationToken).ConfigureAwait(false);
            yield return batch;

            yielded++;

            if (maxBatches is not null && yielded >= maxBatches.Value)
            {
                yield break;
            }

            if (batch.NextCursor is null)
            {
                yield break;
            }

            currentCursor = batch.NextCursor;

            if (batch.PollingInterval > TimeSpan.Zero)
            {
                await Task.Delay(batch.PollingInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<YouTubeLiveChatBatch> FetchSingleBatchAsync(
        YouTubeChatTarget target,
        YouTubeChatCursorToken? cursor,
        CancellationToken cancellationToken)
    {
        var videoId = target.VideoId!;
        SessionCredentials? credentials = null;

        if (cursor is not null)
        {
            credentials = TryGetCredentials(videoId);
        }
        else
        {
            RemoveCredentials(videoId);
        }

        string continuationToken;
        var isBootstrap = false;

        if (cursor is not null && credentials is not null)
        {
            continuationToken = cursor.Value;
        }
        else
        {
            var bootstrap = await client.FetchWatchBootstrapAsync(videoId, cancellationToken).ConfigureAwait(false);
            credentials = new(bootstrap.ApiKey, bootstrap.ClientVersion);
            StoreCredentials(videoId, credentials);
            continuationToken = bootstrap.InitialContinuation;
            isBootstrap = true;
        }

        var response = await client.FetchChatPageAsync(
            credentials.ApiKey,
            CreateRequest(credentials.ClientVersion, continuationToken),
            cancellationToken).ConfigureAwait(false);

        var parsed = InnerTubeLiveChatParser.ParseBatch(response, _retainRawPayloads);

        if (isBootstrap
            && target.Mode == YouTubeChatMode.LiveChat
            && !string.IsNullOrWhiteSpace(parsed.LiveChatSwitchContinuation))
        {
            var liveResponse = await client.FetchChatPageAsync(
                credentials.ApiKey,
                CreateRequest(credentials.ClientVersion, parsed.LiveChatSwitchContinuation),
                cancellationToken).ConfigureAwait(false);

            parsed = InnerTubeLiveChatParser.ParseBatch(liveResponse, _retainRawPayloads);
        }

        if (parsed.ContinuationKind == InnerTubeContinuationKind.Reload && !isBootstrap)
        {
            throw new InnerTubeChatException(
                InnerTubeErrorKind.ContinuationInvalidated,
                "InnerTube вернул reloadContinuationData для активного курсора.");
        }

        var nextCursor = !string.IsNullOrWhiteSpace(parsed.NextContinuation)
            ? new YouTubeChatCursorToken(YouTubeChatTransportKind.InnerTube, target.Mode, parsed.NextContinuation)
            : null;

        return new(
            parsed.Items,
            parsed.NextContinuation,
            parsed.PollingInterval,
            OfflineAtUtc: null,
            nextCursor,
            YouTubeChatTransportKind.InnerTube);
    }

    private static LiveChatRequest CreateRequest(string clientVersion, string continuation) =>
        new()
        {
            Context = new()
            {
                Client = new()
                {
                    ClientName = InnerTubeEndpoints.WebClientName,
                    ClientVersion = clientVersion,
                    Hl = "en",
                    Gl = "US",
                },
            },
            Continuation = continuation,
        };

    private SessionCredentials? TryGetCredentials(string videoId)
    {
        lock (_cacheLock)
        {
            for (var i = 0; i < _credentialsCache.Count; i++)
            {
                if (string.Equals(_credentialsCache[i].VideoId, videoId, StringComparison.Ordinal))
                {
                    return _credentialsCache[i].Credentials;
                }
            }

            return null;
        }
    }

    private void StoreCredentials(string videoId, SessionCredentials credentials)
    {
        lock (_cacheLock)
        {
            RemoveCredentialsLocked(videoId);

            if (_credentialsCache.Count >= CredentialCacheCapacity)
            {
                _credentialsCache.RemoveAt(0);
            }

            _credentialsCache.Add(new(videoId, credentials));
        }
    }

    private void RemoveCredentials(string videoId)
    {
        lock (_cacheLock)
        {
            RemoveCredentialsLocked(videoId);
        }
    }

    private void RemoveCredentialsLocked(string videoId)
    {
        for (var i = _credentialsCache.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_credentialsCache[i].VideoId, videoId, StringComparison.Ordinal))
            {
                _credentialsCache.RemoveAt(i);
            }
        }
    }

    private static async IAsyncEnumerable<YouTubeLiveChatBatch> EmptyBatchesAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    private sealed record CachedCredentialEntry(string VideoId, SessionCredentials Credentials);

    private sealed record SessionCredentials(string ApiKey, string ClientVersion);
}
