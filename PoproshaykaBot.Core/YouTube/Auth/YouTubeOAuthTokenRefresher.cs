using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.YouTube.Auth;

public sealed class YouTubeOAuthTokenRefresher(
    YouTubeOAuthTokenClient tokenClient,
    YouTubeOAuthAccountWriter accountWriter,
    YouTubeOAuthStatusReporter statusReporter,
    YouTubeAccountsStore accountsStore,
    YouTubeSettingsStore settingsStore,
    YouTubeOAuthOptions options,
    ILogger<YouTubeOAuthTokenRefresher> logger) : IDisposable
{
    private static readonly TimeSpan BaseRetryDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _botRefreshLock = new(1, 1);
    private readonly SemaphoreSlim _broadcasterRefreshLock = new(1, 1);
    private readonly Lock _stateSync = new();
    private readonly Dictionary<YouTubeOAuthRole, RetryState> _retryStates = new();

    public async Task<string?> GetAccessTokenAsync(YouTubeOAuthRole role, CancellationToken ct = default)
    {
        var account = accountsStore.Load(role);
        if (HasUsableAccessToken(account))
        {
            return account.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            return null;
        }

        var refreshLock = GetLockForRole(role);
        await refreshLock.WaitAsync(ct);

        try
        {
            account = accountsStore.Load(role);
            if (HasUsableAccessToken(account))
            {
                return account.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(account.RefreshToken) || IsInBackoffOrCooldown(role))
            {
                return null;
            }

            var clientId = settingsStore.Load().ClientId;
            var clientSecret = accountsStore.LoadClientSecret();

            if (string.IsNullOrWhiteSpace(clientId))
            {
                return null;
            }

            try
            {
                return await ExecuteRefreshAsync(role, clientId, clientSecret, account.RefreshToken, ct);
            }
            catch (YouTubeOAuthException exception) when (exception.IsPermanentRejection)
            {
                return null;
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or YouTubeOAuthException)
            {
                if (ct.IsCancellationRequested)
                {
                    throw;
                }

                logger.LogWarning(exception, "Временный сбой обновления YouTube access-токена роли {Role}", role);
                return null;
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public async Task<string> RefreshTokenAsync(
        YouTubeOAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var refreshLock = GetLockForRole(role);
        await refreshLock.WaitAsync(ct);

        try
        {
            return await ExecuteRefreshAsync(role, clientId, clientSecret, refreshToken, ct);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void ResetRetryState(YouTubeOAuthRole role)
    {
        lock (_stateSync)
        {
            _retryStates.Remove(role);
        }
    }

    public void Dispose()
    {
        _botRefreshLock.Dispose();
        _broadcasterRefreshLock.Dispose();
    }

    private async Task<string> ExecuteRefreshAsync(
        YouTubeOAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct)
    {
        try
        {
            var token = await tokenClient.RefreshAsync(clientId, clientSecret, refreshToken, ct);

            accountWriter.ApplyRefreshResult(
                role,
                token.AccessToken,
                token.RefreshToken,
                token.Scopes,
                token.ExpiresInSeconds);

            ResetRetryState(role);
            return token.AccessToken;
        }
        catch (YouTubeOAuthException exception) when (exception.IsPermanentRejection)
        {
            RecordRejectionCooldown(role);

            if (string.Equals(exception.ErrorCode, "invalid_grant", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    exception,
                    "Google отозвал refresh-токен YouTube роли {Role} (invalid_grant) – сохранённая авторизация очищается.",
                    role);

                accountWriter.ClearAccount(role, publishAuthorizationRefreshed: true);
            }
            else
            {
                statusReporter.Report(role, exception.Message);
            }

            throw;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            RecordTransientFailure(role);
            throw;
        }
    }

    private bool HasUsableAccessToken(YouTubeAccountSettings account)
    {
        if (string.IsNullOrWhiteSpace(account.AccessToken))
        {
            return false;
        }

        if (account.AccessTokenExpiresAt is not { } expiresAt)
        {
            return true;
        }

        return expiresAt - options.TokenRefreshMargin > options.TimeProvider.GetUtcNow();
    }

    private bool IsInBackoffOrCooldown(YouTubeOAuthRole role)
    {
        var now = options.TimeProvider.GetUtcNow();
        lock (_stateSync)
        {
            return _retryStates.TryGetValue(role, out var state) && state.BlockedUntilUtc > now;
        }
    }

    private void RecordTransientFailure(YouTubeOAuthRole role)
    {
        var now = options.TimeProvider.GetUtcNow();
        lock (_stateSync)
        {
            var failures = _retryStates.TryGetValue(role, out var state) ? state.Failures + 1 : 1;
            var shift = Math.Min(failures - 1, 10);
            var delaySeconds = Math.Min(BaseRetryDelay.TotalSeconds * (1 << shift), MaxRetryDelay.TotalSeconds);
            _retryStates[role] = new(failures, now.AddSeconds(delaySeconds));
        }
    }

    private void RecordRejectionCooldown(YouTubeOAuthRole role)
    {
        var now = options.TimeProvider.GetUtcNow();
        lock (_stateSync)
        {
            var failures = _retryStates.TryGetValue(role, out var state) ? state.Failures + 1 : 1;
            _retryStates[role] = new(failures, now.Add(options.CredentialsRejectedCooldown));
        }
    }

    private SemaphoreSlim GetLockForRole(YouTubeOAuthRole role)
    {
        return role switch
        {
            YouTubeOAuthRole.Bot => _botRefreshLock,
            YouTubeOAuthRole.Broadcaster => _broadcasterRefreshLock,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private readonly record struct RetryState(int Failures, DateTimeOffset BlockedUntilUtc);
}
