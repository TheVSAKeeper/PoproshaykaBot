using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Moderation;
using PoproshaykaBot.Core.Platforms;
using System.Security.Cryptography;

namespace PoproshaykaBot.Core.Statistics.Linking;

public sealed record AccountLinkActionResult(
    AccountLinkOperationStatus Status,
    string? Code = null,
    PlatformUserKey? InitiatorKey = null,
    string? InitiatorName = null,
    PlatformUserKey? TargetKey = null,
    string? TargetName = null,
    AccountLinkGroupRecord? Group = null)
{
    public bool Succeeded => Status is AccountLinkOperationStatus.ChallengeIssued
        or AccountLinkOperationStatus.ClaimIssued
        or AccountLinkOperationStatus.Linked
        or AccountLinkOperationStatus.Unlinked;
}

public interface IAccountLinkingService
{
    AccountLinkActionResult InitiateViewerChallenge(PlatformUserKey initiator, string initiatorName);

    AccountLinkActionResult SubmitViewerClaim(PlatformUserKey claimant, string claimantName, string challengeCode);

    Task<AccountLinkActionResult> ConfirmViewerLinkAsync(
        PlatformUserKey confirmer,
        string confirmerName,
        string confirmCode,
        CancellationToken cancellationToken = default);

    Task<AccountLinkActionResult> UnlinkSelfAsync(
        PlatformUserKey caller,
        string callerName,
        CancellationToken cancellationToken = default);

    Task<AccountLinkActionResult> LinkManualAsync(
        IReadOnlyList<PlatformUserKey> keys,
        AccountLinkAuthor author,
        CancellationToken cancellationToken = default);

    Task<AccountLinkActionResult> UnlinkManualAsync(
        PlatformUserKey target,
        AccountLinkAuthor author,
        CancellationToken cancellationToken = default);
}

