using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Chat;

namespace PoproshaykaBot.Core.Twitch.Auth;

public sealed class OAuthTokenRefresher(
    OAuthTokenClient tokenClient,
    OAuthAccountWriter accountWriter,
    OAuthStatusReporter statusReporter,
    AccountsStore accountsStore,
    SettingsManager settingsManager,
    IEventBus eventBus,
    ILogger<OAuthTokenRefresher> logger)
    : IDisposable
{
    private static readonly TimeSpan TokenRefreshSkew = TimeSpan.FromMinutes(5);

    private readonly Dictionary<TwitchOAuthRole, SemaphoreSlim> _refreshSemaphores = new()
    {
        [TwitchOAuthRole.Bot] = new(1, 1),
        [TwitchOAuthRole.Broadcaster] = new(1, 1),
    };

    private bool _isDisposed;

    public async Task<string?> GetAccessTokenAsync(TwitchOAuthRole role, CancellationToken ct = default)
    {
        var account = accountsStore.Load(role);

        if (string.IsNullOrWhiteSpace(account.AccessToken))
        {
            return null;
        }

        if (account.StoredScopes.Length > 0
            && !TwitchScopes.SetEquals(account.StoredScopes, account.Scopes))
        {
            await HandleScopeMismatchAsync(role, account, ct);
            return null;
        }

        if (account.AccessTokenExpiresAt is { } expiresAt
            && expiresAt - DateTimeOffset.UtcNow > TokenRefreshSkew)
        {
            return account.AccessToken;
        }

        return await RefreshAccessTokenAsync(role, account.AccessToken, ct);
    }

    public async Task<string> RefreshTokenAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new ArgumentException("Refresh token не может быть пустым", nameof(refreshToken));
        }

        logger.LogInformation("Ручное обновление токена доступа для роли {Role}", role);

        var semaphore = _refreshSemaphores[role];
        await semaphore.WaitAsync(ct);
        try
        {
            return await RefreshTokenInternalAsync(role, clientId, clientSecret, refreshToken, ct);
        }
        catch (OAuthRefreshRejectedException ex)
        {
            HandleRefreshRejected(role, ex, ct);
            throw;
        }
        finally
        {
            semaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        foreach (var semaphore in _refreshSemaphores.Values)
        {
            semaphore.Dispose();
        }

        _isDisposed = true;
    }

    private async Task<string?> RefreshAccessTokenAsync(
        TwitchOAuthRole role,
        string staleToken,
        CancellationToken ct)
    {
        var semaphore = _refreshSemaphores[role];
        await semaphore.WaitAsync(ct);
        try
        {
            var settings = settingsManager.Current.Twitch;
            var account = accountsStore.Load(role);

            if (!string.IsNullOrWhiteSpace(account.AccessToken)
                && !string.Equals(account.AccessToken, staleToken, StringComparison.Ordinal))
            {
                logger.LogDebug("Refresh для роли {Role} пропущен: токен в хранилище уже сменился между чтением и захватом семафора", role);
                return account.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(account.RefreshToken))
            {
                logger.LogWarning("Refresh-токен для роли {Role} отсутствует — обновление невозможно, требуется ручная авторизация", role);
                return null;
            }

            try
            {
                return await RefreshTokenInternalAsync(role, settings.ClientId, settings.ClientSecret, account.RefreshToken, ct);
            }
            catch (OAuthRefreshRejectedException ex)
            {
                HandleRefreshRejected(role, ex, ct);
                return null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Сбой при обновлении токена роли {Role} (transient — refresh-токен сохранён)", role);
                return null;
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<string> RefreshTokenInternalAsync(
        TwitchOAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct)
    {
        statusReporter.Report(role, "Обновление токена доступа...");

        var formData = new Dictionary<string, string>
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

        OAuthRoleHelpers.VerifyLoginMatchesRole(role, validation, settingsManager, logger);

        accountWriter.UpdateSettings(role,
            tokenResponse.AccessToken,
            tokenResponse.RefreshToken,
            tokenResponse.Scope,
            validation.Login,
            validation.UserId,
            tokenResponse.ExpiresIn);

        logger.LogInformation("Refresh для роли {Role} выполнен успешно (login={Login}, expiresIn={ExpiresIn}s, scopes={ScopeCount})",
            role,
            validation.Login,
            tokenResponse.ExpiresIn,
            tokenResponse.Scope?.Count ?? 0);

        statusReporter.Report(role, "Токен доступа обновлён успешно!");
        return tokenResponse.AccessToken;
    }

    private async Task HandleScopeMismatchAsync(TwitchOAuthRole role, TwitchAccountSettings accountSnapshot, CancellationToken ct)
    {
        logger.LogWarning("Scope set роли {Role} изменился — требуется повторная авторизация. Старые: [{Old}]. Новые: [{New}]",
            role,
            string.Join(" ", accountSnapshot.StoredScopes),
            string.Join(" ", accountSnapshot.Scopes));

        var changeMsg = $"Требуется повторная авторизация Twitch ({OAuthRoleHelpers.DescribeRole(role)}): изменился набор прав.";
        statusReporter.Report(role, changeMsg);

        try
        {
            await eventBus.PublishAsync(new BotConnectionStatusUpdated(changeMsg), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            logger.LogDebug("Публикация BotConnectionStatusUpdated (scope mismatch, роль {Role}) отменена", role);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось опубликовать BotConnectionStatusUpdated при scope mismatch (роль {Role})", role);
        }

        accountsStore.Mutate(role, OAuthRoleHelpers.ClearAccountInPlace);
        logger.LogInformation("Учётные данные роли {Role} очищены из-за расхождения scope — ожидается повторная авторизация", role);
    }

    private void HandleRefreshRejected(TwitchOAuthRole role, OAuthRefreshRejectedException exception, CancellationToken ct)
    {
        logger.LogWarning("Refresh-токен роли {Role} отвергнут Twitch (HTTP {Status}, code={ErrorCode}) — токены сброшены, требуется повторная авторизация",
            role,
            exception.HttpStatus,
            exception.ErrorCode ?? "—");

        var msg = $"Требуется повторная авторизация Twitch ({OAuthRoleHelpers.DescribeRole(role)}): refresh-токен отвергнут сервером.";
        statusReporter.Report(role, msg);
        _ = eventBus.PublishAsync(new BotConnectionStatusUpdated(msg), ct);

        accountsStore.Mutate(role, OAuthRoleHelpers.ClearAccountInPlace);
    }
}
