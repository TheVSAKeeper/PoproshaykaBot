using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using PoproshaykaBot.Core.YouTube.Streaming;

namespace PoproshaykaBot.Core.YouTube;

internal sealed class YouTubeChannelAdapter(
    IYouTubeLiveBroadcastClient broadcastClient,
    IYouTubeChannelIdentityClient identityClient,
    IYouTubeBroadcastSelectionStore selectionStore,
    YouTubeAccountsStore accountsStore,
    [FromKeyedServices(StreamingPlatform.YouTube)] IPlatformAuthService authService,
    ILogger<YouTubeChannelAdapter> logger,
    IYouTubePublicLiveDiscoveryClient? publicDiscovery = null) : IPlatformChannelAdapter
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
                .Concat(accountsStore.GetAllAccounts(PlatformAuthRole.Bot))
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

        var candidates = await CollectHandleResolutionContextsAsync(contextId, cancellationToken)
            .ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return null;
        }

        string? resolvedId = null;
        for (var i = 0; i < candidates.Count; i++)
        {
            var (role, resolvedContextId) = candidates[i];
            try
            {
                if (role == PlatformAuthRole.Broadcaster && AuthContextIds.IsDefault(resolvedContextId))
                {
                    using var _ = PlatformAuthRequestOptions.UseContext(AuthContextIds.Default);
                    resolvedId = await broadcastClient
                        .ResolveChannelIdByHandleAsync(handle, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    resolvedId = await broadcastClient
                        .ResolveChannelIdByHandleAsync(handle, role, resolvedContextId, cancellationToken)
                        .ConfigureAwait(false);
                }

                break;
            }
            catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized && i + 1 < candidates.Count)
            {
                logger.LogDebug(ex, "Контекст YouTube {Role}/{ContextId} вернул 401 при поиске канала {Handle}, пробуем следующий", role, resolvedContextId, handle);
            }
        }

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

        var broadcasterAssignment = channel.GetAccountAssignment(PlatformAuthRole.Broadcaster);
        var broadcasterContextId = AuthContextIds.Normalize(broadcasterAssignment?.ContextId ?? channel.BroadcasterContextId);
        var broadcasterToken = await GetTokenAsync(PlatformAuthRole.Broadcaster, broadcasterContextId, cancellationToken).ConfigureAwait(false);
        var broadcasterAccount = accountsStore.Load(PlatformAuthRole.Broadcaster, broadcasterContextId);
        var broadcasterMismatchReason = broadcasterAssignment switch
        {
            { IsVerified: false } => "Доступ аккаунта ведущего к каналу YouTube не подтверждён.",
            _ when !string.IsNullOrWhiteSpace(broadcasterAccount.ChannelId)
                && !string.Equals(broadcasterAccount.ChannelId, channel.ChannelId, StringComparison.Ordinal)
                => "Выбранный контекст ведущего принадлежит другому каналу YouTube.",
            not null when !string.Equals(broadcasterAssignment.Account.AccountId, broadcasterAccount.ChannelId, StringComparison.Ordinal)
                => "Авторизация ведущего изменилась – требуется повторная проверка доступа.",
            _ => null,
        };
        var broadcasterMismatch = broadcasterMismatchReason is not null;
        var broadcasterUnconfirmed = string.IsNullOrWhiteSpace(broadcasterAccount.ChannelId)
            && !(broadcasterAssignment is { IsVerified: true }
                && string.Equals(broadcasterAssignment.Account.AccountId, channel.ChannelId, StringComparison.Ordinal));

        var botAssignment = channel.GetAccountAssignment(PlatformAuthRole.Bot);
        var botContextId = AuthContextIds.Normalize(botAssignment?.ContextId ?? channel.BotContextId);
        var botToken = await GetTokenAsync(PlatformAuthRole.Bot, botContextId, cancellationToken).ConfigureAwait(false);
        var botAccount = accountsStore.Load(PlatformAuthRole.Bot, botContextId);
        var botMismatch = botAssignment switch
        {
            { IsVerified: false } => "Доступ аккаунта бота к каналу YouTube не подтверждён.",
            not null when !string.Equals(botAssignment.Account.AccountId, botAccount.ChannelId, StringComparison.Ordinal)
                => "Авторизация бота изменилась – требуется повторная проверка доступа.",
            _ => null,
        };
        var hasBotToken = !string.IsNullOrWhiteSpace(botToken);
        var hasUsableBot = hasBotToken && botMismatch is null;
        var botChatReason = !hasBotToken
            ? "Нет авторизации бота для отправки в чат YouTube."
            : botMismatch
            ?? (botAssignment is null
                ? "Подтвердите доступ аккаунта бота к каналу YouTube в настройках площадки."
                : null);
        var hasVerifiedBotChat = hasUsableBot && botAssignment is { IsVerified: true };
        var canUseBroadcaster = !string.IsNullOrWhiteSpace(broadcasterToken)
            && !broadcasterMismatch
            && !(broadcasterUnconfirmed && hasUsableBot && publicDiscovery is not null);

        if (canUseBroadcaster)
        {
            try
            {
                return await ProbeWithBroadcasterAsync(
                    channel.ChannelId,
                    broadcasterContextId,
                    hasVerifiedBotChat,
                    botChatReason,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized)
            {
                if (!hasUsableBot || publicDiscovery is null)
                {
                    logger.LogWarning(ex, "Ошибка проверки эфира YouTube для {ChannelId}", channel.ChannelId);
                    return new(false, false, null, FailureReason: StreamingErrorMessages.SafeMessage(ex));
                }

                logger.LogDebug(ex, "Токен ведущего YouTube вернул 401 при проверке эфира {ChannelId}, переключаемся на бота", channel.ChannelId);
            }
        }

        if (broadcasterAssignment is not null && !string.IsNullOrWhiteSpace(broadcasterToken) && broadcasterMismatch && !hasUsableBot)
        {
            return new(false, false, null, FailureReason: broadcasterMismatchReason);
        }

        if (!hasBotToken)
        {
            if (botAssignment is not null && broadcasterAssignment is null)
            {
                return new(false, false, null, FailureReason: "Нет авторизации бота для канала YouTube.");
            }

            if (!string.IsNullOrWhiteSpace(broadcasterToken) && broadcasterMismatch)
            {
                return new(false, false, null, FailureReason: broadcasterMismatchReason);
            }

            return new(false, false, null, FailureReason: "Нет авторизации ведущего или бота для канала YouTube.");
        }

        if (botMismatch is not null)
        {
            return new(false, false, null, FailureReason: botMismatch);
        }

        if (publicDiscovery is null)
        {
            return new(false, false, null, FailureReason: "Публичный поиск активного эфира YouTube недоступен.");
        }

        try
        {
            var discovered = await publicDiscovery.FindActiveLiveVideoAsync(channel.ChannelId, cancellationToken).ConfigureAwait(false);
            if (discovered is null)
            {
                return new(false, hasVerifiedBotChat, null, FailureReason: botChatReason ?? "Активная трансляция на канале YouTube не найдена.");
            }

            var sample = await broadcastClient
                .GetVideoLiveSampleAsync(discovered.VideoId, PlatformAuthRole.Bot, botContextId, cancellationToken)
                .ConfigureAwait(false);
            if (sample is null
                || !sample.ActualStartTimeUtc.HasValue
                || sample.ActualEndTimeUtc is not null
                || string.IsNullOrWhiteSpace(sample.ChannelId)
                || !string.Equals(sample.ChannelId, channel.ChannelId, StringComparison.Ordinal))
            {
                return new(false, hasVerifiedBotChat, null, FailureReason: botChatReason ?? "Активная трансляция на канале YouTube не найдена.");
            }

            var stream = new StreamInfo
            {
                Id = discovered.VideoId,
                UserId = channel.ChannelId,
                UserLogin = channel.ChannelId,
                UserName = !string.IsNullOrWhiteSpace(sample.ChannelTitle) ? sample.ChannelTitle : discovered.ChannelTitle,
                Title = !string.IsNullOrWhiteSpace(sample.Title) ? sample.Title : discovered.Title,
                ViewerCount = sample.ConcurrentViewers ?? 0,
                StartedAt = (sample.ActualStartTimeUtc ?? discovered.StartedAtUtc ?? DateTimeOffset.UtcNow).UtcDateTime,
            };

            var liveChatId = string.IsNullOrWhiteSpace(sample.ActiveLiveChatId) ? null : sample.ActiveLiveChatId.Trim();
            if (!hasVerifiedBotChat)
            {
                return new(true, false, stream, liveChatId, botChatReason);
            }

            if (string.IsNullOrWhiteSpace(liveChatId))
            {
                return new(true, false, stream, null, "Чат трансляции YouTube недоступен или отключён.");
            }

            return new(true, true, stream, liveChatId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ошибка проверки эфира YouTube для {ChannelId}", channel.ChannelId);
            return new(false, false, null, FailureReason: StreamingErrorMessages.SafeMessage(ex));
        }
    }

    private async Task<ChannelLiveProbeResult> ProbeWithBroadcasterAsync(
        string channelId,
        string broadcasterContextId,
        bool hasVerifiedBotChat,
        string? botChatReason,
        CancellationToken cancellationToken)
    {
        try
        {
            var broadcasts = await broadcastClient.ListActiveBroadcastsAsync(broadcasterContextId, cancellationToken).ConfigureAwait(false);
            var matches = broadcasts
                .Where(b => string.Equals(b.ChannelId, channelId, StringComparison.Ordinal))
                .ToArray();

            if (matches.Length == 0)
            {
                return new(false, hasVerifiedBotChat, null, FailureReason: botChatReason ?? "Активная трансляция на канале YouTube не найдена.");
            }

            var selectedVideoId = selectionStore.GetSelectedVideoId(channelId);
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

            if (!hasVerifiedBotChat)
            {
                return new(true, false, stream, selected.LiveChatId, botChatReason ?? "Нет авторизации бота для отправки в чат YouTube.");
            }

            if (string.IsNullOrWhiteSpace(selected.LiveChatId))
            {
                return new(true, false, stream, null, "Чат трансляции YouTube недоступен или отключён.");
            }

            return new(true, true, stream, selected.LiveChatId);
        }
        catch (YouTubeApiException ex) when (ex.Kind == YouTubeApiErrorKind.Unauthorized)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ошибка проверки эфира YouTube для {ChannelId}", channelId);
            return new(false, false, null, FailureReason: StreamingErrorMessages.SafeMessage(ex));
        }
    }

    private async Task<IReadOnlyList<(PlatformAuthRole Role, string ContextId)>> CollectHandleResolutionContextsAsync(
        string normalizedPreferredContextId,
        CancellationToken cancellationToken)
    {
        var candidates = new List<(PlatformAuthRole Role, string ContextId)>();
        var seen = new HashSet<(PlatformAuthRole Role, string ContextId)>();

        foreach (var role in new[] { PlatformAuthRole.Bot, PlatformAuthRole.Broadcaster })
        {
            await TryAddCandidateAsync(role, normalizedPreferredContextId).ConfigureAwait(false);

            foreach (var candidateContext in accountsStore.ListContexts(role))
            {
                await TryAddCandidateAsync(role, AuthContextIds.Normalize(candidateContext.ContextId)).ConfigureAwait(false);
            }
        }

        return candidates;

        async Task TryAddCandidateAsync(PlatformAuthRole role, string contextId)
        {
            if (!seen.Add((role, contextId)))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(await GetTokenAsync(role, contextId, cancellationToken).ConfigureAwait(false)))
            {
                candidates.Add((role, contextId));
            }
        }
    }

    private ValueTask<string?> GetTokenAsync(PlatformAuthRole role, string contextId, CancellationToken cancellationToken)
    {
        return AuthContextIds.IsDefault(contextId)
            ? authService.GetAccessTokenAsync(role, forceRefresh: false, cancellationToken)
            : authService.GetAccessTokenAsync(role, contextId, forceRefresh: false, cancellationToken);
    }
}