public sealed class AccountLinkingService(
    IUserStatisticsRepository repository,
    IEventBus eventBus,
    TimeProvider? timeProvider = null) : IAccountLinkingService
{
    internal static readonly TimeSpan ChallengeTtl = TimeSpan.FromMinutes(10);
    internal const int MaxClaimsPerChallenge = 5;
    private const string CodeAlphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly Dictionary<PlatformUserKey, PendingLinkRequest> _requestsByInitiator = [];
    private readonly Dictionary<string, PendingLinkRequest> _requestsByCode = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (PendingLinkRequest Request, PendingLinkClaim Claim)> _claimsByCode = new(StringComparer.OrdinalIgnoreCase);

    public AccountLinkActionResult InitiateViewerChallenge(PlatformUserKey initiator, string initiatorName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(initiatorName);
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            CleanupExpiredLocked(now);
            RemoveInitiatorRequestLocked(initiator);

            var code = GenerateUniqueCodeLocked("R-", static (self, candidate) => self._requestsByCode.ContainsKey(candidate));
            var request = new PendingLinkRequest(
                initiator,
                initiatorName.Trim(),
                code,
                now + ChallengeTtl);

            _requestsByInitiator[initiator] = request;
            _requestsByCode[code] = request;

            return new(
                AccountLinkOperationStatus.ChallengeIssued,
                Code: code,
                InitiatorKey: initiator,
                InitiatorName: request.InitiatorName);
        }
    }

    public AccountLinkActionResult SubmitViewerClaim(PlatformUserKey claimant, string claimantName, string challengeCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimantName);
        if (string.IsNullOrWhiteSpace(challengeCode))
        {
            return new(AccountLinkOperationStatus.InvalidOrExpiredCode);
        }

        var normalizedCode = challengeCode.Trim().ToUpperInvariant();
        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            CleanupExpiredLocked(now);

            if (!_requestsByCode.TryGetValue(normalizedCode, out var request) || request.ExpiresAtUtc <= now)
            {
                return new(AccountLinkOperationStatus.InvalidOrExpiredCode);
            }

            if (claimant == request.Initiator)
            {
                return new(AccountLinkOperationStatus.SameAccountRejected);
            }

            var initiatorGroup = repository.GetLinkGroup(request.Initiator);
            var claimantGroup = repository.GetLinkGroup(claimant);

            if (initiatorGroup is not null && claimantGroup is not null && initiatorGroup.GroupId == claimantGroup.GroupId)
            {
                return new(AccountLinkOperationStatus.AlreadyLinkedSameGroup, Group: initiatorGroup);
            }

            if (claimantGroup is not null)
            {
                return new(AccountLinkOperationStatus.TargetInAnotherGroup);
            }

            if (request.Claims.TryGetValue(claimant, out var existing))
            {
                return new(
                    AccountLinkOperationStatus.ClaimIssued,
                    Code: existing.ConfirmCode,
                    InitiatorKey: request.Initiator,
                    InitiatorName: request.InitiatorName,
                    TargetKey: claimant,
                    TargetName: existing.ClaimantName);
            }

            if (request.Claims.Count >= MaxClaimsPerChallenge)
            {
                return new(AccountLinkOperationStatus.ClaimLimitReached);
            }

            var confirmCode = GenerateUniqueCodeLocked("C-", static (self, candidate) => self._claimsByCode.ContainsKey(candidate));
            var claim = new PendingLinkClaim(claimant, claimantName.Trim(), confirmCode);
            request.Claims[claimant] = claim;
            _claimsByCode[confirmCode] = (request, claim);

            return new(
                AccountLinkOperationStatus.ClaimIssued,
                Code: confirmCode,
                InitiatorKey: request.Initiator,
                InitiatorName: request.InitiatorName,
                TargetKey: claimant,
                TargetName: claim.ClaimantName);
        }
    }

    public async Task<AccountLinkActionResult> ConfirmViewerLinkAsync(
        PlatformUserKey confirmer,
        string confirmerName,
        string confirmCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmerName);
        if (string.IsNullOrWhiteSpace(confirmCode))
        {
            return new(AccountLinkOperationStatus.InvalidOrExpiredCode);
        }

        var normalizedCode = confirmCode.Trim().ToUpperInvariant();
        var now = _timeProvider.GetUtcNow();

        PendingLinkRequest request;
        PendingLinkClaim claim;
        AccountLinkOperationStatus status;
        AccountLinkGroupRecord? group;

        var author = new AccountLinkAuthor(
            AccountLinkAuthorKind.Viewer,
            confirmer.Platform,
            confirmer.UserId,
            confirmerName.Trim());

        lock (_lock)
        {
            CleanupExpiredLocked(now);

            if (!_claimsByCode.TryGetValue(normalizedCode, out var entry) || entry.Request.ExpiresAtUtc <= now)
            {
                return new(AccountLinkOperationStatus.InvalidOrExpiredCode);
            }

            request = entry.Request;
            claim = entry.Claim;

            if (confirmer != request.Initiator)
            {
                return new(
                    AccountLinkOperationStatus.WrongConfirmingAccount,
                    InitiatorKey: request.Initiator,
                    InitiatorName: request.InitiatorName);
            }

            status = repository.LinkAccounts(
                [request.Initiator, claim.Claimant],
                AccountLinkOrigin.ViewerConfirmed,
                author,
                allowMergeExistingGroups: false,
                out group);

            if (status == AccountLinkOperationStatus.Linked)
            {
                RemoveInitiatorRequestLocked(request.Initiator);
            }
        }

        if (status == AccountLinkOperationStatus.Linked)
        {
            await eventBus.PublishAsync(
                    new AccountLinksChanged(status, request.Initiator, group?.GroupId),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new(
            status,
            InitiatorKey: request.Initiator,
            InitiatorName: request.InitiatorName,
            TargetKey: claim.Claimant,
            TargetName: claim.ClaimantName,
            Group: group);
    }

    public async Task<AccountLinkActionResult> UnlinkSelfAsync(
        PlatformUserKey caller,
        string callerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callerName);

        var author = new AccountLinkAuthor(
            AccountLinkAuthorKind.Viewer,
            caller.Platform,
            caller.UserId,
            callerName.Trim());

        var status = repository.UnlinkAccount(caller, author);
        if (status == AccountLinkOperationStatus.Unlinked)
        {
            await eventBus.PublishAsync(
                    new AccountLinksChanged(status, caller, null),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new(status, InitiatorKey: caller, InitiatorName: callerName.Trim());
    }

    public async Task<AccountLinkActionResult> LinkManualAsync(
        IReadOnlyList<PlatformUserKey> keys,
        AccountLinkAuthor author,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(author);

        var status = repository.LinkAccounts(
            keys,
            AccountLinkOrigin.StreamerManual,
            author,
            allowMergeExistingGroups: true,
            out var group);

        if (status == AccountLinkOperationStatus.Linked && keys.Count > 0)
        {
            await eventBus.PublishAsync(
                    new AccountLinksChanged(status, keys[0], group?.GroupId),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new(status, Group: group);
    }

    public async Task<AccountLinkActionResult> UnlinkManualAsync(
        PlatformUserKey target,
        AccountLinkAuthor author,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(author);

        var status = repository.UnlinkAccount(target, author);
        if (status == AccountLinkOperationStatus.Unlinked)
        {
            await eventBus.PublishAsync(
                    new AccountLinksChanged(status, target, null),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return new(status, TargetKey: target);
    }

    private string GenerateUniqueCodeLocked(string prefix, Func<AccountLinkingService, string, bool> exists)
    {
        Span<char> chars = stackalloc char[4];
        for (var attempt = 0; attempt < 128; attempt++)
        {
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            }

            var candidate = prefix + new string(chars);
            if (!exists(this, candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Не удалось сгенерировать уникальный код привязки.");
    }

    private void CleanupExpiredLocked(DateTimeOffset now)
    {
        var expiredInitiators = _requestsByInitiator
            .Where(pair => pair.Value.ExpiresAtUtc <= now)
            .Select(static pair => pair.Key)
            .ToList();

        foreach (var initiator in expiredInitiators)
        {
            RemoveInitiatorRequestLocked(initiator);
        }
    }

    private void RemoveInitiatorRequestLocked(PlatformUserKey initiator)
    {
        if (!_requestsByInitiator.Remove(initiator, out var existing))
        {
            return;
        }

        _requestsByCode.Remove(existing.ChallengeCode);
        foreach (var claim in existing.Claims.Values)
        {
            _claimsByCode.Remove(claim.ConfirmCode);
        }
    }

    private sealed class PendingLinkRequest(
        PlatformUserKey initiator,
        string initiatorName,
        string challengeCode,
        DateTimeOffset expiresAtUtc)
    {
        public PlatformUserKey Initiator { get; } = initiator;
        public string InitiatorName { get; } = initiatorName;
        public string ChallengeCode { get; } = challengeCode;
        public DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;
        public Dictionary<PlatformUserKey, PendingLinkClaim> Claims { get; } = [];
    }

    private sealed record PendingLinkClaim(
        PlatformUserKey Claimant,
        string ClaimantName,
        string ConfirmCode);
}
