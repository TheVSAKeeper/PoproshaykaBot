using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.Server;

internal static class ObsOverlayMessageFilter
{
    internal static readonly TimeSpan PreStreamGraceWindow = TimeSpan.FromMinutes(5);

    public static bool Allows(ChatMessageData entry, ChatSettings chatSettings)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(chatSettings);

        if (entry.CommandResponse is { } mark && !mark.Target.GoesToOverlay())
        {
            return false;
        }

        return !entry.IsHistorical || chatSettings.ShowHistoryInObs;
    }

    public static bool AllowsCurrentStream(
        ChatMessageData entry,
        IChatPlatformRegistry? platformRegistry,
        IYouTubeActiveBroadcastSource? youtubeBroadcastSource = null,
        IBroadcastState? broadcastState = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (platformRegistry is null)
        {
            return true;
        }

        if (!platformRegistry.TryGet(entry.Platform, out var platform))
        {
            return !entry.IsHistorical;
        }

        var status = platform.StreamStatus;
        if (status.CurrentStatus != StreamStatus.Online || status.CurrentStream is not { } stream)
        {
            if (entry.IsHistorical
                || entry.BroadcastId is not null
                || !string.IsNullOrWhiteSpace(entry.StreamId)
                || entry.StreamStartedAtUtc is not null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(entry.ChatSessionId))
            {
                return false;
            }

            return true;
        }

        if (entry.Platform == StreamingPlatform.YouTube
            && youtubeBroadcastSource?.CurrentBroadcast is { } broadcast
            && !string.IsNullOrWhiteSpace(broadcast.LiveChatId)
            && !string.IsNullOrWhiteSpace(entry.ChatSessionId))
        {
            return string.Equals(entry.ChatSessionId, broadcast.LiveChatId, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(entry.StreamId)
            && !string.IsNullOrWhiteSpace(stream.Id))
        {
            return string.Equals(entry.StreamId, stream.Id, StringComparison.Ordinal);
        }

        if (entry.StreamStartedAtUtc is { } entryStartedAt && stream.StartedAt != default)
        {
            var streamStartUtc = DateTime.SpecifyKind(stream.StartedAt, DateTimeKind.Utc);
            return Math.Abs((ChatMessageDataFactory.EnsureUtc(entryStartedAt) - streamStartUtc).TotalMinutes) <= 1.0;
        }

        if (entry.BroadcastId is { } entryBroadcastId
            && broadcastState?.CurrentBroadcastId is { } currentBroadcastId)
        {
            return entryBroadcastId == currentBroadcastId;
        }

        if (stream.StartedAt != default)
        {
            var streamStartUtc = DateTime.SpecifyKind(stream.StartedAt, DateTimeKind.Utc) - PreStreamGraceWindow;
            var entryUtc = ChatMessageDataFactory.EnsureUtc(entry.Timestamp);
            return entryUtc >= streamStartUtc;
        }

        return !entry.IsHistorical;
    }
}
