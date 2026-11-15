using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Quota;
using System.Text.RegularExpressions;

namespace PoproshaykaBot.Core.YouTube.Streaming;

internal sealed partial class YouTubeTargetChannelResolver(
    IYouTubeLiveBroadcastClient client,
    YouTubeAccountsStore accountsStore,
    ILogger<YouTubeTargetChannelResolver> logger)
{
    private readonly Lock _sync = new();
    private readonly Dictionary<(string ProjectKey, string NormalizedHandle), string> _handleCache = new();
    private string? _lastWarnedMismatchKey;

    public async Task<string?> ResolveAsync(YouTubeSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var broadcaster = accountsStore.Load(PlatformAuthRole.Broadcaster);
        var broadcasterChannelId = broadcaster.ChannelId?.Trim() ?? string.Empty;
        var projectKey = YouTubeQuotaPolicy.NormalizeProjectKey(settings);

        string? resolvedChannelId;

        if (string.IsNullOrWhiteSpace(settings.Channel))
        {
            resolvedChannelId = string.IsNullOrWhiteSpace(broadcasterChannelId) ? null : broadcasterChannelId;
        }
        else if (TryExtractDirectChannelId(settings.Channel, out var directId))
        {
            resolvedChannelId = directId;
        }
        else
        {
            var normalizedHandle = NormalizeHandle(settings.Channel);

            if (string.IsNullOrWhiteSpace(normalizedHandle))
            {
                return null;
            }

            string? cached;

            lock (_sync)
            {
                _handleCache.TryGetValue((projectKey, normalizedHandle), out cached);
            }

            if (!string.IsNullOrEmpty(cached))
            {
                resolvedChannelId = cached;
            }
            else
            {
                resolvedChannelId = await client.ResolveChannelIdByHandleAsync(normalizedHandle, cancellationToken)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(resolvedChannelId))
                {
                    lock (_sync)
                    {
                        _handleCache[(projectKey, normalizedHandle)] = resolvedChannelId;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(resolvedChannelId)
            && !string.IsNullOrWhiteSpace(broadcasterChannelId)
            && !string.Equals(broadcasterChannelId, resolvedChannelId, StringComparison.Ordinal))
        {
            var mismatchKey = $"{broadcasterChannelId}->{resolvedChannelId}";

            lock (_sync)
            {
                if (!string.Equals(_lastWarnedMismatchKey, mismatchKey, StringComparison.Ordinal))
                {
                    _lastWarnedMismatchKey = mismatchKey;
                    logger.LogWarning(
                        "YouTube: целевой канал {TargetChannelId} отличается от канала авторизованного ведущего {BroadcasterChannelId}. liveBroadcasts.list возвращает только трансляции аккаунта ведущего",
                        resolvedChannelId,
                        broadcasterChannelId);
                }
            }
        }

        return resolvedChannelId;
    }

    internal static bool TryExtractDirectChannelId(string rawInput, out string channelId)
    {
        var trimmed = rawInput.Trim();

        if (ChannelUrlRegex().Match(trimmed) is { Success: true } urlMatch)
        {
            channelId = urlMatch.Groups["id"].Value;
            return true;
        }

        if (BareChannelIdRegex().IsMatch(trimmed))
        {
            channelId = trimmed;
            return true;
        }

        channelId = string.Empty;
        return false;
    }

    internal static string? NormalizeHandle(string rawInput)
    {
        var trimmed = rawInput.Trim();

        if (HandleUrlRegex().Match(trimmed) is { Success: true } urlMatch)
        {
            return "@" + urlMatch.Groups["handle"].Value.TrimStart('@');
        }

        if (trimmed.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        var stripped = trimmed.TrimStart('@');
        return string.IsNullOrWhiteSpace(stripped) ? null : "@" + stripped;
    }

    [GeneratedRegex(@"^https?://(?:www\.|m\.)?youtube\.com/channel/(?<id>UC[A-Za-z0-9_-]{10,})(?:[/?#].*)?$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex ChannelUrlRegex();

    [GeneratedRegex(@"^UC[A-Za-z0-9_-]{20,}$", RegexOptions.None, matchTimeoutMilliseconds: 500)]
    private static partial Regex BareChannelIdRegex();

    [GeneratedRegex(@"^https?://(?:www\.|m\.)?youtube\.com/@(?<handle>[^/?#\s]+)(?:[/?#].*)?$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex HandleUrlRegex();
}
