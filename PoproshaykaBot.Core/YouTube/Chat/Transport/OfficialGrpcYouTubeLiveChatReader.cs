using System.Runtime.CompilerServices;

namespace PoproshaykaBot.Core.YouTube.Chat.Transport;

internal sealed class OfficialGrpcYouTubeLiveChatReader(IYouTubeLiveChatStreamClient streamClient)
    : IYouTubeLiveChatReader
{
    public YouTubeChatTransportKind TransportKind => YouTubeChatTransportKind.OfficialGrpc;

    public bool IsServerStream => true;

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
            throw new ArgumentException("Требуется LiveChatId для чтения официального gRPC-потока.", nameof(target));
        }

        if (maxBatches is <= 0)
        {
            return EmptyBatchesAsync();
        }

        var pageToken = cursor is { Transport: YouTubeChatTransportKind.OfficialGrpc or YouTubeChatTransportKind.OfficialRest }
            && !string.IsNullOrWhiteSpace(cursor.Value)
            ? cursor.Value
            : null;

        return ReadBatchesCoreAsync(target.LiveChatId, pageToken, maxBatches, cancellationToken);
    }

    private async IAsyncEnumerable<YouTubeLiveChatBatch> ReadBatchesCoreAsync(
        string liveChatId,
        string? pageToken,
        int? maxBatches,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var yielded = 0;

        await foreach (var batch in streamClient.StreamBatchesAsync(liveChatId, pageToken, cancellationToken).ConfigureAwait(false))
        {
            yield return NormalizeBatch(batch, YouTubeChatTransportKind.OfficialGrpc);
            yielded++;

            if (maxBatches is not null && yielded >= maxBatches.Value)
            {
                yield break;
            }
        }
    }

    private static async IAsyncEnumerable<YouTubeLiveChatBatch> EmptyBatchesAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }

    internal static YouTubeLiveChatBatch NormalizeBatch(YouTubeLiveChatBatch batch, YouTubeChatTransportKind transport)
    {
        var nextCursor = batch.NextCursor;

        if (nextCursor is null && !string.IsNullOrWhiteSpace(batch.NextPageToken))
        {
            nextCursor = new(transport, YouTubeChatMode.LiveChat, batch.NextPageToken);
        }

        return batch.Transport == transport && ReferenceEquals(batch.NextCursor, nextCursor)
            ? batch
            : batch with
            {
                Transport = transport,
                NextCursor = nextCursor,
            };
    }
}
