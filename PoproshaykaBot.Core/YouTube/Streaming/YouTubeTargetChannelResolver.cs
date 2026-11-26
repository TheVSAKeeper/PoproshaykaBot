using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube.Quota;

namespace PoproshaykaBot.Core.YouTube.Streaming;

internal sealed class YouTubeTargetChannelResolver(
    IYouTubeLiveBroadcastClient client,
    YouTubeAccountsStore accountsStore,
    ILogger<YouTubeTargetChannelResolver> logger)
{
    private readonly Lock _sync = new();
    private readonly Dictionary<(string ProjectKey, string NormalizedHandle), string> _handleCache = new();
    private string? _lastWarnedMismatchKey;

    public Task<string?> ResolveAsync(YouTubeSettings settings, CancellationToken cancellationToken)
    {
        return ResolveAsync(settings, PlatformAuthRole.Broadcaster, null, cancellationToken);
    }

    public async Task<string?> ResolveAsync(
        YouTubeSettings settings,
        PlatformAuthRole resolveRole,
        string? contextId,
        CancellationToken cancellationToken)
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
                resolvedChannelId = resolveRole == PlatformAuthRole.Broadcaster && AuthContextIds.IsDefault(contextId)
                    ? await client.ResolveChannelIdByHandleAsync(normalizedHandle, cancellationToken).ConfigureAwait(false)
                    : await client.ResolveChannelIdByHandleAsync(normalizedHandle, resolveRole, contextId, cancellationToken)
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

        if (resolveRole == PlatformAuthRole.Broadcaster
            && !string.IsNullOrWhiteSpace(resolvedChannelId)
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

    internal static bool TryExtractDirectChannelId(string rawInput, out string channelId) =>
        YouTubeChannelInput.TryExtractDirectChannelId(rawInput, out channelId);

    internal static string? NormalizeHandle(string rawInput) =>
        YouTubeChannelInput.NormalizeHandle(rawInput);
}
