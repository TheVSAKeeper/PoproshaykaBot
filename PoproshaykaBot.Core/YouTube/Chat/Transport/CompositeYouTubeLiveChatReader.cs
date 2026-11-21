using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.YouTube.Chat.InnerTube;
using PoproshaykaBot.Core.YouTube.Client;

namespace PoproshaykaBot.Core.YouTube.Chat.Transport;

internal readonly record struct InnerTubeFailureOutcome(
    bool SwitchedToFallback,
    bool ResetCursorOnly,
    bool LiveStreamEnded,
    bool RequiresBotAuth,
    bool ShouldUseTransientBackoff);

internal sealed class CompositeYouTubeLiveChatReader(
    InnerTubeYouTubeLiveChatReader? innerTubeReader,
    OfficialGrpcYouTubeLiveChatReader grpcReader,
    OfficialRestYouTubeLiveChatReader restReader,
    TimeProvider? timeProvider = null,
    ILogger<CompositeYouTubeLiveChatReader>? logger = null)
    : IYouTubeLiveChatReader
{
    private const int MaxConsecutiveInnerTubeTransientFailures = 2;
    private const int MaxConsecutiveGrpcFailures = 2;
    private const int BlockedVideoCacheCapacity = 64;

    private static readonly TimeSpan InnerTubeCooldownDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaxConsentCooldownDuration = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan BlockedVideoDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan GrpcRetryInterval = TimeSpan.FromMinutes(10);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _sync = new();
    private readonly Dictionary<string, DateTimeOffset> _blockedInnerTubeVideos = new(StringComparer.Ordinal);
    private readonly Queue<string> _blockedVideoOrder = new();

    private YouTubeChatTransportKind _lastTransport = innerTubeReader is not null
        ? YouTubeChatTransportKind.InnerTube
        : YouTubeChatTransportKind.OfficialGrpc;
    private DateTimeOffset? _innerTubeCooldownUntilUtc;
    private int _consecutiveInnerTubeTransientFailures;
    private int _consecutiveConsentFailures;
    private DateTimeOffset? _grpcFallbackUntilUtc;
    private int _consecutiveGrpcFailures;
    private string? _lastLoggedSharedVideoId;

    public YouTubeChatTransportKind TransportKind
    {
        get
        {
            lock (_sync)
            {
                return _lastTransport;
            }
        }
    }

    public bool IsServerStream => false;

    public bool CanRead(YouTubeChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return innerTubeReader?.CanRead(target) == true
            || grpcReader.CanRead(target)
            || restReader.CanRead(target);
    }

    public YouTubeChatTransportKind SelectTransport(YouTubeChatTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var nowUtc = _timeProvider.GetUtcNow();
        string? expiredCooldownVideoId;
        string? skippedSharedVideoId = null;
        YouTubeChatTransportKind selected;

        lock (_sync)
        {
            if (target.RequiresOfficialMessageIds
                && !string.IsNullOrWhiteSpace(target.VideoId)
                && !string.Equals(_lastLoggedSharedVideoId, target.VideoId, StringComparison.Ordinal))
            {
                _lastLoggedSharedVideoId = target.VideoId;
                skippedSharedVideoId = target.VideoId;
            }

            if (TrySelectInnerTubeLocked(target, nowUtc, out expiredCooldownVideoId))
            {
                selected = YouTubeChatTransportKind.InnerTube;
            }
            else
            {
                selected = SelectOfficialTransportLocked(target, nowUtc);
            }

            if (selected != YouTubeChatTransportKind.None)
            {
                _lastTransport = selected;
            }
        }

        if (skippedSharedVideoId is not null && logger is not null)
        {
            CoreLog.InnerTubeSkippedSharedAccount(logger, skippedSharedVideoId);
        }

        if (expiredCooldownVideoId is not null && logger is not null)
        {
            CoreLog.InnerTubeCooldownExpired(logger, expiredCooldownVideoId);
        }

        return selected;
    }

    public IAsyncEnumerable<YouTubeLiveChatBatch> ReadBatchesAsync(
        YouTubeChatTarget target,
        YouTubeChatCursorToken? cursor,
        CancellationToken cancellationToken)
    {
        return ReadBatchesAsync(target, cursor, maxBatches: null, cancellationToken);
    }

    public IAsyncEnumerable<YouTubeLiveChatBatch> ReadBatchesAsync(
        YouTubeChatTarget target,
        YouTubeChatCursorToken? cursor,
        int? maxBatches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (maxBatches is <= 0)
        {
            return EmptyBatchesAsync();
        }

        return ReadBatchesWithFallbackAsync(target, cursor, maxBatches, cancellationToken);
    }

    internal IAsyncEnumerable<YouTubeLiveChatBatch> ReadSelectedTransportAsync(
        YouTubeChatTarget target,
        YouTubeChatTransportKind transport,
        YouTubeChatCursorToken? cursor,
        int? maxBatches,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        return transport switch
        {
            YouTubeChatTransportKind.InnerTube when innerTubeReader is not null
                => innerTubeReader.ReadBatchesAsync(target, cursor, maxBatches, cancellationToken),
            YouTubeChatTransportKind.OfficialGrpc
                => grpcReader.ReadBatchesAsync(target, cursor, maxBatches, cancellationToken),
            YouTubeChatTransportKind.OfficialRest
                => restReader.ReadBatchesAsync(target, cursor, maxBatches, cancellationToken),
            _ => EmptyBatchesAsync(),
        };
    }

    internal TimeSpan? GetRemainingInnerTubeCooldown()
    {
        lock (_sync)
        {
            if (!_innerTubeCooldownUntilUtc.HasValue)
            {
                return null;
            }

            var remaining = _innerTubeCooldownUntilUtc.Value - _timeProvider.GetUtcNow();
            return remaining > TimeSpan.Zero ? remaining : null;
        }
    }

    internal void RecordSuccess(YouTubeChatTransportKind transport)
    {
        lock (_sync)
        {
            _lastTransport = transport;

            if (transport == YouTubeChatTransportKind.InnerTube)
            {
                _consecutiveInnerTubeTransientFailures = 0;
                _consecutiveConsentFailures = 0;
            }
            else if (transport == YouTubeChatTransportKind.OfficialGrpc)
            {
                _consecutiveGrpcFailures = 0;
            }
        }
    }

    internal InnerTubeFailureOutcome HandleInnerTubeFailure(YouTubeChatTarget target, InnerTubeChatException exception)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.Kind == InnerTubeErrorKind.LiveStreamEnded)
        {
            return new(
                SwitchedToFallback: false,
                ResetCursorOnly: false,
                LiveStreamEnded: true,
                RequiresBotAuth: false,
                ShouldUseTransientBackoff: false);
        }

        if (exception.Kind == InnerTubeErrorKind.ContinuationInvalidated)
        {
            if (logger is not null)
            {
                CoreLog.InnerTubeCursorReset(logger, target.VideoId, exception.Message);
            }

            return new(
                SwitchedToFallback: false,
                ResetCursorOnly: true,
                LiveStreamEnded: false,
                RequiresBotAuth: false,
                ShouldUseTransientBackoff: false);
        }

        var nowUtc = _timeProvider.GetUtcNow();
        var hasOfficialFallback = grpcReader.CanRead(target) || restReader.CanRead(target);

        if (!hasOfficialFallback)
        {
            var requiresAuth = exception.Kind is InnerTubeErrorKind.LoginRequired
                or InnerTubeErrorKind.MembersOnly
                or InnerTubeErrorKind.Forbidden;

            return new(
                SwitchedToFallback: false,
                ResetCursorOnly: false,
                LiveStreamEnded: false,
                RequiresBotAuth: requiresAuth,
                ShouldUseTransientBackoff: !requiresAuth);
        }

        var activatedFallback = false;
        var loggedCooldownMinutes = 0;

        lock (_sync)
        {
            switch (exception.Kind)
            {
                case InnerTubeErrorKind.RateLimited:
                case InnerTubeErrorKind.Forbidden:
                    _innerTubeCooldownUntilUtc = nowUtc + InnerTubeCooldownDuration;
                    _consecutiveInnerTubeTransientFailures = 0;
                    loggedCooldownMinutes = (int)InnerTubeCooldownDuration.TotalMinutes;
                    activatedFallback = true;
                    break;

                case InnerTubeErrorKind.ConsentRequired:
                    _consecutiveConsentFailures++;
                    var consentMultiplier = 1 << Math.Min(_consecutiveConsentFailures - 1, 3);
                    var consentCooldown = TimeSpan.FromMinutes(Math.Min(
                        MaxConsentCooldownDuration.TotalMinutes,
                        InnerTubeCooldownDuration.TotalMinutes * consentMultiplier));
                    _innerTubeCooldownUntilUtc = nowUtc + consentCooldown;
                    _consecutiveInnerTubeTransientFailures = 0;
                    loggedCooldownMinutes = (int)consentCooldown.TotalMinutes;
                    activatedFallback = true;
                    break;

                case InnerTubeErrorKind.LoginRequired:
                case InnerTubeErrorKind.MembersOnly:
                    BlockInnerTubeVideoLocked(target.VideoId, nowUtc + BlockedVideoDuration);
                    _consecutiveInnerTubeTransientFailures = 0;
                    loggedCooldownMinutes = (int)BlockedVideoDuration.TotalMinutes;
                    activatedFallback = true;
                    break;

                default:
                    _consecutiveInnerTubeTransientFailures++;

                    if (_consecutiveInnerTubeTransientFailures >= MaxConsecutiveInnerTubeTransientFailures)
                    {
                        _innerTubeCooldownUntilUtc = nowUtc + InnerTubeCooldownDuration;
                        _consecutiveInnerTubeTransientFailures = 0;
                        loggedCooldownMinutes = (int)InnerTubeCooldownDuration.TotalMinutes;
                        activatedFallback = true;
                    }

                    break;
            }
        }

        if (activatedFallback)
        {
            if (logger is not null)
            {
                CoreLog.InnerTubeFallbackActivated(logger, exception.Kind.ToString(), loggedCooldownMinutes);
            }

            return new(
                SwitchedToFallback: true,
                ResetCursorOnly: false,
                LiveStreamEnded: false,
                RequiresBotAuth: false,
                ShouldUseTransientBackoff: false);
        }

        return new(
            SwitchedToFallback: false,
            ResetCursorOnly: false,
            LiveStreamEnded: false,
            RequiresBotAuth: false,
            ShouldUseTransientBackoff: true);
    }

    internal void SwitchToRestFallback()
    {
        lock (_sync)
        {
            _grpcFallbackUntilUtc = _timeProvider.GetUtcNow() + GrpcRetryInterval;
            _consecutiveGrpcFailures = 0;
        }
    }

    internal bool RecordGrpcTransientFailure()
    {
        lock (_sync)
        {
            _consecutiveGrpcFailures++;

            if (_consecutiveGrpcFailures < MaxConsecutiveGrpcFailures)
            {
                return false;
            }

            _grpcFallbackUntilUtc = _timeProvider.GetUtcNow() + GrpcRetryInterval;
            _consecutiveGrpcFailures = 0;
            return true;
        }
    }

    private async IAsyncEnumerable<YouTubeLiveChatBatch> ReadBatchesWithFallbackAsync(
        YouTubeChatTarget target,
        YouTubeChatCursorToken? cursor,
        int? maxBatches,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var remainingBatches = maxBatches;
        var currentCursor = cursor;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var transport = SelectTransport(target);

            if (transport == YouTubeChatTransportKind.None)
            {
                yield break;
            }

            currentCursor = NormalizeCursorForTransport(currentCursor, transport, target.Mode);

            await using var enumerator = ReadSelectedTransportAsync(
                    target,
                    transport,
                    currentCursor,
                    remainingBatches,
                    cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            var retryWithFallback = false;

            while (true)
            {
                bool hasNext;

                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (InnerTubeChatException ex) when (transport == YouTubeChatTransportKind.InnerTube)
                {
                    var outcome = HandleInnerTubeFailure(target, ex);

                    if (outcome.SwitchedToFallback)
                    {
                        currentCursor = null;
                        retryWithFallback = true;
                        break;
                    }

                    throw;
                }
                catch (YouTubeApiException ex) when (transport == YouTubeChatTransportKind.OfficialGrpc)
                {
                    if (TryHandleGrpcFallbackException(ex, target))
                    {
                        retryWithFallback = true;
                        break;
                    }

                    throw;
                }
                catch (HttpRequestException) when (transport == YouTubeChatTransportKind.OfficialGrpc)
                {
                    if (RecordGrpcTransientFailure() && restReader.CanRead(target))
                    {
                        retryWithFallback = true;
                        break;
                    }

                    throw;
                }

                if (!hasNext)
                {
                    yield break;
                }

                var batch = enumerator.Current;
                RecordSuccess(transport);
                currentCursor = batch.NextCursor;

                yield return batch;

                if (remainingBatches is not null)
                {
                    remainingBatches--;

                    if (remainingBatches <= 0)
                    {
                        yield break;
                    }
                }
            }

            if (!retryWithFallback)
            {
                yield break;
            }
        }
    }

    private bool TryHandleGrpcFallbackException(YouTubeApiException exception, YouTubeChatTarget target)
    {
        if (exception.Kind == YouTubeApiErrorKind.TransportUnsupported)
        {
            SwitchToRestFallback();
            return restReader.CanRead(target);
        }

        if (exception.Kind is YouTubeApiErrorKind.InvalidPageToken
            or YouTubeApiErrorKind.LiveChatEnded
            or YouTubeApiErrorKind.QuotaExceeded
            or YouTubeApiErrorKind.LocalQuotaBudgetReached
            or YouTubeApiErrorKind.Unauthorized
            or YouTubeApiErrorKind.RateLimitExceeded)
        {
            return false;
        }

        return RecordGrpcTransientFailure() && restReader.CanRead(target);
    }

    private bool TrySelectInnerTubeLocked(
        YouTubeChatTarget target,
        DateTimeOffset nowUtc,
        out string? expiredCooldownVideoId)
    {
        expiredCooldownVideoId = null;

        if (innerTubeReader is null || !innerTubeReader.CanRead(target))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(target.VideoId)
            && _blockedInnerTubeVideos.TryGetValue(target.VideoId, out var blockedUntilUtc))
        {
            if (nowUtc < blockedUntilUtc)
            {
                return false;
            }

            _blockedInnerTubeVideos.Remove(target.VideoId);
        }

        if (_innerTubeCooldownUntilUtc.HasValue)
        {
            if (nowUtc < _innerTubeCooldownUntilUtc.Value)
            {
                return false;
            }

            _innerTubeCooldownUntilUtc = null;
            _consecutiveInnerTubeTransientFailures = 0;
            expiredCooldownVideoId = target.VideoId;
        }

        return true;
    }

    private YouTubeChatTransportKind SelectOfficialTransportLocked(YouTubeChatTarget target, DateTimeOffset nowUtc)
    {
        if (_grpcFallbackUntilUtc.HasValue && nowUtc >= _grpcFallbackUntilUtc.Value)
        {
            _grpcFallbackUntilUtc = null;
            _consecutiveGrpcFailures = 0;
        }

        if (_grpcFallbackUntilUtc is null && grpcReader.CanRead(target))
        {
            return YouTubeChatTransportKind.OfficialGrpc;
        }

        if (restReader.CanRead(target))
        {
            return YouTubeChatTransportKind.OfficialRest;
        }

        return grpcReader.CanRead(target)
            ? YouTubeChatTransportKind.OfficialGrpc
            : YouTubeChatTransportKind.None;
    }

    private void BlockInnerTubeVideoLocked(string? videoId, DateTimeOffset expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return;
        }

        if (_blockedInnerTubeVideos.ContainsKey(videoId))
        {
            _blockedInnerTubeVideos[videoId] = expiresAtUtc;
            return;
        }

        while (_blockedInnerTubeVideos.Count >= BlockedVideoCacheCapacity && _blockedVideoOrder.TryDequeue(out var oldest))
        {
            _blockedInnerTubeVideos.Remove(oldest);
        }

        _blockedInnerTubeVideos[videoId] = expiresAtUtc;
        _blockedVideoOrder.Enqueue(videoId);
    }

    private static YouTubeChatCursorToken? NormalizeCursorForTransport(
        YouTubeChatCursorToken? cursor,
        YouTubeChatTransportKind targetTransport,
        YouTubeChatMode targetMode)
    {
        if (cursor is null || string.IsNullOrWhiteSpace(cursor.Value))
        {
            return null;
        }

        if (cursor.Transport == targetTransport)
        {
            return cursor.Mode == targetMode ? cursor : null;
        }

        if (IsOfficial(cursor.Transport) && IsOfficial(targetTransport))
        {
            return cursor with { Transport = targetTransport };
        }

        return null;
    }

    private static bool IsOfficial(YouTubeChatTransportKind transport) =>
        transport is YouTubeChatTransportKind.OfficialGrpc or YouTubeChatTransportKind.OfficialRest;

    private static async IAsyncEnumerable<YouTubeLiveChatBatch> EmptyBatchesAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield break;
    }
}
