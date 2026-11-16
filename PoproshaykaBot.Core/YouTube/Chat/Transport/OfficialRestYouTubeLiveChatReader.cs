using System.Runtime.CompilerServices;

namespace PoproshaykaBot.Core.YouTube.Chat.Transport;

internal sealed class OfficialRestYouTubeLiveChatReader(
    IYouTubeLiveChatPollingClient pollingClient,
    TimeProvider? timeProvider = null)
    : IYouTubeLiveChatReader
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public YouTubeChatTransportKind TransportKind => YouTubeChatTransportKind.OfficialRest;

    public bool IsServerStream => false;

    public bool CanRead(YouTubeChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return !string.IsNullOrWhiteSpace(target.LiveChatId);
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

        if (string.IsNullOrWhiteSpace(target.LiveChatId))
        {
            throw new ArgumentException("Требуется LiveChatId для чтения официального REST-чата.", nameof(target));
        }

        if (maxBatches is <= 0)
        {
            return EmptyBatchesAsync();
        }

        var initialToken = cursor is { Transport: YouTubeChatTransportKind.OfficialGrpc or YouTubeChatTransportKind.OfficialRest }
            && !string.IsNullOrWhiteSpace(cursor.Value)
            ? cursor.Value
            : null;

        return ReadBatchesCoreAsync(target.LiveChatId, initialToken, maxBatches, cancellationToken);
    }

    private async IAsyncEnumerable<YouTubeLiveChatBatch> ReadBatchesCoreAsync(
        string liveChatId,
        string? pageToken,
        int? maxBatches,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var yielded = 0;
        var currentToken = pageToken;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rawBatch = await pollingClient.ListMessagesAsync(liveChatId, currentToken, cancellationToken).ConfigureAwait(false);
            var batch = OfficialGrpcYouTubeLiveChatReader.NormalizeBatch(rawBatch, YouTubeChatTransportKind.OfficialRest);
            yield return batch;

            yielded++;

            if (maxBatches is not null && yielded >= maxBatches.Value)
            {
                yield break;
            }

            if (IsEnded(batch))
            {
                yield break;
            }

            currentToken = batch.NextCursor?.Value ?? batch.NextPageToken;

            if (batch.PollingInterval > TimeSpan.Zero)
            {
                await Task.Delay(batch.PollingInterval, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static bool IsEnded(YouTubeLiveChatBatch batch)
    {
        if (batch.OfflineAtUtc is not null)
        {
            return true;
        }

        foreach (var item in batch.Items)
        {
            if (item.Kind == YouTubeLiveChatItemKind.ChatEnded)
            {
                return true;
            }
        }

        return false;
    }

    private static async IAsyncEnumerable<YouTubeLiveChatBatch> EmptyBatchesAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
