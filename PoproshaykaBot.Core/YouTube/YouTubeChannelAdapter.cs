using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.YouTube;

internal sealed class YouTubeChannelAdapter(
    IYouTubeLiveBroadcastClient broadcastClient,
    IYouTubeChannelIdentityClient identityClient,
    IYouTubeBroadcastSelectionStore selectionStore,
    YouTubeAccountsStore accountsStore,
    [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformAuthService authService,
    ILogger<YouTubeChannelAdapter> logger) : IPlatformChannelAdapter
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public PlatformCapabilities Capabilities =>
        PlatformCapabilities.BackgroundStreamMonitoring
        | PlatformCapabilities.MultiChannelMonitoring
        | PlatformCapabilities.ViewerCountSampling;

    public async Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(
        string rawInput,
        string? preferredContextId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return null;
        }

        var trimmed = rawInput.Trim();
        var contextId = AuthContextIds.Normalize(preferredContextId);

        if (YouTubeTargetChannelResolver.TryExtractDirectChannelId(trimmed, out var directId))
        {
            var matchedContext = accountsStore.GetAllAccounts(PlatformAuthRole.Broadcaster)
                .FirstOrDefault(a => string.Equals(a.ChannelId, directId, StringComparison.Ordinal));
            var displayName = !string.IsNullOrWhiteSpace(matchedContext?.ChannelTitle)
                ? matchedContext.ChannelTitle
                : directId;
            return new(
                new ChannelKey(StreamingPlatform.YouTube, directId),
                displayName,
                trimmed,
                $"https://youtube.com/channel/{directId}");
        }

        var handle = YouTubeTargetChannelResolver.NormalizeHandle(trimmed);
        if (string.IsNullOrWhiteSpace(handle))
        {
            return null;
        }

        using var _ = PlatformAuthRequestOptions.UseContext(contextId);
        var resolvedId = await broadcastClient.ResolveChannelIdByHandleAsync(handle, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(resolvedId))
        {
            return null;
        }

        return new(
            new ChannelKey(StreamingPlatform.YouTube, resolvedId),
            handle,
            handle,
            $"https://youtube.com/{handle}");
    }

    public async Task<ChannelAccessVerification> VerifyChannelAccessAsync(
        ChannelKey channel,
        PlatformAuthRole role,
        string contextId,
        CancellationToken cancellationToken)
    {
        if (channel.Platform != StreamingPlatform.YouTube || string.IsNullOrWhiteSpace(channel.ChannelId))
        {
            return new(false, null, "Некорректный идентификатор канала YouTube.");
        }

        var normalizedContext = AuthContextIds.Normalize(contextId);
        if (!accountsStore.TryLoadContext(role, normalizedContext, out var account) || account is null)
        {
            return new(false, null, $"Контекст авторизации YouTube «{normalizedContext}» не найден.");
        }

        var token = await authService.GetAccessTokenAsync(role, normalizedContext, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(false, null, $"Токен YouTube роли {PlatformAuthRoles.Describe(role)} отсутствует или просрочен.");
        }

        account = accountsStore.Load(role, normalizedContext);
        var requiredScopes = YouTubeScopes.RequiredFor(role);
        if (!requiredScopes.All(req => YouTubeScopes.IsGranted(account, req)))
        {
            return new(false, account.ChannelId, $"Недостаточно прав (scopes) YouTube для роли {PlatformAuthRoles.Describe(role)}.");
        }

        if (role == PlatformAuthRole.Bot)
        {
            var botChannelId = string.IsNullOrWhiteSpace(account.ChannelId) ? null : account.ChannelId.Trim();
            return string.IsNullOrWhiteSpace(botChannelId)
                ? new(false, null, "У аккаунта бота не определён идентификатор канала YouTube.")
                : new(true, botChannelId);
        }

        try
        {
            var identity = await identityClient.GetOwnChannelAsync(token, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(identity.ChannelId, channel.ChannelId, StringComparison.Ordinal))
            {
                return new(false, identity.ChannelId, $"Авторизованный аккаунт ведёт канал {identity.ChannelTitle} ({identity.ChannelId}), а не {channel.ChannelId}.");
            }

            return new(true, identity.ChannelId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ошибка проверки доступа YouTube для {ChannelId} (контекст {ContextId})", channel.ChannelId, normalizedContext);
            return new(false, account.ChannelId, StreamingErrorMessages.SafeMessage(ex));
        }
    }

    public async Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(
        ChannelCatalogEntry channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (string.IsNullOrWhiteSpace(channel.ChannelId))
        {
            return new(false, false, null, FailureReason: "Не задан идентификатор канала YouTube.");
        }

        var broadcasterContextId = AuthContextIds.Normalize(channel.BroadcasterContextId);
        var broadcasterToken = await authService.GetAccessTokenAsync(PlatformAuthRole.Broadcaster, broadcasterContextId, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(broadcasterToken))
        {
            return new(false, false, null, FailureReason: "Нет авторизации ведущего для канала YouTube.");
        }

        var broadcasterAccount = accountsStore.Load(PlatformAuthRole.Broadcaster, broadcasterContextId);
        if (!string.IsNullOrWhiteSpace(broadcasterAccount.ChannelId)
            && !string.Equals(broadcasterAccount.ChannelId, channel.ChannelId, StringComparison.Ordinal))
        {
            return new(false, false, null, FailureReason: "Выбранный контекст ведущего принадлежит другому каналу YouTube.");
        }

        var botContextId = AuthContextIds.Normalize(channel.BotContextId);
        var botToken = await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, botContextId, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        var hasBotAuth = !string.IsNullOrWhiteSpace(botToken);

        try
        {
            var broadcasts = await broadcastClient.ListActiveBroadcastsAsync(broadcasterContextId, cancellationToken).ConfigureAwait(false);
            var matches = broadcasts
                .Where(b => string.Equals(b.ChannelId, channel.ChannelId, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length == 0)
            {
                return new(false, hasBotAuth, null, FailureReason: "Активная трансляция на канале YouTube не найдена.");
            }

            var selectedVideoId = selectionStore.GetSelectedVideoId(channel.ChannelId);
            var selected = matches.Length == 1
                ? matches[0]
                : (!string.IsNullOrWhiteSpace(selectedVideoId)
                    ? matches.FirstOrDefault(m => string.Equals(m.VideoId, selectedVideoId, StringComparison.Ordinal))
                    : null);

            if (selected is null)
            {
                return new(true, false, null, FailureReason: "На канале найдено несколько активных трансляций – выберите нужную.");
            }

            var stream = new StreamInfo
            {
                Id = selected.VideoId,
                UserId = selected.ChannelId,
                UserLogin = selected.ChannelId,
                UserName = selected.ChannelTitle,
                Title = selected.Title,
                StartedAt = selected.StartedAtUtc.UtcDateTime,
            };

            if (!hasBotAuth)
            {
                return new(true, false, stream, selected.LiveChatId, "Нет авторизации бота для отправки в чат YouTube.");
            }

            if (string.IsNullOrWhiteSpace(selected.LiveChatId))
            {
                return new(true, false, stream, null, "Чат трансляции YouTube недоступен или отключён.");
            }

            return new(true, true, stream, selected.LiveChatId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ошибка проверки эфира YouTube для {ChannelId}", channel.ChannelId);
            return new(false, false, null, FailureReason: StreamingErrorMessages.SafeMessage(ex));
        }
    }
}
