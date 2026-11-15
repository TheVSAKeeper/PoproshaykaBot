using PoproshaykaBot.Core.Infrastructure.Events;
using System.Security.Cryptography;
using System.Text;

namespace PoproshaykaBot.Core.Platforms.Auth;

public sealed class OAuthRoleTokenRefresher<TAccount> : IDisposable
    where TAccount : class, IRoleAccountSettings
{
    private readonly Func<PlatformAuthRole, TAccount> _loadAccount;
    private readonly IOAuthTokenRefreshStrategy<TAccount> _strategy;
    private readonly TimeProvider _timeProvider;
    private readonly IDisposable _refreshEventSubscription;
    private readonly SemaphoreSlim _botRefreshLock = new(1, 1);
    private readonly SemaphoreSlim _broadcasterRefreshLock = new(1, 1);
    private readonly Lock _stateSync = new();
    private readonly Dictionary<PlatformAuthRole, RetryState> _retryStates = new();
    private bool _disposed;

    public OAuthRoleTokenRefresher(
        Func<PlatformAuthRole, TAccount> loadAccount,
        IOAuthTokenRefreshStrategy<TAccount> strategy,
        IEventBus eventBus,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(loadAccount);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _loadAccount = loadAccount;
        _strategy = strategy;
        _timeProvider = timeProvider;

        _refreshEventSubscription = eventBus.Subscribe<PlatformAuthorizationRefreshed>(@event =>
        {
            if (@event.Platform == _strategy.Platform
                && @event.Role is PlatformAuthRole.Bot or PlatformAuthRole.Broadcaster)
            {
                ResetRetryState(@event.Role);
            }
        });
    }

    public bool HasUsableAccessToken(PlatformAuthRole role)
    {
        PlatformAuthRoles.EnsureActive(role);
        return HasUsableAccessToken(role, _loadAccount(role));
    }

    public async ValueTask<string?> GetAccessTokenAsync(
        PlatformAuthRole role,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        PlatformAuthRoles.EnsureActive(role);

        var account = _loadAccount(role);

        if (HasScopeMismatch(role, account))
        {
            await _strategy.HandleScopeMismatchAsync(role, account, ct);
            return null;
        }

        if (!forceRefresh && HasUsableAccessToken(role, account))
        {
            return account.AccessToken;
        }

        if (string.IsNullOrWhiteSpace(account.RefreshToken))
        {
            if (!string.IsNullOrWhiteSpace(account.AccessToken))
            {
                _strategy.OnMissingRefreshToken(role);
            }

            return null;
        }

        var preLockAccessToken = account.AccessToken;
        var refreshLock = GetLockForRole(role);
        await refreshLock.WaitAsync(ct);

        try
        {
            account = _loadAccount(role);

            if (HasScopeMismatch(role, account))
            {
                await _strategy.HandleScopeMismatchAsync(role, account, ct);
                return null;
            }

            if (!forceRefresh && HasUsableAccessToken(role, account))
            {
                return account.AccessToken;
            }

            if (!forceRefresh
                && !string.IsNullOrWhiteSpace(account.AccessToken)
                && !string.Equals(account.AccessToken, preLockAccessToken, StringComparison.Ordinal))
            {
                _strategy.OnTokenReplacedWhileWaiting(role);
                return account.AccessToken;
            }

            if (string.IsNullOrWhiteSpace(account.RefreshToken))
            {
                _strategy.OnMissingRefreshToken(role);
                return null;
            }

            if (!_strategy.TryReadBackgroundCredentials(out var clientId, out var clientSecret))
            {
                return null;
            }

            var fingerprint = ComputeCredentialFingerprint(clientId, clientSecret, account.RefreshToken, account.AccessToken);
            if (IsInBackoffOrCooldown(role, fingerprint))
            {
                return null;
            }

            try
            {
                return await ExecuteRefreshAsync(role, clientId, clientSecret, account.RefreshToken, fingerprint, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (_strategy.ClassifyFailure(exception) != OAuthRefreshFailureKind.None)
            {
                return null;
            }
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public async Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        PlatformAuthRoles.EnsureActive(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var refreshLock = GetLockForRole(role);
        await refreshLock.WaitAsync(ct);

        try
        {
            var currentAccount = _loadAccount(role);
            var fingerprint = ComputeCredentialFingerprint(clientId, clientSecret, refreshToken, currentAccount.AccessToken);
            return await ExecuteRefreshAsync(role, clientId, clientSecret, refreshToken, fingerprint, ct);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void ResetRetryState(PlatformAuthRole role)
    {
        PlatformAuthRoles.EnsureActive(role);

        lock (_stateSync)
        {
            _retryStates.Remove(role);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshEventSubscription.Dispose();
        _botRefreshLock.Dispose();
        _broadcasterRefreshLock.Dispose();
    }

    private bool HasScopeMismatch(PlatformAuthRole role, TAccount account)
    {
        return account.StoredScopes.Length > 0 && !_strategy.AreStoredScopesValid(role, account);
    }

    private bool HasUsableAccessToken(PlatformAuthRole role, TAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.AccessToken))
        {
            return false;
        }

        if (HasScopeMismatch(role, account))
        {
            return false;
        }

        if (account.AccessTokenExpiresAt is not { } expiresAt)
        {
            return !_strategy.RefreshWhenExpirationMissing;
        }

        return expiresAt - _strategy.TokenRefreshMargin > _timeProvider.GetUtcNow();
    }

    private async Task<string> ExecuteRefreshAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        string fingerprint,
        CancellationToken ct)
    {
        try
        {
            var accessToken = await _strategy.RefreshAndSaveAsync(role, clientId, clientSecret, refreshToken, ct);
            ResetRetryState(role);
            return accessToken;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var kind = _strategy.ClassifyFailure(exception);
            switch (kind)
            {
                case OAuthRefreshFailureKind.RefreshTokenRejected:
                    await _strategy.OnRefreshTokenRejectedAsync(role, exception, ct);
                    ResetRetryState(role);
                    break;
                case OAuthRefreshFailureKind.CredentialsRejected:
                    var pausedUntil = RecordFailure(role, fingerprint, _strategy.CredentialsRejectedCooldown, fixedDelay: true);
                    _strategy.OnCredentialsRejected(role, exception, pausedUntil);
                    break;
                case OAuthRefreshFailureKind.Transient:
                    var nextAttempt = RecordFailure(role, fingerprint, _strategy.BaseTransientRetryDelay, fixedDelay: false);
                    _strategy.OnTransientFailure(role, exception, nextAttempt);
                    break;
            }

            throw;
        }
    }

    private bool IsInBackoffOrCooldown(PlatformAuthRole role, string fingerprint)
    {
        var now = _timeProvider.GetUtcNow();
        RetryState? deferredState = null;

        lock (_stateSync)
        {
            if (!_retryStates.TryGetValue(role, out var state))
            {
                return false;
            }

            if (!string.Equals(state.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                _retryStates.Remove(role);
                return false;
            }

            if (state.BlockedUntilUtc > now)
            {
                deferredState = state;
            }
        }

        if (deferredState is { } deferred)
        {
            _strategy.OnRetryDeferred(role, deferred.BlockedUntilUtc, deferred.Failures);
            return true;
        }

        return false;
    }

    private DateTimeOffset RecordFailure(PlatformAuthRole role, string fingerprint, TimeSpan delay, bool fixedDelay)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_stateSync)
        {
            var failures = _retryStates.TryGetValue(role, out var state)
                && string.Equals(state.Fingerprint, fingerprint, StringComparison.Ordinal)
                ? state.Failures + 1
                : 1;

            var effectiveDelay = fixedDelay
                ? delay
                : CalculateExponentialDelay(failures);

            var blockedUntil = now + effectiveDelay;
            _retryStates[role] = new(failures, blockedUntil, fingerprint);
            return blockedUntil;
        }
    }

    private TimeSpan CalculateExponentialDelay(int failures)
    {
        var shift = Math.Min(failures - 1, 10);
        var delaySeconds = _strategy.BaseTransientRetryDelay.TotalSeconds * (1 << shift);
        var clampedSeconds = Math.Min(delaySeconds, _strategy.MaxTransientRetryDelay.TotalSeconds);
        return TimeSpan.FromSeconds(clampedSeconds);
    }

    private SemaphoreSlim GetLockForRole(PlatformAuthRole role)
    {
        return role switch
        {
            PlatformAuthRole.Bot => _botRefreshLock,
            PlatformAuthRole.Broadcaster => _broadcasterRefreshLock,
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
        };
    }

    private static string ComputeCredentialFingerprint(
        string clientId,
        string clientSecret,
        string refreshToken,
        string accessToken)
    {
        var raw = string.Concat(clientId, "\0", clientSecret, "\0", refreshToken, "\0", accessToken);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private readonly record struct RetryState(int Failures, DateTimeOffset BlockedUntilUtc, string Fingerprint);
}
