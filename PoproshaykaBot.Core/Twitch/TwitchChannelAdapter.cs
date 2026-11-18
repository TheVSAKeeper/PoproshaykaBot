using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Twitch;

internal sealed class TwitchChannelAdapter(
    [FromKeyedServices(TwitchEndpoints.HelixBotClient)] ITwitchHelixClient botHelix,
    [FromKeyedServices(TwitchEndpoints.HelixBroadcasterClient)] ITwitchHelixClient broadcasterHelix,
    AccountsStore accountsStore,
    [FromKeyedServices(StreamingPlatform.Twitch)] IPlatformAuthService authService,
    ILogger<TwitchChannelAdapter> logger) : IPlatformChannelAdapter
{
    public StreamingPlatform Platform => StreamingPlatform.Twitch;

    public PlatformCapabilities Capabilities =>
        PlatformCapabilities.Reply
        | PlatformCapabilities.Whisper
        | PlatformCapabilities.NativePolls
        | PlatformCapabilities.ChannelMetadataEdit
        | PlatformCapabilities.EmotePositions
        | PlatformCapabilities.BackgroundStreamMonitoring
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

        var login = NormalizeTwitchLogin(rawInput);
        if (string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        var contextId = AuthContextIds.Normalize(preferredContextId);
        using var _ = PlatformAuthRequestOptions.UseContext(contextId);

        UserInfo? user = null;
        if (authService.HasUsableAccessToken(PlatformAuthRole.Bot, contextId))
        {
            user = await botHelix.GetUserByLoginAsync(login, cancellationToken).ConfigureAwait(false);
        }
        else if (authService.HasUsableAccessToken(PlatformAuthRole.Broadcaster, contextId))
        {
            user = await broadcasterHelix.GetUserByLoginAsync(login, cancellationToken).ConfigureAwait(false);
        }

        if (user is null)
        {
            return null;
        }

        return new(
            new ChannelKey(StreamingPlatform.Twitch, user.Id),
            string.IsNullOrWhiteSpace(user.DisplayName) ? user.Login : user.DisplayName,
            user.Login,
            $"https://twitch.tv/{user.Login}");
    }

    public async Task<ChannelAccessVerification> VerifyChannelAccessAsync(
        ChannelKey channel,
        PlatformAuthRole role,
        string contextId,
        CancellationToken cancellationToken)
    {
        if (channel.Platform != StreamingPlatform.Twitch || string.IsNullOrWhiteSpace(channel.ChannelId))
        {
            return new(false, null, "Некорректный идентификатор канала Twitch.");
        }

        var normalizedContext = AuthContextIds.Normalize(contextId);
        if (!accountsStore.TryLoadContext(role, normalizedContext, out var account) || account is null)
        {
            return new(false, null, $"Контекст авторизации «{normalizedContext}» не найден.");
        }

        var token = await authService.GetAccessTokenAsync(role, normalizedContext, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new(false, null, $"Токен роли {PlatformAuthRoles.Describe(role)} отсутствует или просрочен.");
        }

        account = accountsStore.Load(role, normalizedContext);
        var requiredScopes = role == PlatformAuthRole.Bot
            ? TwitchScopes.BotRequired
            : TwitchScopes.BroadcasterRequired;
        var grantedScopes = account.StoredScopes.Length > 0 ? account.StoredScopes : account.Scopes;

        if (!requiredScopes.All(req => grantedScopes.Contains(req, StringComparer.OrdinalIgnoreCase)))
        {
            return new(false, account.UserId, $"Недостаточно прав (scopes) для роли {PlatformAuthRoles.Describe(role)}.");
        }

        if (role == PlatformAuthRole.Broadcaster)
        {
            if (string.IsNullOrWhiteSpace(account.UserId))
            {
                return new(false, null, "У аккаунта стримера не определён user_id.");
            }

            if (!string.Equals(account.UserId, channel.ChannelId, StringComparison.Ordinal))
            {
                return new(false, account.UserId, "Авторизованный аккаунт стримера принадлежит другому каналу Twitch.");
            }

            return new(true, account.UserId);
        }

        using var _ = PlatformAuthRequestOptions.UseContext(normalizedContext);
        var users = await botHelix.GetUsersByIdsAsync([channel.ChannelId], cancellationToken).ConfigureAwait(false);
        if (users.Count == 0)
        {
            return new(false, account.UserId, "Целевой канал Twitch не найден через API.");
        }

        return new(true, string.IsNullOrWhiteSpace(account.UserId) ? null : account.UserId);
    }

    public async Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(
        ChannelCatalogEntry channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        if (string.IsNullOrWhiteSpace(channel.ChannelId))
        {
            return new(false, false, null, FailureReason: "Не задан идентификатор канала Twitch.");
        }

        var botContextId = AuthContextIds.Normalize(channel.BotContextId);
        var botToken = await authService.GetAccessTokenAsync(PlatformAuthRole.Bot, botContextId, forceRefresh: false, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return new(false, false, null, FailureReason: "Нет действительной авторизации бота для канала Twitch.");
        }

        try
        {
            using var _ = PlatformAuthRequestOptions.UseContext(botContextId);
            var stream = await botHelix.GetStreamAsync(channel.ChannelId, cancellationToken).ConfigureAwait(false);
            if (stream is null)
            {
                return new(false, true, null, FailureReason: "Трансляция на канале сейчас не запущена.");
            }

            return new(true, true, StreamInfoMapper.MapFromHelix(stream), channel.ChannelId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Ошибка проверки эфира Twitch для {ChannelId}", channel.ChannelId);
            return new(false, false, null, FailureReason: StreamingErrorMessages.SafeMessage(ex));
        }
    }

    private static string NormalizeTwitchLogin(string rawInput)
    {
        var trimmed = rawInput.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            && uri.Host.Contains("twitch.tv", StringComparison.OrdinalIgnoreCase))
        {
            var segment = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return segment?.TrimStart('@').ToLowerInvariant() ?? string.Empty;
        }

        return trimmed.TrimStart('@').ToLowerInvariant();
    }
}
