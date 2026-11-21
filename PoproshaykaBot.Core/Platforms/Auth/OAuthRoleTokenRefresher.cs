using System.Collections.Concurrent;
using PoproshaykaBot.Core.Infrastructure.Events;
using System.Security.Cryptography;
using System.Text;

namespace PoproshaykaBot.Core.Platforms.Auth;

public sealed class OAuthRoleTokenRefresher<TAccount> : IDisposable
    where TAccount : class, IRoleAccountSettings
{
    private readonly Func<PlatformAuthRole, string?, TAccount> _loadAccount;
    private readonly IOAuthTokenRefreshStrategy<TAccount> _strategy;
    private readonly TimeProvider _timeProvider;
    private readonly Func<TAccount, string?>? _getLockGroupKey;
    private readonly IDisposable _refreshEventSubscription;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _refreshLocks = new(StringComparer.Ordinal);
    private readonly Lock _stateSync = new();
    private readonly Dictionary<(PlatformAuthRole Role, string ContextId), RetryState> _retryStates = new();
    private bool _disposed;

    public OAuthRoleTokenRefresher(
        Func<PlatformAuthRole, TAccount> loadAccount,
        IOAuthTokenRefreshStrategy<TAccount> strategy,
        IEventBus eventBus,
        TimeProvider timeProvider)
        : this(
            (role, _) => loadAccount(role),
            strategy,
            eventBus,
            timeProvider)
    {
        ArgumentNullException.ThrowIfNull(loadAccount);
    }

    public OAuthRoleTokenRefresher(
        Func<PlatformAuthRole, string?, TAccount> loadAccount,
        IOAuthTokenRefreshStrategy<TAccount> strategy,
        IEventBus eventBus,
        TimeProvider timeProvider,
        Func<TAccount, string?>? getLockGroupKey = null)
    {
        ArgumentNullException.ThrowIfNull(loadAccount);
        ArgumentNullException.ThrowIfNull(strategy);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _loadAccount = loadAccount;
        _strategy = strategy;
        _timeProvider = timeProvider;
        _getLockGroupKey = getLockGroupKey;

        _refreshEventSubscription = eventBus.Subscribe<PlatformAuthorizationRefreshed>(@event =>
        {
            if (@event.Platform == _strategy.Platform
                && @event.Role is PlatformAuthRole.Bot or PlatformAuthRole.Broadcaster)
            {
                ResetRetryState(@event.Role, @event.ContextId);
            }
        });
    }

    public bool HasUsableAccessToken(PlatformAuthRole role)
    {
        return HasUsableAccessToken(role, (string?)null);
    }

    public bool HasUsableAccessToken(PlatformAuthRole role, string? contextId)
    {
        PlatformAuthRoles.EnsureActive(role);
        var normalized = AuthContextIds.Normalize(contextId);
        return HasUsableAccessToken(role, _loadAccount(role, normalized));
    }

    public ValueTask<string?> GetAccessTokenAsync(
        PlatformAuthRole role,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        return GetAccessTokenAsync(role, null, forceRefresh, ct);
    }

    public async ValueTask<string?> GetAccessTokenAsync(
        PlatformAuthRole role,
        string? contextId,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        PlatformAuthRoles.EnsureActive(role);
        var normalized = AuthContextIds.Normalize(contextId);

        var account = _loadAccount(role, normalized);

        if (HasScopeMismatch(role, account))
        {
            await _strategy.HandleScopeMismatchAsync(role, normalized, account, ct);
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
        var refreshLock = GetLockForKey(role, normalized, account);
        await refreshLock.WaitAsync(ct);

        try
        {
            account = _loadAccount(role, normalized);

            if (HasScopeMismatch(role, account))
            {
                await _strategy.HandleScopeMismatchAsync(role, normalized, account, ct);
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
            if (IsInBackoffOrCooldown((role, normalized), fingerprint))
            {
                return null;
            }

            try
            {
                return await ExecuteRefreshAsync(role, normalized, clientId, clientSecret, account.RefreshToken, fingerprint, ct);
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

    public Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        return RefreshTokenAsync(role, null, clientId, clientSecret, refreshToken, ct);
    }

    public async Task<string> RefreshTokenAsync(
        PlatformAuthRole role,
        string? contextId,
        string clientId,
        string clientSecret,
        string refreshToken,
        CancellationToken ct = default)
    {
        PlatformAuthRoles.EnsureActive(role);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var normalized = AuthContextIds.Normalize(contextId);
        var initialAccount = _loadAccount(role, normalized);
        var refreshLock = GetLockForKey(role, normalized, initialAccount);
        await refreshLock.WaitAsync(ct);

        try
        {
            var currentAccount = _loadAccount(role, normalized);
            var fingerprint = ComputeCredentialFingerprint(clientId, clientSecret, refreshToken, currentAccount.AccessToken);
            return await ExecuteRefreshAsync(role, normalized, clientId, clientSecret, refreshToken, fingerprint, ct);
        }
        finally
        {
            refreshLock.Release();
        }
    }

    public void ResetRetryState(PlatformAuthRole role)
    {
        ResetRetryState(role, null);
    }

    public void ResetRetryState(PlatformAuthRole role, string? contextId)
    {
        PlatformAuthRoles.EnsureActive(role);
        var normalized = AuthContextIds.Normalize(contextId);

        lock (_stateSync)
        {
            _retryStates.Remove((role, normalized));
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
        foreach (var semaphore in _refreshLocks.Values)
        {
            semaphore.Dispose();
        }

        _refreshLocks.Clear();
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
        string contextId,
        string clientId,
        string clientSecret,
        string refreshToken,
        string fingerprint,
        CancellationToken ct)
    {
        try
        {
            var accessToken = await _strategy.RefreshAndSaveAsync(role, contextId, clientId, clientSecret, refreshToken, ct);
            ResetRetryState(role, contextId);
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
                    await _strategy.OnRefreshTokenRejectedAsync(role, contextId, exception, ct);
                    ResetRetryState(role, contextId);
                    break;
                case OAuthRefreshFailureKind.CredentialsRejected:
                    var pausedUntil = RecordFailure((role, contextId), fingerprint, _strategy.CredentialsRejectedCooldown, fixedDelay: true);
                    _strategy.OnCredentialsRejected(role, exception, pausedUntil);
                    break;
                case OAuthRefreshFailureKind.Transient:
                    var nextAttempt = RecordFailure((role, contextId), fingerprint, _strategy.BaseTransientRetryDelay, fixedDelay: false);
                    _strategy.OnTransientFailure(role, exception, nextAttempt);
                    break;
            }

            throw;
        }
    }

    private bool IsInBackoffOrCooldown((PlatformAuthRole Role, string ContextId) key, string fingerprint)
    {
        var now = _timeProvider.GetUtcNow();
        RetryState? deferredState = null;

        lock (_stateSync)
        {
            if (!_retryStates.TryGetValue(key, out var state))
            {
                return false;
            }

            if (!string.Equals(state.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                _retryStates.Remove(key);
                return false;
            }

            if (state.BlockedUntilUtc > now)
            {
                deferredState = state;
            }
        }

        if (deferredState is { } deferred)
        {
            _strategy.OnRetryDeferred(key.Role, deferred.BlockedUntilUtc, deferred.Failures);
            return true;
        }

        return false;
    }

    private DateTimeOffset RecordFailure((PlatformAuthRole Role, string ContextId) key, string fingerprint, TimeSpan delay, bool fixedDelay)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_stateSync)
        {
            var failures = _retryStates.TryGetValue(key, out var state)
                && string.Equals(state.Fingerprint, fingerprint, StringComparison.Ordinal)
                ? state.Failures + 1
                : 1;

            var effectiveDelay = fixedDelay
                ? delay
                : CalculateExponentialDelay(failures);

            var blockedUntil = now + effectiveDelay;
            _retryStates[key] = new(failures, blockedUntil, fingerprint);
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

    private SemaphoreSlim GetLockForKey(PlatformAuthRole role, string contextId, TAccount account)
    {
        var groupKey = _getLockGroupKey?.Invoke(account);
        var key = !string.IsNullOrWhiteSpace(groupKey)
            ? $"subject:{groupKey.Trim()}"
            : $"ctx:{(int)role}:{contextId}";
        return _refreshLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
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
