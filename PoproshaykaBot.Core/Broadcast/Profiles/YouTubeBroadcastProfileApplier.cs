using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Streaming;
using System.Globalization;

namespace PoproshaykaBot.Core.Broadcast.Profiles;

internal sealed class YouTubeBroadcastProfileApplier(
    ChannelCatalogStore channelCatalogStore,
    YouTubeAccountsStore accountsStore,
    IYouTubeActiveBroadcastSource activeBroadcastSource,
    IYouTubeLiveBroadcastClient broadcastClient,
    ILogger<YouTubeBroadcastProfileApplier> logger)
    : IYouTubeBroadcastProfileApplier
{
    private const string MissingAuthMessage = "Нет авторизации владельца канала YouTube";
    private const string MissingWriteScopeMessage = "Нет прав записи YouTube (нужен объединённый вход или право youtube.force-ssl)";
    private const string NoActiveBroadcastMessage = "На YouTube нет активной трансляции";
    private const string MissingTitleMessage = "Не указано название трансляции для YouTube";

    public async Task<BroadcastProfilePlatformApplyResult> ApplyAsync(
        BroadcastProfile profile,
        BroadcastProfileYouTubeMetadata? youtubeMetadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var account = YouTubeBroadcastWriteAccess.ResolveBroadcasterAccount(
            channelCatalogStore,
            accountsStore,
            out var contextId,
            out var activeChannel);

        if (!YouTubeBroadcastWriteAccess.HasUsableCredentials(account))
        {
            logger.LogInformation("Профиль {ProfileName} не применён к YouTube: нет авторизации владельца канала", profile.Name);
            return new(StreamingPlatform.YouTube, false, MissingAuthMessage);
        }

        if (!YouTubeBroadcastWriteAccess.HasWriteScope(account))
        {
            logger.LogInformation("Профиль {ProfileName} не применён к YouTube: у аккаунта владельца нет прав записи", profile.Name);
            return new(StreamingPlatform.YouTube, false, MissingWriteScopeMessage);
        }

        var (broadcast, discoveryError) = await ResolveActiveBroadcastAsync(activeChannel, contextId, cancellationToken)
            .ConfigureAwait(false);

        if (discoveryError is not null)
        {
            return new(StreamingPlatform.YouTube, false, discoveryError);
        }

        if (broadcast is null)
        {
            logger.LogInformation("Профиль {ProfileName} не применён к YouTube: активная трансляция не найдена", profile.Name);
            return new(StreamingPlatform.YouTube, false, NoActiveBroadcastMessage);
        }

        var normalized = youtubeMetadata?.Normalize();
        var numberText = profile.CurrentNumber.ToString(CultureInfo.InvariantCulture);

        var effectiveTitle = !string.IsNullOrWhiteSpace(normalized?.TitleOverride)
            ? RenderTemplate(normalized.TitleOverride, numberText)
            : profile.Title?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(effectiveTitle))
        {
            effectiveTitle = broadcast.Title?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(effectiveTitle))
        {
            return new(StreamingPlatform.YouTube, false, MissingTitleMessage);
        }

        var effectiveDescription = !string.IsNullOrWhiteSpace(normalized?.Description)
            ? RenderTemplate(normalized.Description, numberText)
            : broadcast.Description;

        var request = new YouTubeBroadcastUpdateRequest(
            broadcast.VideoId,
            effectiveTitle,
            effectiveDescription,
            broadcast.ScheduledStartTimeUtc,
            normalized?.PrivacyStatus);

        var updateResult = await broadcastClient
            .UpdateBroadcastMetadataAsync(request, contextId, cancellationToken)
            .ConfigureAwait(false);

        if (updateResult.IsSuccess)
        {
            logger.LogInformation(
                "Профиль {ProfileName} применён к трансляции YouTube {VideoId}",
                profile.Name,
                broadcast.VideoId);
            return new(StreamingPlatform.YouTube, true);
        }

        logger.LogWarning(
            "Не удалось применить профиль {ProfileName} к трансляции YouTube {VideoId}: {ErrorKind} – {ErrorMessage}",
            profile.Name,
            broadcast.VideoId,
            updateResult.ErrorKind,
            updateResult.ErrorMessage);

        return new(
            StreamingPlatform.YouTube,
            false,
            string.IsNullOrWhiteSpace(updateResult.ErrorMessage)
                ? "Не удалось обновить трансляцию YouTube"
                : updateResult.ErrorMessage);
    }

    private async Task<(YouTubeBroadcastSummary? Broadcast, string? ErrorMessage)> ResolveActiveBroadcastAsync(
        ChannelCatalogEntry? activeChannel,
        string? contextId,
        CancellationToken cancellationToken)
    {
        var cached = activeChannel is not null
            ? activeBroadcastSource.GetActiveBroadcast(new(StreamingPlatform.YouTube, activeChannel.ChannelId))
              ?? activeBroadcastSource.CurrentBroadcast
            : activeBroadcastSource.CurrentBroadcast;

        if (cached is not null && cached.ScheduledStartTimeUtc.HasValue)
        {
            return (cached, null);
        }

        try
        {
            var broadcasts = await broadcastClient
                .ListActiveBroadcastsAsync(contextId, cancellationToken)
                .ConfigureAwait(false);

            var matched = SelectBroadcast(broadcasts, cached, activeChannel);
            return (matched ?? cached, null);
        }
        catch (YouTubeApiException ex)
        {
            logger.LogWarning(ex, "Не удалось получить активную трансляцию YouTube перед применением профиля");
            if (cached is not null)
            {
                return (cached, null);
            }

            return (null, FormatDiscoveryError(ex));
        }
    }

    private static YouTubeBroadcastSummary? SelectBroadcast(
        IReadOnlyList<YouTubeBroadcastSummary> broadcasts,
        YouTubeBroadcastSummary? cached,
        ChannelCatalogEntry? activeChannel)
    {
        if (broadcasts.Count == 0)
        {
            return null;
        }

        if (cached is not null)
        {
            var byVideoId = broadcasts.FirstOrDefault(item =>
                string.Equals(item.VideoId, cached.VideoId, StringComparison.Ordinal));
            if (byVideoId is not null)
            {
                return byVideoId;
            }
        }

        if (activeChannel is not null && !string.IsNullOrWhiteSpace(activeChannel.ChannelId))
        {
            var byChannelId = broadcasts.FirstOrDefault(item =>
                string.Equals(item.ChannelId, activeChannel.ChannelId, StringComparison.OrdinalIgnoreCase));
            if (byChannelId is not null)
            {
                return byChannelId;
            }
        }

        return broadcasts[0];
    }

    private static string RenderTemplate(string value, string numberText)
    {
        var template = MessageTemplate.For(value);
        return template.Contains("n")
            ? template.With("n", numberText).Render()
            : value;
    }

    private static string FormatDiscoveryError(YouTubeApiException exception)
    {
        return exception.Kind switch
        {
            YouTubeApiErrorKind.Unauthorized => MissingAuthMessage,
            YouTubeApiErrorKind.Forbidden => "Недостаточно прав для чтения активной трансляции YouTube",
            YouTubeApiErrorKind.QuotaExceeded => "Исчерпана квота YouTube API",
            YouTubeApiErrorKind.LocalQuotaBudgetReached => "Исчерпан локальный бюджет квоты YouTube API",
            YouTubeApiErrorKind.Transient => "Сетевая ошибка при обращении к YouTube API",
            _ => "Не удалось получить активную трансляцию YouTube",
        };
    }
}
