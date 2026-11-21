using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthTokenRefresher : IDisposable
{
    private readonly OAuthRoleTokenRefresher<YouTubeAccountSettings> _inner;

    public YouTubeOAuthTokenRefresher(
        YouTubeOAuthTokenClient tokenClient,
        YouTubeOAuthAccountWriter accountWriter,
        YouTubeOAuthStatusReporter statusReporter,
        YouTubeAccountsStore accountsStore,
        YouTubeSettingsStore settingsStore,
        YouTubeOAuthOptions options,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<YouTubeOAuthTokenRefresher> logger)
    {
        ArgumentNullException.ThrowIfNull(tokenClient);
        ArgumentNullException.ThrowIfNull(accountWriter);
        ArgumentNullException.ThrowIfNull(statusReporter);
        ArgumentNullException.ThrowIfNull(accountsStore);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var strategy = new YouTubeRefreshStrategy(
            tokenClient,
            accountWriter,
            statusReporter,
            accountsStore,
            settingsStore,
            options,
            eventBus,
            logger);

        _inner = new(
            (role, contextId) => accountsStore.Load(role, contextId),
            strategy,
            eventBus,
            timeProvider,
            static account => account.ChannelId);
    }

    public bool HasUsableAccessToken(PlatformAuthRole role)
    {
        return _inner.HasUsableAccessToken(role);
    }

    public bool HasUsableAccessToken(PlatformAuthRole role, string? contextId)
    {
        return _inner.HasUsableAccessToken(role, contextId);
    }

    public ValueTask<string?> GetAccessTokenAsync(
        PlatformAuthRole role,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        return _inner.GetAccessTokenAsync(role, forceRefresh, ct);
    }

    public ValueTask<string?> GetAccessTokenAsync(
        PlatformAuthRole role,
        string? contextId,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        return _inner.GetAccessTokenAsync(role, contextId, forceRefresh, ct);
    }

    public Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return _inner.RefreshTokenAsync(role, clientId, clientSecret, refreshToken, ct);
    }

    public Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string? contextId,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return _inner.RefreshTokenAsync(role, contextId, clientId, clientSecret, refreshToken, ct);
    }

    public void ResetRetryState(PlatformAuthRole role)
    {
        _inner.ResetRetryState(role);
    }

    public void ResetRetryState(PlatformAuthRole role, string? contextId)
    {
        _inner.ResetRetryState(role, contextId);
    }

    public void Dispose()
    {
        _inner.Dispose();
    }

    private sealed class YouTubeRefreshStrategy(
        YouTubeOAuthTokenClient tokenClient,
        YouTubeOAuthAccountWriter accountWriter,
        YouTubeOAuthStatusReporter statusReporter,
        YouTubeAccountsStore accountsStore,
        YouTubeSettingsStore settingsStore,
        YouTubeOAuthOptions options,
        IEventBus eventBus,
        ILogger<YouTubeOAuthTokenRefresher> logger)
        : IOAuthTokenRefreshStrategy<YouTubeAccountSettings>
    {
        private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

        public StreamingPlatform Platform => StreamingPlatform.YouTube;

        public TimeSpan TokenRefreshMargin => options.TokenRefreshMargin;

        public TimeSpan BaseTransientRetryDelay => BaseRetryDelay;

        public TimeSpan MaxTransientRetryDelay => MaxRetryDelay;

        public TimeSpan CredentialsRejectedCooldown => options.CredentialsRejectedCooldown;

        public bool AreStoredScopesValid(PlatformAuthRole role, YouTubeAccountSettings account)
        {
            return YouTubeScopes.SetEquals(account.StoredScopes, account.Scopes)
                && YouTubeScopes.RequiredFor(role).All(required => YouTubeScopes.IsGranted(account, required));
        }

        public Task HandleScopeMismatchAsync(PlatformAuthRole role, YouTubeAccountSettings accountSnapshot, CancellationToken ct)
        {
            return HandleScopeMismatchAsync(role, AuthContextIds.Default, accountSnapshot, ct);
        }

        public async Task HandleScopeMismatchAsync(PlatformAuthRole role, string contextId, YouTubeAccountSettings accountSnapshot, CancellationToken ct)
        {
            logger.LogWarning(
                "Scope set YouTube роли {Role} (контекст {ContextId}) изменился или не содержит обязательных прав – требуется повторная авторизация. Выдано: [{Old}]. Ожидается: [{New}]",
                role,
                contextId,
                string.Join(" ", accountSnapshot.StoredScopes),
                string.Join(" ", accountSnapshot.Scopes));

            var message = $"Требуется повторная авторизация YouTube ({PlatformAuthRoles.Describe(role)}): изменился набор прав.";
            statusReporter.Report(role, message);

            try
            {
                await eventBus.PublishAsync(new BotConnectionStatusUpdated(message), ct);
            }
            catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Публикация BotConnectionStatusUpdated (YouTube scope mismatch, роль {Role}) отменена", role);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Не удалось опубликовать BotConnectionStatusUpdated при YouTube scope mismatch (роль {Role})", role);
            }

            accountWriter.ClearAccount(role, contextId, publishAuthorizationRefreshed: true);
        }

        public bool TryReadBackgroundCredentials(out string clientId, out string clientSecret)
        {
            clientId = settingsStore.Load().ClientId;
            if (string.IsNullOrWhiteSpace(clientId))
            {
                clientSecret = string.Empty;
                return false;
            }

            clientSecret = accountsStore.LoadClientSecret();
            return true;
        }

        public Task<string> RefreshAndSaveAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string refreshToken,
            CancellationToken ct)
        {
            return RefreshAndSaveAsync(role, AuthContextIds.Default, clientId, clientSecret, refreshToken, ct);
        }

        public async Task<string> RefreshAndSaveAsync(
            PlatformAuthRole role,
            string contextId,
            string clientId,
            string clientSecret,
            string refreshToken,
            CancellationToken ct)
        {
            var token = await tokenClient.RefreshAsync(clientId, clientSecret, refreshToken, ct);

            accountWriter.ApplyRefreshResult(
                role,
                contextId,
                token.AccessToken,
                token.RefreshToken,
                token.Scopes,
                token.ExpiresInSeconds,
                previousRefreshToken: refreshToken);

            return token.AccessToken;
        }

        public OAuthRefreshFailureKind ClassifyFailure(Exception exception)
        {
            return exception switch
            {
                YouTubeOAuthException { IsPermanentRejection: true, ErrorCode: var errorCode }
                    when string.Equals(errorCode, "invalid_grant", StringComparison.OrdinalIgnoreCase)
                    => OAuthRefreshFailureKind.RefreshTokenRejected,
                YouTubeOAuthException { IsPermanentRejection: true }
                    => OAuthRefreshFailureKind.CredentialsRejected,
                _ => OAuthRefreshFailureKind.Transient,
            };
        }

        public Task OnRefreshTokenRejectedAsync(PlatformAuthRole role, Exception exception, CancellationToken ct)
        {
            return OnRefreshTokenRejectedAsync(role, AuthContextIds.Default, exception, ct);
        }

        public Task OnRefreshTokenRejectedAsync(PlatformAuthRole role, string contextId, Exception exception, CancellationToken ct)
        {
            logger.LogWarning(
                exception,
                "Google отозвал refresh-токен YouTube роли {Role} (контекст {ContextId}, invalid_grant) – сохранённая авторизация очищается.",
                role,
                contextId);

            accountWriter.ClearAccount(role, contextId, publishAuthorizationRefreshed: true);
            return Task.CompletedTask;
        }

        public void OnCredentialsRejected(PlatformAuthRole role, Exception exception, DateTimeOffset pausedUntil)
        {
            logger.LogError(
                exception,
                "Google отклонил учётные данные приложения при обновлении YouTube токена роли {Role} – повтор приостановлен до {PausedUntil:HH:mm:ss} UTC",
                role,
                pausedUntil);

            statusReporter.Report(
                role,
                $"Требуется повторная авторизация YouTube ({PlatformAuthRoles.Describe(role)}): {YouTubeOAuthErrorFormatter.DescribeSafe(exception)}.");
        }

        public void OnTransientFailure(PlatformAuthRole role, Exception exception, DateTimeOffset nextAttemptAt)
        {
            logger.LogWarning(
                exception,
                "Временный сбой обновления YouTube access-токена роли {Role}, следующая попытка не раньше {NextAttempt:HH:mm:ss} UTC",
                role,
                nextAttemptAt);
        }
    }
}
