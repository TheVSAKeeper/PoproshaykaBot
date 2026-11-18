using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed class OAuthTokenRefresher : IDisposable
{
    private readonly OAuthRoleTokenRefresher<TwitchAccountSettings> _inner;

    public OAuthTokenRefresher(
        OAuthTokenClient tokenClient,
        OAuthAccountWriter accountWriter,
        OAuthStatusReporter statusReporter,
        AccountsStore accountsStore,
        SettingsManager settingsManager,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<OAuthTokenRefresher> logger)
    {
        ArgumentNullException.ThrowIfNull(tokenClient);
        ArgumentNullException.ThrowIfNull(accountWriter);
        ArgumentNullException.ThrowIfNull(statusReporter);
        ArgumentNullException.ThrowIfNull(accountsStore);
        ArgumentNullException.ThrowIfNull(settingsManager);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var strategy = new TwitchRefreshStrategy(
            tokenClient,
            accountWriter,
            statusReporter,
            accountsStore,
            settingsManager,
            eventBus,
            logger);

        _inner = new((role, contextId) => accountsStore.Load(role, contextId), strategy, eventBus, timeProvider);
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
        return RefreshTokenAsync(role, null, clientId, clientSecret, refreshToken, ct);
    }

    public Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string? contextId,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Refresh token не может быть пустым", nameof(refreshToken));
        }

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

    private sealed class TwitchRefreshStrategy(
        OAuthTokenClient tokenClient,
        OAuthAccountWriter accountWriter,
        OAuthStatusReporter statusReporter,
        AccountsStore accountsStore,
        SettingsManager settingsManager,
        IEventBus eventBus,
        ILogger<OAuthTokenRefresher> logger)
        : IOAuthTokenRefreshStrategy<TwitchAccountSettings>
    {
        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

        public StreamingPlatform Platform => StreamingPlatform.Twitch;

        public TimeSpan TokenRefreshMargin => RefreshMargin;

        public TimeSpan BaseTransientRetryDelay => BaseRetryDelay;

        public TimeSpan MaxTransientRetryDelay => MaxRetryDelay;

        public TimeSpan CredentialsRejectedCooldown => MaxRetryDelay;

        public bool RefreshWhenExpirationMissing => true;

        public bool AreStoredScopesValid(PlatformAuthRole role, TwitchAccountSettings account)
        {
            return TwitchScopes.SetEquals(account.StoredScopes, account.Scopes);
        }

        public Task HandleScopeMismatchAsync(PlatformAuthRole role, TwitchAccountSettings accountSnapshot, CancellationToken ct)
        {
            return HandleScopeMismatchAsync(role, AuthContextIds.Default, accountSnapshot, ct);
        }

        public async Task HandleScopeMismatchAsync(PlatformAuthRole role, string contextId, TwitchAccountSettings accountSnapshot, CancellationToken ct)
        {
            logger.LogWarning("Scope set роли {Role} (контекст {ContextId}) изменился – требуется повторная авторизация. Старые: [{Old}]. Новые: [{New}]",
                role,
                contextId,
                string.Join(" ", accountSnapshot.StoredScopes),
                string.Join(" ", accountSnapshot.Scopes));

            var changeMsg = $"Требуется повторная авторизация Twitch ({PlatformAuthRoles.Describe(role)}): изменился набор прав.";
            statusReporter.Report(role, changeMsg);

            try
            {
                await eventBus.PublishAsync(new BotConnectionStatusUpdated(changeMsg), ct);
            }
            catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Публикация BotConnectionStatusUpdated (scope mismatch, роль {Role}) отменена", role);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Не удалось опубликовать BotConnectionStatusUpdated при scope mismatch (роль {Role})", role);
            }

            accountsStore.Mutate(role, contextId, OAuthRoleHelpers.ClearAccountInPlace);
            logger.LogInformation("Учётные данные роли {Role} (контекст {ContextId}) очищены из-за расхождения scope – ожидается повторная авторизация", role, contextId);
        }

        public bool TryReadBackgroundCredentials(out string clientId, out string clientSecret)
        {
            var settings = settingsManager.Current.Twitch;
            clientId = settings.ClientId;
            clientSecret = settings.ClientSecret;
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
            statusReporter.Report(role, "Обновление токена доступа...");

            var formData = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "refresh_token", refreshToken },
                { "grant_type", "refresh_token" },
            };

            var tokenResponse = await tokenClient.PostTokenRequestAsync(formData, ct);
            var validation = await tokenClient.ValidateAsync(tokenResponse.AccessToken, ct);

            if (validation == null)
            {
                throw new InvalidOperationException("Twitch вернул токен, но Validate-эндпойнт его не подтвердил.");
            }

            OAuthRoleHelpers.VerifyLoginMatchesRole(
                role,
                validation,
                settingsManager,
                logger,
                checkBroadcasterChannel: AuthContextIds.IsDefault(contextId));

            accountWriter.UpdateSettings(
                role,
                contextId,
                tokenResponse.AccessToken,
                tokenResponse.RefreshToken,
                tokenResponse.Scope,
                validation.Login,
                validation.UserId,
                tokenResponse.ExpiresIn);

            logger.LogInformation("Refresh для роли {Role} (контекст {ContextId}) выполнен успешно (login={Login}, expiresIn={ExpiresIn}s, scopes={ScopeCount})",
                role,
                contextId,
                validation.Login,
                tokenResponse.ExpiresIn,
                tokenResponse.Scope?.Count ?? 0);

            statusReporter.Report(role, "Токен доступа обновлён успешно!");
            return tokenResponse.AccessToken;
        }

        public OAuthRefreshFailureKind ClassifyFailure(Exception exception)
        {
            return exception switch
            {
                OAuthRefreshRejectedException => OAuthRefreshFailureKind.RefreshTokenRejected,
                OAuthTokenRequestException { IsCredentialsRejected: true } => OAuthRefreshFailureKind.CredentialsRejected,
                _ => OAuthRefreshFailureKind.Transient,
            };
        }

        public Task OnRefreshTokenRejectedAsync(PlatformAuthRole role, Exception exception, CancellationToken ct)
        {
            return OnRefreshTokenRejectedAsync(role, AuthContextIds.Default, exception, ct);
        }

        public Task OnRefreshTokenRejectedAsync(PlatformAuthRole role, string contextId, Exception exception, CancellationToken ct)
        {
            var rejected = (OAuthRefreshRejectedException)exception;
            logger.LogWarning("Refresh-токен роли {Role} (контекст {ContextId}) отвергнут Twitch (HTTP {Status}, code={ErrorCode}) – токены сброшены, требуется повторная авторизация",
                role,
                contextId,
                rejected.HttpStatus,
                rejected.ErrorCode ?? "–");

            var msg = $"Требуется повторная авторизация Twitch ({PlatformAuthRoles.Describe(role)}): refresh-токен отвергнут сервером.";
            statusReporter.Report(role, msg);
            _ = eventBus.PublishAsync(new BotConnectionStatusUpdated(msg), ct);

            accountsStore.Mutate(role, contextId, OAuthRoleHelpers.ClearAccountInPlace);
            return Task.CompletedTask;
        }

        public void OnCredentialsRejected(PlatformAuthRole role, Exception exception, DateTimeOffset pausedUntil)
        {
            var credentialsRejected = (OAuthTokenRequestException)exception;
            logger.LogError(credentialsRejected, "Twitch отклонил учётные данные приложения при обновлении токена роли {Role} (HTTP {Status}) – обновление приостановлено до {NextAttempt:HH:mm:ss} UTC, проверьте Client ID и Client Secret",
                role,
                credentialsRejected.HttpStatus,
                pausedUntil);

            statusReporter.Report(role,
                $"Twitch отклонил Client ID или Client Secret ({PlatformAuthRoles.Describe(role)}). Проверьте их в настройках – обновление токена приостановлено.");
        }

        public void OnTransientFailure(PlatformAuthRole role, Exception exception, DateTimeOffset nextAttemptAt)
        {
            logger.LogError(exception, "Сбой при обновлении токена роли {Role} (transient – refresh-токен сохранён), следующая попытка не раньше {NextAttempt:HH:mm:ss} UTC",
                role,
                nextAttemptAt);
        }

        public void OnMissingRefreshToken(PlatformAuthRole role)
        {
            logger.LogWarning("Refresh-токен для роли {Role} отсутствует – обновление невозможно, требуется ручная авторизация", role);
        }

        public void OnTokenReplacedWhileWaiting(PlatformAuthRole role)
        {
            logger.LogDebug("Refresh для роли {Role} пропущен: токен в хранилище уже сменился между чтением и захватом семафора", role);
        }

        public void OnRetryDeferred(PlatformAuthRole role, DateTimeOffset nextAttemptAt, int consecutiveFailures)
        {
            logger.LogDebug("Refresh для роли {Role} отложен до {NextAttempt:HH:mm:ss} UTC (неудач подряд: {Failures})",
                role,
                nextAttemptAt,
                consecutiveFailures);
        }
    }
}
