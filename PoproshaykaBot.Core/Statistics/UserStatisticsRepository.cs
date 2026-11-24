using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

public sealed class UserStatisticsRepository(
    ILogger<UserStatisticsRepository> logger,
    TimeProvider? timeProvider = null) : IUserStatisticsRepository
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly Dictionary<PlatformUserKey, ViewerAccountRecord> _accounts = [];
    private readonly Dictionary<ChannelUserKey, ChannelUserCounterRecord> _channelCounters = [];
    private readonly Dictionary<PlatformUserKey, UnattributedUserCounterRecord> _unattributedCounters = [];
    private readonly Dictionary<Guid, AccountLinkGroupRecord> _linkGroups = [];
    private readonly Dictionary<PlatformUserKey, Guid> _keyToGroupId = [];
    private UserStatisticsMigrationState? _migration;
    private bool _hasChanges;

    public (int Accounts, int ChannelCounters, int UnattributedCounters, int LinkGroups) GetCatalogCounts()
    {
        lock (_lock)
        {
            return (_accounts.Count, _channelCounters.Count, _unattributedCounters.Count, _linkGroups.Count);
        }
    }

    public bool HasChanges
    {
        get
        {
            lock (_lock)
            {
                return _hasChanges;
            }
        }
    }

    public IPlatformUserStatistics ForPlatform(StreamingPlatform platform)
    {
        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
        }

        return new PlatformSliceView(this, platform);
    }

    public IReadOnlyList<UserStatistics> GetAll()
    {
        lock (_lock)
        {
            var platforms = _accounts.Values.Select(static a => a.Platform)
                .Concat(_channelCounters.Values.Select(static c => c.Platform))
                .Concat(_unattributedCounters.Values.Select(static u => u.Platform))
                .Distinct()
                .ToList();

            var result = new List<UserStatistics>();
            foreach (var platform in platforms)
            {
                result.AddRange(GetPlatformUsersLocked(platform));
            }

            return result;
        }
    }

    public void TrackMessage(PlatformUserKey key, string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        lock (_lock)
        {
            var account = UpsertAccountLocked(key, username, now);
            account.LastSeen = now;

            if (!_unattributedCounters.TryGetValue(key, out var counter))
            {
                counter = new()
                {
                    Platform = key.Platform,
                    UserId = key.UserId,
                    FirstSeen = now,
                    LastSeen = now,
                };
                _unattributedCounters[key] = counter;
            }

            counter.MessageCount++;
            counter.LastSeen = now;
            _hasChanges = true;
        }
    }

    public void TrackMessage(ChannelKey channelKey, PlatformUserKey key, string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var compositeKey = new ChannelUserKey(channelKey, key);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        lock (_lock)
        {
            var account = UpsertAccountLocked(key, username, now);
            account.LastSeen = now;

            if (!_channelCounters.TryGetValue(compositeKey, out var counter))
            {
                counter = new()
                {
                    Platform = channelKey.Platform,
                    ChannelId = channelKey.ChannelId,
                    UserId = key.UserId,
                    FirstSeen = now,
                    LastSeen = now,
                };
                _channelCounters[compositeKey] = counter;
            }

            counter.MessageCount++;
            counter.LastSeen = now;
            _hasChanges = true;
        }
    }

    public bool IncrementBonusPoints(PlatformUserKey key, ulong delta)
    {
        return AdjustAnyTargetPoints(key, delta, isBonus: true);
    }

    public bool IncrementPenaltyPoints(PlatformUserKey key, ulong delta)
    {
        return AdjustAnyTargetPoints(key, delta, isBonus: false);
    }

    public bool IncrementBonusPoints(
        StatisticsCounterTarget target,
        PlatformUserKey key,
        ulong delta,
        string? fallbackUserName = null)
    {
        return AdjustTargetPoints(target, key, delta, isBonus: true, fallbackUserName);
    }

    public bool IncrementPenaltyPoints(
        StatisticsCounterTarget target,
        PlatformUserKey key,
        ulong delta,
        string? fallbackUserName = null)
    {
        return AdjustTargetPoints(target, key, delta, isBonus: false, fallbackUserName);
    }

    public IReadOnlyList<ViewerAggregateStatistics> QuerySlice(StatisticsSlice slice)
    {
        ArgumentNullException.ThrowIfNull(slice);

        lock (_lock)
        {
            return QuerySliceLocked(slice);
        }
    }

    public IReadOnlyList<ViewerAggregateStatistics> GetTop(
        StatisticsSlice slice,
        int count,
        UserTopMode mode = UserTopMode.Points)
    {
        ArgumentNullException.ThrowIfNull(slice);
        if (count <= 0)
        {
            return [];
        }

        lock (_lock)
        {
            var items = QuerySliceLocked(slice);
            IEnumerable<ViewerAggregateStatistics> ordered = mode == UserTopMode.Messages
                ? items.OrderByDescending(static x => x.MessageCount).ThenByDescending(static x => x.Points)
                : items.OrderByDescending(static x => x.Points).ThenByDescending(static x => x.MessageCount);

            return [.. ordered.Take(count)];
        }
    }

    public ViewerAggregateStatistics? FindViewerById(StatisticsSlice slice, PlatformUserKey key)
    {
        ArgumentNullException.ThrowIfNull(slice);

        lock (_lock)
        {
            var identity = ResolveIdentityLocked(key);
            return slice.Channel is { } channel
                ? FindChannelViewerLocked(slice, channel, identity)
                : QuerySliceLocked(slice, identity).SingleOrDefault();
        }
    }

    public ViewerAggregateStatistics? FindViewerByName(
        StatisticsSlice slice,
        string name,
        StreamingPlatform? preferredPlatform = null)
    {
        ArgumentNullException.ThrowIfNull(slice);

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var target = name.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        lock (_lock)
        {
            var matches = QuerySliceLocked(slice)
                .Where(viewer => viewer.Accounts.Any(account =>
                    string.Equals(account.Name, target, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (matches.Count == 0)
            {
                return null;
            }

            if (matches.Count == 1 || preferredPlatform is null)
            {
                return matches.OrderByDescending(static v => v.LastSeen).First();
            }

            return matches
                .OrderByDescending(v => v.Accounts.Any(a =>
                    a.Key.Platform == preferredPlatform.Value
                    && string.Equals(a.Name, target, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(static v => v.LastSeen)
                .First();
        }
    }

    public IReadOnlySet<PlatformUserKey> GetLinkedKeys(PlatformUserKey key)
    {
        lock (_lock)
        {
            if (_keyToGroupId.TryGetValue(key, out var groupId)
                && _linkGroups.TryGetValue(groupId, out var group))
            {
                return group.Members.Select(static m => m.Key).ToHashSet();
            }

            return new HashSet<PlatformUserKey> { key };
        }
    }

    public IReadOnlyList<ViewerAccountRecord> GetKnownAccounts()
    {
        lock (_lock)
        {
            return [.. _accounts.Values.Select(static a => a.Clone())];
        }
    }

    public IReadOnlyList<ChannelKey> GetKnownChannels()
    {
        lock (_lock)
        {
            return [.. _channelCounters.Keys.Select(static k => k.Channel).Distinct()];
        }
    }

    public IReadOnlyList<StreamingPlatform> GetUnattributedPlatforms()
    {
        lock (_lock)
        {
            return [.. _unattributedCounters.Keys.Select(static k => k.Platform).Distinct().OrderBy(static p => p)];
        }
    }

    public AccountLinkGroupRecord? GetLinkGroup(PlatformUserKey key)
    {
        lock (_lock)
        {
            return _keyToGroupId.TryGetValue(key, out var groupId) && _linkGroups.TryGetValue(groupId, out var group)
                ? group.Clone()
                : null;
        }
    }

    public AccountLinkOperationStatus LinkAccounts(
        IReadOnlyList<PlatformUserKey> keys,
        AccountLinkOrigin origin,
        AccountLinkAuthor author,
        bool allowMergeExistingGroups,
        out AccountLinkGroupRecord? resultGroup)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(author);

        var distinctKeys = keys.Distinct().ToList();
        if (distinctKeys.Count < 2)
        {
            resultGroup = null;
            return AccountLinkOperationStatus.SameAccountRejected;
        }

        var now = _timeProvider.GetUtcNow();

        lock (_lock)
        {
            var initiator = distinctKeys[0];
            _keyToGroupId.TryGetValue(initiator, out var initiatorGroupId);
            var hasInitiatorGroup = initiatorGroupId != Guid.Empty && _linkGroups.ContainsKey(initiatorGroupId);

            var allGroupIds = new HashSet<Guid>();
            foreach (var key in distinctKeys)
            {
                if (_keyToGroupId.TryGetValue(key, out var gid) && _linkGroups.ContainsKey(gid))
                {
                    allGroupIds.Add(gid);
                }
            }

            if (allGroupIds.Count == 1
                && distinctKeys.All(k => _keyToGroupId.TryGetValue(k, out var g) && g == allGroupIds.First()))
            {
                resultGroup = _linkGroups[allGroupIds.First()].Clone();
                return AccountLinkOperationStatus.AlreadyLinkedSameGroup;
            }

            if (!allowMergeExistingGroups)
            {
                foreach (var target in distinctKeys.Skip(1))
                {
                    if (_keyToGroupId.TryGetValue(target, out var targetGroupId)
                        && _linkGroups.ContainsKey(targetGroupId)
                        && (!hasInitiatorGroup || targetGroupId != initiatorGroupId))
                    {
                        resultGroup = null;
                        return AccountLinkOperationStatus.TargetInAnotherGroup;
                    }
                }
            }

            AccountLinkGroupRecord targetGroup;
            if (hasInitiatorGroup)
            {
                targetGroup = _linkGroups[initiatorGroupId];
            }
            else if (allGroupIds.Count > 0)
            {
                var firstId = allGroupIds.First();
                targetGroup = _linkGroups[firstId];
            }
            else
            {
                targetGroup = new()
                {
                    GroupId = Guid.NewGuid(),
                    CreatedAtUtc = now,
                };
                _linkGroups[targetGroup.GroupId] = targetGroup;
            }

            foreach (var otherGroupId in allGroupIds)
            {
                if (otherGroupId == targetGroup.GroupId || !_linkGroups.TryGetValue(otherGroupId, out var otherGroup))
                {
                    continue;
                }

                foreach (var member in otherGroup.Members)
                {
                    if (!targetGroup.Members.Any(m => m.Key == member.Key))
                    {
                        targetGroup.Members.Add(member.Clone());
                    }

                    _keyToGroupId[member.Key] = targetGroup.GroupId;
                }

                _linkGroups.Remove(otherGroupId);
            }

            foreach (var key in distinctKeys)
            {
                if (!targetGroup.Members.Any(m => m.Key == key))
                {
                    targetGroup.Members.Add(new()
                    {
                        Platform = key.Platform,
                        UserId = key.UserId,
                        Origin = origin,
                        Author = author,
                        LinkedAtUtc = now,
                    });
                }

                _keyToGroupId[key] = targetGroup.GroupId;
            }

            _hasChanges = true;
            resultGroup = targetGroup.Clone();
            return AccountLinkOperationStatus.Linked;
        }
    }

    public AccountLinkOperationStatus UnlinkAccount(PlatformUserKey key, AccountLinkAuthor author)
    {
        ArgumentNullException.ThrowIfNull(author);

        lock (_lock)
        {
            if (!_keyToGroupId.TryGetValue(key, out var groupId)
                || !_linkGroups.TryGetValue(groupId, out var group))
            {
                return AccountLinkOperationStatus.NotLinked;
            }

            group.Members.RemoveAll(m => m.Key == key);
            _keyToGroupId.Remove(key);

            if (group.Members.Count < 2)
            {
                foreach (var remaining in group.Members)
                {
                    _keyToGroupId.Remove(remaining.Key);
                }

                _linkGroups.Remove(groupId);
            }

            _hasChanges = true;
            return AccountLinkOperationStatus.Unlinked;
        }
    }

    public PointsTransferPreview PreviewTransfer(PointsTransferRequest request)
    {
        ValidateTransferRequest(request);

        lock (_lock)
        {
            var plan = BuildTransferPlanLocked(request);
            return plan.ToPreview();
        }
    }

    public PointsTransferResult ExecuteTransfer(PointsTransferRequest request)
    {
        ValidateTransferRequest(request);

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        lock (_lock)
        {
            var plan = BuildTransferPlanLocked(request);

            foreach (var entry in plan.Entries)
            {
                if (!entry.Preview.WillTransfer || entry.Preview.TargetUserKey is not { } targetUserKey)
                {
                    continue;
                }

                ApplyTransferEntryLocked(request, entry.SourceUserKey, targetUserKey, entry.Preview, now);
            }

            if (plan.TransferredCount > 0)
            {
                _hasChanges = true;
                logger.LogInformation(
                    "Выполнен перенос баллов из {Source} в {TargetPlatform}/{TargetChannelId} (режим: {Mode}): перенесено {TransferredCount}, пропущено {SkippedCount}, баллов {TotalPoints}",
                    request.Source,
                    request.Target.Platform,
                    request.Target.ChannelId,
                    request.Mode,
                    plan.TransferredCount,
                    plan.SkippedCount,
                    plan.TotalPoints);
            }

            return plan.ToResult();
        }
    }

    public void SeedPlatform(StreamingPlatform platform, IEnumerable<UserStatistics> users)
    {
        ArgumentNullException.ThrowIfNull(users);
        if (!Enum.IsDefined(platform))
        {
            throw new ArgumentOutOfRangeException(nameof(platform), platform, "Неизвестная площадка.");
        }

        lock (_lock)
        {
            foreach (var key in _accounts.Keys.Where(k => k.Platform == platform).ToList())
            {
                _accounts.Remove(key);
            }

            foreach (var key in _channelCounters.Keys.Where(k => k.Channel.Platform == platform).ToList())
            {
                _channelCounters.Remove(key);
            }

            foreach (var key in _unattributedCounters.Keys.Where(k => k.Platform == platform).ToList())
            {
                _unattributedCounters.Remove(key);
            }

            foreach (var user in users)
            {
                if (string.IsNullOrWhiteSpace(user.UserId))
                {
                    continue;
                }

                var key = new PlatformUserKey(platform, user.UserId);
                _accounts[key] = new()
                {
                    Platform = platform,
                    UserId = user.UserId,
                    Name = user.Name,
                    FirstSeen = user.FirstSeen,
                    LastSeen = user.LastSeen,
                };
                _unattributedCounters[key] = new()
                {
                    Platform = platform,
                    UserId = user.UserId,
                    MessageCount = user.MessageCount,
                    BonusPoints = user.BonusPoints,
                    PenaltyPoints = user.PenaltyPoints,
                    FirstSeen = user.FirstSeen,
                    LastSeen = user.LastSeen,
                };
            }

            _hasChanges = false;
        }
    }

    public void SeedChannel(ChannelKey channelKey, IEnumerable<UserStatistics> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        lock (_lock)
        {
            foreach (var key in _channelCounters.Keys.Where(k => k.Channel == channelKey).ToList())
            {
                _channelCounters.Remove(key);
            }

            foreach (var user in users)
            {
                if (string.IsNullOrWhiteSpace(user.UserId))
                {
                    continue;
                }

                var userKey = new PlatformUserKey(channelKey.Platform, user.UserId);
                UpsertAccountSeedLocked(userKey, user.Name, user.FirstSeen, user.LastSeen);

                _channelCounters[new(channelKey, userKey)] = new()
                {
                    Platform = channelKey.Platform,
                    ChannelId = channelKey.ChannelId,
                    UserId = user.UserId,
                    MessageCount = user.MessageCount,
                    BonusPoints = user.BonusPoints,
                    PenaltyPoints = user.PenaltyPoints,
                    FirstSeen = user.FirstSeen,
                    LastSeen = user.LastSeen,
                };
            }

            _hasChanges = false;
        }
    }

    internal void ReplaceCatalog(UserStatisticsCatalogDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        lock (_lock)
        {
            _accounts.Clear();
            _channelCounters.Clear();
            _unattributedCounters.Clear();
            _linkGroups.Clear();
            _keyToGroupId.Clear();
            _migration = document.Migration is null
                ? null
                : new()
                {
                    Version = document.Migration.Version,
                    CompletedAtUtc = document.Migration.CompletedAtUtc,
                    MigratedPlatforms = [.. document.Migration.MigratedPlatforms],
                };

            foreach (var account in document.Accounts)
            {
                if (!Enum.IsDefined(account.Platform) || string.IsNullOrWhiteSpace(account.UserId))
                {
                    continue;
                }

                _accounts[account.Key] = account.Clone();
            }

            foreach (var counter in document.ChannelCounters)
            {
                if (!Enum.IsDefined(counter.Platform)
                    || string.IsNullOrWhiteSpace(counter.ChannelId)
                    || string.IsNullOrWhiteSpace(counter.UserId))
                {
                    continue;
                }

                _channelCounters[counter.Key] = counter.Clone();
                UpsertAccountSeedLocked(counter.UserKey, counter.UserId, counter.FirstSeen, counter.LastSeen);
            }

            foreach (var counter in document.UnattributedCounters)
            {
                if (!Enum.IsDefined(counter.Platform) || string.IsNullOrWhiteSpace(counter.UserId))
                {
                    continue;
                }

                _unattributedCounters[counter.UserKey] = counter.Clone();
                UpsertAccountSeedLocked(counter.UserKey, counter.UserId, counter.FirstSeen, counter.LastSeen);
            }

            foreach (var group in document.AccountLinks)
            {
                if (group.GroupId == Guid.Empty)
                {
                    continue;
                }

                var validMembers = new List<AccountLinkMemberRecord>();
                foreach (var member in group.Members)
                {
                    if (!Enum.IsDefined(member.Platform) || string.IsNullOrWhiteSpace(member.UserId))
                    {
                        continue;
                    }

                    var key = member.Key;
                    if (_keyToGroupId.ContainsKey(key) || validMembers.Any(m => m.Key == key))
                    {
                        continue;
                    }

                    validMembers.Add(member.Clone());
                }

                if (validMembers.Count < 2)
                {
                    continue;
                }

                var normalizedGroup = new AccountLinkGroupRecord
                {
                    GroupId = group.GroupId,
                    CreatedAtUtc = group.CreatedAtUtc,
                    Members = validMembers,
                };

                _linkGroups[normalizedGroup.GroupId] = normalizedGroup;
                foreach (var member in validMembers)
                {
                    _keyToGroupId[member.Key] = normalizedGroup.GroupId;
                }
            }

            _hasChanges = false;
        }
    }

    internal UserStatisticsCatalogDocument CreateSnapshotAndMarkSaved()
    {
        lock (_lock)
        {
            var snapshot = new UserStatisticsCatalogDocument
            {
                SchemaVersion = UserStatisticsCatalogDocument.CurrentSchemaVersion,
                Migration = _migration is null
                    ? null
                    : new()
                    {
                        Version = _migration.Version,
                        CompletedAtUtc = _migration.CompletedAtUtc,
                        MigratedPlatforms = [.. _migration.MigratedPlatforms],
                    },
                Accounts = [.. _accounts.Values.Select(static a => a.Clone())],
                ChannelCounters = [.. _channelCounters.Values.Select(static c => c.Clone())],
                UnattributedCounters = [.. _unattributedCounters.Values.Select(static u => u.Clone())],
                AccountLinks = [.. _linkGroups.Values.Select(static g => g.Clone())],
            };

            _hasChanges = false;
            return snapshot;
        }
    }

    internal void MarkChanged()
    {
        lock (_lock)
        {
            _hasChanges = true;
        }
    }

    private ViewerAccountRecord UpsertAccountLocked(PlatformUserKey key, string username, DateTime now)
    {
        if (_accounts.TryGetValue(key, out var account))
        {
            account.UpdateName(username);
            return account;
        }

        account = new()
        {
            Platform = key.Platform,
            UserId = key.UserId,
            Name = username,
            FirstSeen = now,
            LastSeen = now,
        };
        _accounts[key] = account;
        logger.LogInformation(
            "Зарегистрирован новый пользователь в статистике: {Platform} {UserId}",
            key.Platform,
            key.UserId);
        return account;
    }

    private void UpsertAccountSeedLocked(PlatformUserKey key, string fallbackName, DateTime firstSeen, DateTime lastSeen)
    {
        if (_accounts.TryGetValue(key, out var existing))
        {
            if (firstSeen != default && (existing.FirstSeen == default || firstSeen < existing.FirstSeen))
            {
                existing.FirstSeen = firstSeen;
            }

            if (lastSeen > existing.LastSeen)
            {
                existing.LastSeen = lastSeen;
            }

            return;
        }

        _accounts[key] = new()
        {
            Platform = key.Platform,
            UserId = key.UserId,
            Name = fallbackName,
            FirstSeen = firstSeen,
            LastSeen = lastSeen,
        };
    }

    private bool AdjustAnyTargetPoints(PlatformUserKey key, ulong delta, bool isBonus)
    {
        if (delta == 0)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        lock (_lock)
        {
            if (_unattributedCounters.TryGetValue(key, out var unattributed))
            {
                ApplyDelta(unattributed, delta, isBonus, now);
                if (_accounts.TryGetValue(key, out var acc))
                {
                    acc.LastSeen = now;
                }

                _hasChanges = true;
                return true;
            }

            var latestChannelCounter = _channelCounters.Values
                .Where(c => c.UserKey == key)
                .OrderByDescending(static c => c.LastSeen)
                .FirstOrDefault();

            if (latestChannelCounter is not null)
            {
                ApplyDelta(latestChannelCounter, delta, isBonus, now);
                if (_accounts.TryGetValue(key, out var acc))
                {
                    acc.LastSeen = now;
                }

                _hasChanges = true;
                return true;
            }

            logger.LogWarning(
                "Попытка обновить очки для неизвестного пользователя {Platform} {UserId}. Операция пропущена",
                key.Platform,
                key.UserId);
            return false;
        }
    }

    private bool AdjustTargetPoints(
        StatisticsCounterTarget target,
        PlatformUserKey key,
        ulong delta,
        bool isBonus,
        string? fallbackUserName)
    {
        if (target.Kind == StatisticsCounterTargetKind.None)
        {
            throw new ArgumentException("Целевой срез счётчика не задан.", nameof(target));
        }

        if (target.Platform != key.Platform)
        {
            throw new ArgumentException("Площадка цели должна совпадать с площадкой аккаунта.", nameof(target));
        }

        if (delta == 0)
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;

        lock (_lock)
        {
            var hasAccount = _accounts.TryGetValue(key, out var account);
            if (!hasAccount && string.IsNullOrWhiteSpace(fallbackUserName))
            {
                logger.LogWarning(
                    "Попытка обновить очки для неизвестного пользователя {Platform} {UserId}. Операция пропущена",
                    key.Platform,
                    key.UserId);
                return false;
            }

            if (target.Kind == StatisticsCounterTargetKind.Channel)
            {
                var compositeKey = new ChannelUserKey(target.AsChannelKey!.Value, key);
                if (!_channelCounters.TryGetValue(compositeKey, out var counter))
                {
                    counter = new()
                    {
                        Platform = key.Platform,
                        ChannelId = compositeKey.Channel.ChannelId,
                        UserId = key.UserId,
                        FirstSeen = now,
                        LastSeen = now,
                    };
                    _channelCounters[compositeKey] = counter;
                }

                ApplyDelta(counter, delta, isBonus, now);
            }
            else
            {
                if (!_unattributedCounters.TryGetValue(key, out var counter))
                {
                    counter = new()
                    {
                        Platform = key.Platform,
                        UserId = key.UserId,
                        FirstSeen = now,
                        LastSeen = now,
                    };
                    _unattributedCounters[key] = counter;
                }

                ApplyDelta(counter, delta, isBonus, now);
            }

            if (hasAccount)
            {
                account!.LastSeen = now;
            }
            else
            {
                UpsertAccountLocked(key, fallbackUserName!, now);
            }

            _hasChanges = true;
            return true;
        }
    }

    private static void ApplyDelta(ChannelUserCounterRecord counter, ulong delta, bool isBonus, DateTime now)
    {
        if (isBonus)
        {
            counter.BonusPoints += delta;
        }
        else
        {
            counter.PenaltyPoints += delta;
        }

        counter.LastSeen = now;
    }

    private static void ApplyDelta(UnattributedUserCounterRecord counter, ulong delta, bool isBonus, DateTime now)
    {
        if (isBonus)
        {
            counter.BonusPoints += delta;
        }
        else
        {
            counter.PenaltyPoints += delta;
        }

        counter.LastSeen = now;
    }

    private List<UserStatistics> GetPlatformUsersLocked(StreamingPlatform platform)
    {
        var builders = new Dictionary<string, (ViewerAccountRecord? Account, ulong Messages, ulong Bonus, ulong Penalty, DateTime FirstSeen, DateTime LastSeen)>(StringComparer.Ordinal);

        void Merge(string userId, ulong messages, ulong bonus, ulong penalty, DateTime firstSeen, DateTime lastSeen)
        {
            var key = new PlatformUserKey(platform, userId);
            _accounts.TryGetValue(key, out var account);

            if (!builders.TryGetValue(userId, out var current))
            {
                builders[userId] = (account, messages, bonus, penalty, firstSeen, lastSeen);
                return;
            }

            builders[userId] = (
                current.Account ?? account,
                current.Messages + messages,
                current.Bonus + bonus,
                current.Penalty + penalty,
                MinDate(current.FirstSeen, firstSeen),
                MaxDate(current.LastSeen, lastSeen));
        }

        foreach (var counter in _channelCounters.Values)
        {
            if (counter.Platform == platform)
            {
                Merge(counter.UserId, counter.MessageCount, counter.BonusPoints, counter.PenaltyPoints, counter.FirstSeen, counter.LastSeen);
            }
        }

        foreach (var counter in _unattributedCounters.Values)
        {
            if (counter.Platform == platform)
            {
                Merge(counter.UserId, counter.MessageCount, counter.BonusPoints, counter.PenaltyPoints, counter.FirstSeen, counter.LastSeen);
            }
        }

        var result = new List<UserStatistics>(builders.Count);
        foreach (var (userId, data) in builders)
        {
            result.Add(new()
            {
                Platform = platform,
                UserId = userId,
                Name = data.Account?.Name ?? userId,
                MessageCount = data.Messages,
                BonusPoints = data.Bonus,
                PenaltyPoints = data.Penalty,
                FirstSeen = data.FirstSeen,
                LastSeen = data.LastSeen,
            });
        }

        return result;
    }

    private ViewerAggregateStatistics? FindChannelViewerLocked(
        StatisticsSlice slice,
        ChannelKey channel,
        ViewerIdentityKey identity)
    {
        IEnumerable<PlatformUserKey> members = identity.Kind == ViewerIdentityKind.LinkedGroup
            ? _linkGroups[identity.GroupId].Members.Select(static member => member.Key)
            : [identity.StandaloneUser];
        ViewerAggregateBuilder? builder = null;

        foreach (var member in members)
        {
            if (member.Platform == channel.Platform && _channelCounters.TryGetValue(new(channel, member), out var counter))
            {
                (builder ??= new(identity)).AddBreakdown(counter);
            }
        }

        return builder?.Build(slice, _accounts, _linkGroups);
    }

    private List<ViewerAggregateStatistics> QuerySliceLocked(StatisticsSlice slice, ViewerIdentityKey? filter = null)
    {
        var builders = new Dictionary<ViewerIdentityKey, ViewerAggregateBuilder>();

        foreach (var counter in _channelCounters.Values)
        {
            if (!MatchesSlice(slice, counter.ChannelKey))
            {
                continue;
            }

            var identity = ResolveIdentityLocked(counter.UserKey);
            if (filter.HasValue && identity != filter.Value)
            {
                continue;
            }

            if (!builders.TryGetValue(identity, out var builder))
            {
                builder = new(identity);
                builders[identity] = builder;
            }

            builder.AddBreakdown(counter);
        }

        foreach (var counter in _unattributedCounters.Values)
        {
            if (!MatchesUnattributedSlice(slice, counter.Platform))
            {
                continue;
            }

            var identity = ResolveIdentityLocked(counter.UserKey);
            if (filter.HasValue && identity != filter.Value)
            {
                continue;
            }

            if (!builders.TryGetValue(identity, out var builder))
            {
                builder = new(identity);
                builders[identity] = builder;
            }

            builder.AddBreakdown(
                counter.UserKey,
                StatisticsCounterTarget.ForUnattributed(counter.Platform),
                counter.MessageCount,
                counter.BonusPoints,
                counter.PenaltyPoints,
                counter.FirstSeen,
                counter.LastSeen);
        }

        var results = new List<ViewerAggregateStatistics>(builders.Count);
        foreach (var builder in builders.Values)
        {
            results.Add(builder.Build(slice, _accounts, _linkGroups));
        }

        return results;
    }

    private ViewerIdentityKey ResolveIdentityLocked(PlatformUserKey userKey)
    {
        return _keyToGroupId.TryGetValue(userKey, out var groupId) && _linkGroups.ContainsKey(groupId)
            ? ViewerIdentityKey.FromGroup(groupId)
            : ViewerIdentityKey.FromUser(userKey);
    }

    private static bool MatchesSlice(StatisticsSlice slice, ChannelKey channel)
    {
        return slice.Kind switch
        {
            StatisticsSliceKind.Channel => slice.Channel == channel,
            StatisticsSliceKind.ChannelGroup => slice.GroupChannels.Contains(channel),
            StatisticsSliceKind.AllChannels => true,
            StatisticsSliceKind.Platform => slice.Platform == channel.Platform,
            _ => false,
        };
    }

    private static bool MatchesUnattributedSlice(StatisticsSlice slice, StreamingPlatform platform)
    {
        return slice.Kind switch
        {
            StatisticsSliceKind.AllChannels => true,
            StatisticsSliceKind.Platform => slice.Platform == platform,
            StatisticsSliceKind.UnattributedPlatform => !slice.Platform.HasValue || slice.Platform.Value == platform,
            _ => false,
        };
    }

    private static DateTime MinDate(DateTime first, DateTime second)
    {
        if (first == default)
        {
            return second;
        }

        if (second == default)
        {
            return first;
        }

        return first < second ? first : second;
    }

    private static DateTime MaxDate(DateTime first, DateTime second) => first > second ? first : second;

    private static void ValidateTransferRequest(PointsTransferRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.Source.IsValid)
        {
            throw new ArgumentException("Источник переноса баллов не задан.", nameof(request));
        }

        if (!request.Target.IsValid)
        {
            throw new ArgumentException("Целевой канал переноса баллов не задан.", nameof(request));
        }

        if (!Enum.IsDefined(request.Mode) || request.Mode == PointsTransferMode.None)
        {
            throw new ArgumentException("Режим переноса баллов не задан.", nameof(request));
        }

        if (request.Source.MatchesTarget(request.Target))
        {
            throw new ArgumentException("Источник и целевой канал не могут совпадать.", nameof(request));
        }
    }

    private TransferPlan BuildTransferPlanLocked(PointsTransferRequest request)
    {
        HashSet<PlatformUserKey>? filter = request.SourceUserKeys is null
            ? null
            : [.. request.SourceUserKeys];

        var snapshots = CollectSourceSnapshotsLocked(request.Source, filter);
        var entries = new List<TransferPlanEntry>(snapshots.Count);
        var pendingTargetTotals = new Dictionary<PlatformUserKey, (ulong Messages, ulong Bonus, ulong Penalty)>();

        var transferredCount = 0;
        var skippedCount = 0;
        var missingLinkSkippedCount = 0;
        var ambiguousLinkSkippedCount = 0;
        ulong totalMessages = 0;
        ulong totalBonus = 0;
        ulong totalPenalty = 0;

        foreach (var snapshot in snapshots
            .OrderByDescending(static s => PointsTransferMath.ComputeSignedPoints(s.MessageCount, s.BonusPoints, s.PenaltyPoints))
            .ThenBy(s => ResolveAccountNameLocked(s.UserKey), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static s => s.UserKey.UserId, StringComparer.Ordinal))
        {
            var sourceName = ResolveAccountNameLocked(snapshot.UserKey);
            var (targetUserKey, linkSkipReason) = ResolveTransferTargetLocked(snapshot.UserKey, request.Source.Platform, request.Target.Platform);

            var skipReason = linkSkipReason;
            var requestedMessages = request.Mode == PointsTransferMode.AllCounters ? snapshot.MessageCount : 0UL;
            var transferredMessages = requestedMessages;
            var transferredBonus = snapshot.BonusPoints;
            var transferredPenalty = snapshot.PenaltyPoints;

            if (skipReason == PointsTransferSkipReason.None && targetUserKey is { } resolvedTargetKey)
            {
                if (!pendingTargetTotals.TryGetValue(resolvedTargetKey, out var currentTarget))
                {
                    if (_channelCounters.TryGetValue(new ChannelUserKey(request.Target, resolvedTargetKey), out var existingTarget))
                    {
                        currentTarget = (existingTarget.MessageCount, existingTarget.BonusPoints, existingTarget.PenaltyPoints);
                    }
                    else
                    {
                        currentTarget = (0UL, 0UL, 0UL);
                    }
                }

                transferredMessages = Math.Min(requestedMessages, ulong.MaxValue - currentTarget.Messages);
                transferredBonus = Math.Min(snapshot.BonusPoints, ulong.MaxValue - currentTarget.Bonus);
                transferredPenalty = Math.Min(snapshot.PenaltyPoints, ulong.MaxValue - currentTarget.Penalty);

                if (transferredMessages == 0 && transferredBonus == 0 && transferredPenalty == 0)
                {
                    skipReason = PointsTransferSkipReason.NothingToTransfer;
                }
                else
                {
                    pendingTargetTotals[resolvedTargetKey] = (
                        currentTarget.Messages + transferredMessages,
                        currentTarget.Bonus + transferredBonus,
                        currentTarget.Penalty + transferredPenalty);
                }
            }

            var targetName = targetUserKey is { } resolvedTarget ? ResolveAccountNameLocked(resolvedTarget) : null;

            var preview = new PointsTransferUserPreview(
                snapshot.UserKey,
                sourceName,
                targetUserKey,
                targetName,
                transferredMessages,
                transferredBonus,
                transferredPenalty,
                skipReason);

            entries.Add(new(snapshot.UserKey, preview));

            if (preview.WillTransfer)
            {
                transferredCount++;
                totalMessages = PointsTransferMath.AddSaturating(totalMessages, transferredMessages);
                totalBonus = PointsTransferMath.AddSaturating(totalBonus, transferredBonus);
                totalPenalty = PointsTransferMath.AddSaturating(totalPenalty, transferredPenalty);
            }
            else
            {
                skippedCount++;
                if (skipReason == PointsTransferSkipReason.MissingTargetAccountLink)
                {
                    missingLinkSkippedCount++;
                }
                else if (skipReason == PointsTransferSkipReason.AmbiguousTargetAccountLink)
                {
                    ambiguousLinkSkippedCount++;
                }
            }
        }

        var totalPoints = PointsTransferMath.ComputeSignedPoints(totalMessages, totalBonus, totalPenalty);
        return new(
            request.Source,
            request.Target,
            request.Mode,
            entries,
            transferredCount,
            skippedCount,
            missingLinkSkippedCount,
            ambiguousLinkSkippedCount,
            totalMessages,
            totalBonus,
            totalPenalty,
            totalPoints);
    }

    private List<SourceCounterSnapshot> CollectSourceSnapshotsLocked(
        PointsTransferSource source,
        HashSet<PlatformUserKey>? filter)
    {
        var list = new List<SourceCounterSnapshot>();

        if (source.Kind == PointsTransferSourceKind.UnattributedPlatform)
        {
            foreach (var counter in _unattributedCounters.Values)
            {
                if (counter.Platform != source.Platform)
                {
                    continue;
                }

                var userKey = counter.UserKey;
                if (filter is not null && !filter.Contains(userKey))
                {
                    continue;
                }

                list.Add(new(userKey, counter.MessageCount, counter.BonusPoints, counter.PenaltyPoints, counter.FirstSeen, counter.LastSeen));
            }

            return list;
        }

        var sourceChannel = source.AsChannelKey!.Value;
        foreach (var counter in _channelCounters.Values)
        {
            if (counter.ChannelKey != sourceChannel)
            {
                continue;
            }

            var userKey = counter.UserKey;
            if (filter is not null && !filter.Contains(userKey))
            {
                continue;
            }

            list.Add(new(userKey, counter.MessageCount, counter.BonusPoints, counter.PenaltyPoints, counter.FirstSeen, counter.LastSeen));
        }

        return list;
    }

    private (PlatformUserKey? TargetUserKey, PointsTransferSkipReason SkipReason) ResolveTransferTargetLocked(
        PlatformUserKey sourceUserKey,
        StreamingPlatform sourcePlatform,
        StreamingPlatform targetPlatform)
    {
        if (sourcePlatform == targetPlatform)
        {
            return (sourceUserKey, PointsTransferSkipReason.None);
        }

        if (!_keyToGroupId.TryGetValue(sourceUserKey, out var groupId)
            || !_linkGroups.TryGetValue(groupId, out var group))
        {
            return (null, PointsTransferSkipReason.MissingTargetAccountLink);
        }

        PlatformUserKey? matched = null;
        var count = 0;
        foreach (var member in group.Members)
        {
            if (member.Platform == targetPlatform)
            {
                matched = member.Key;
                count++;
                if (count > 1)
                {
                    return (null, PointsTransferSkipReason.AmbiguousTargetAccountLink);
                }
            }
        }

        return count == 1
            ? (matched, PointsTransferSkipReason.None)
            : (null, PointsTransferSkipReason.MissingTargetAccountLink);
    }

    private string ResolveAccountNameLocked(PlatformUserKey key)
    {
        return _accounts.TryGetValue(key, out var account) && !string.IsNullOrWhiteSpace(account.Name)
            ? account.Name
            : key.UserId;
    }

    private void ApplyTransferEntryLocked(
        PointsTransferRequest request,
        PlatformUserKey sourceUserKey,
        PlatformUserKey targetUserKey,
        PointsTransferUserPreview preview,
        DateTime now)
    {
        var movedMessages = preview.MessageCount;
        var movedBonus = preview.BonusPoints;
        var movedPenalty = preview.PenaltyPoints;
        DateTime sourceFirstSeen;
        DateTime sourceLastSeen;

        if (request.Source.Kind == PointsTransferSourceKind.UnattributedPlatform)
        {
            if (!_unattributedCounters.TryGetValue(sourceUserKey, out var sourceCounter))
            {
                return;
            }

            sourceFirstSeen = sourceCounter.FirstSeen;
            sourceLastSeen = sourceCounter.LastSeen;
            sourceCounter.MessageCount -= Math.Min(sourceCounter.MessageCount, movedMessages);
            sourceCounter.BonusPoints -= Math.Min(sourceCounter.BonusPoints, movedBonus);
            sourceCounter.PenaltyPoints -= Math.Min(sourceCounter.PenaltyPoints, movedPenalty);

            if (sourceCounter.MessageCount == 0 && sourceCounter.BonusPoints == 0 && sourceCounter.PenaltyPoints == 0)
            {
                _unattributedCounters.Remove(sourceUserKey);
            }
        }
        else
        {
            var sourceCompositeKey = new ChannelUserKey(request.Source.AsChannelKey!.Value, sourceUserKey);
            if (!_channelCounters.TryGetValue(sourceCompositeKey, out var sourceCounter))
            {
                return;
            }

            sourceFirstSeen = sourceCounter.FirstSeen;
            sourceLastSeen = sourceCounter.LastSeen;
            sourceCounter.MessageCount -= Math.Min(sourceCounter.MessageCount, movedMessages);
            sourceCounter.BonusPoints -= Math.Min(sourceCounter.BonusPoints, movedBonus);
            sourceCounter.PenaltyPoints -= Math.Min(sourceCounter.PenaltyPoints, movedPenalty);

            if (sourceCounter.MessageCount == 0 && sourceCounter.BonusPoints == 0 && sourceCounter.PenaltyPoints == 0)
            {
                _channelCounters.Remove(sourceCompositeKey);
            }
        }

        var targetCompositeKey = new ChannelUserKey(request.Target, targetUserKey);
        if (!_channelCounters.TryGetValue(targetCompositeKey, out var targetCounter))
        {
            targetCounter = new()
            {
                Platform = request.Target.Platform,
                ChannelId = request.Target.ChannelId,
                UserId = targetUserKey.UserId,
                FirstSeen = sourceFirstSeen == default ? now : sourceFirstSeen,
                LastSeen = sourceLastSeen == default ? now : sourceLastSeen,
            };
            _channelCounters[targetCompositeKey] = targetCounter;
        }
        else if (request.Mode == PointsTransferMode.AllCounters && movedMessages > 0)
        {
            targetCounter.FirstSeen = MinDate(targetCounter.FirstSeen, sourceFirstSeen);
            targetCounter.LastSeen = MaxDate(targetCounter.LastSeen, sourceLastSeen);
        }

        if (movedMessages > 0)
        {
            targetCounter.MessageCount = PointsTransferMath.AddSaturating(targetCounter.MessageCount, movedMessages);
        }

        targetCounter.BonusPoints = PointsTransferMath.AddSaturating(targetCounter.BonusPoints, movedBonus);
        targetCounter.PenaltyPoints = PointsTransferMath.AddSaturating(targetCounter.PenaltyPoints, movedPenalty);

        UpsertAccountSeedLocked(
            targetUserKey,
            string.IsNullOrWhiteSpace(preview.TargetUserName) ? targetUserKey.UserId : preview.TargetUserName,
            targetCounter.FirstSeen,
            targetCounter.LastSeen);
    }

    private readonly record struct SourceCounterSnapshot(
        PlatformUserKey UserKey,
        ulong MessageCount,
        ulong BonusPoints,
        ulong PenaltyPoints,
        DateTime FirstSeen,
        DateTime LastSeen);

    private sealed record TransferPlanEntry(
        PlatformUserKey SourceUserKey,
        PointsTransferUserPreview Preview);

    private sealed record TransferPlan(
        PointsTransferSource Source,
        ChannelKey Target,
        PointsTransferMode Mode,
        List<TransferPlanEntry> Entries,
        int TransferredCount,
        int SkippedCount,
        int MissingLinkSkippedCount,
        int AmbiguousLinkSkippedCount,
        ulong TotalMessages,
        ulong TotalBonusPoints,
        ulong TotalPenaltyPoints,
        long TotalPoints)
    {
        public PointsTransferPreview ToPreview()
        {
            return new(
                Source,
                Target,
                Mode,
                Entries.Select(static e => e.Preview).ToList(),
                TransferredCount,
                SkippedCount,
                TotalMessages,
                TotalBonusPoints,
                TotalPenaltyPoints,
                TotalPoints);
        }

        public PointsTransferResult ToResult()
        {
            return new(
                Source,
                Target,
                Mode,
                TransferredCount,
                SkippedCount,
                MissingLinkSkippedCount,
                AmbiguousLinkSkippedCount,
                TotalMessages,
                TotalBonusPoints,
                TotalPenaltyPoints,
                TotalPoints);
        }
    }

    private sealed class ViewerAggregateBuilder(ViewerIdentityKey identityKey)
    {
        private readonly Dictionary<PlatformUserKey, DateTime> _contributingUsers = [];
        private readonly List<ViewerChannelBreakdown> _breakdowns = [];
        private ulong _messages;
        private ulong _bonus;
        private ulong _penalty;
        private DateTime _firstSeen;
        private DateTime _lastSeen;

        public void AddBreakdown(ChannelUserCounterRecord counter)
        {
            AddBreakdown(
                counter.UserKey,
                StatisticsCounterTarget.ForChannel(counter.ChannelKey),
                counter.MessageCount,
                counter.BonusPoints,
                counter.PenaltyPoints,
                counter.FirstSeen,
                counter.LastSeen);
        }

        public void AddBreakdown(
            PlatformUserKey userKey,
            StatisticsCounterTarget target,
            ulong messages,
            ulong bonus,
            ulong penalty,
            DateTime firstSeen,
            DateTime lastSeen)
        {
            _messages += messages;
            _bonus += bonus;
            _penalty += penalty;
            _firstSeen = MinDate(_firstSeen, firstSeen);
            _lastSeen = MaxDate(_lastSeen, lastSeen);

            if (_contributingUsers.TryGetValue(userKey, out var existingLastSeen))
            {
                _contributingUsers[userKey] = MaxDate(existingLastSeen, lastSeen);
            }
            else
            {
                _contributingUsers[userKey] = lastSeen;
            }

            _breakdowns.Add(new(target, messages, bonus, penalty, firstSeen, lastSeen));
        }

        public ViewerAggregateStatistics Build(
            StatisticsSlice slice,
            Dictionary<PlatformUserKey, ViewerAccountRecord> accounts,
            Dictionary<Guid, AccountLinkGroupRecord> linkGroups)
        {
            Guid? linkGroupId = identityKey.Kind == ViewerIdentityKind.LinkedGroup ? identityKey.GroupId : null;
            AccountLinkGroupRecord? group = null;
            if (linkGroupId.HasValue)
            {
                linkGroups.TryGetValue(linkGroupId.Value, out group);
            }

            var memberKeys = group is not null
                ? group.Members.Select(static m => m.Key).ToList()
                : [identityKey.StandaloneUser];

            var memberByKey = group?.Members.ToDictionary(static m => m.Key) ?? [];
            var accountInfos = new List<ViewerAccountInfo>(memberKeys.Count);

            foreach (var key in memberKeys)
            {
                accounts.TryGetValue(key, out var account);
                memberByKey.TryGetValue(key, out var linkMember);

                accountInfos.Add(new(
                    key,
                    string.IsNullOrWhiteSpace(account?.Name) ? key.UserId : account.Name,
                    account?.FirstSeen ?? default,
                    account?.LastSeen ?? default,
                    linkMember?.Origin ?? AccountLinkOrigin.None,
                    linkMember?.Author,
                    linkMember?.LinkedAtUtc));
            }

            var orderedAccounts = accountInfos
                .OrderByDescending(info => _contributingUsers.ContainsKey(info.Key))
                .ThenByDescending(info => slice.Platform.HasValue && info.Key.Platform == slice.Platform.Value)
                .ThenByDescending(info => _contributingUsers.GetValueOrDefault(info.Key, info.LastSeen))
                .ThenBy(static info => info.Key.Platform)
                .ThenBy(static info => info.Key.UserId, StringComparer.Ordinal)
                .ToList();

            var primary = orderedAccounts[0];
            var orderedBreakdowns = _breakdowns
                .OrderByDescending(static b => b.Points)
                .ThenByDescending(static b => b.LastSeen)
                .ToList();

            return new(
                identityKey,
                primary.Key,
                primary.Name,
                _messages,
                _bonus,
                _penalty,
                _firstSeen,
                _lastSeen,
                orderedAccounts,
                orderedBreakdowns,
                linkGroupId);
        }
    }

    private sealed class PlatformSliceView(UserStatisticsRepository repository, StreamingPlatform platform) : IPlatformUserStatistics
    {
        public StreamingPlatform Platform => platform;

        public UserStatistics? GetById(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            lock (repository._lock)
            {
                return repository.GetPlatformUsersLocked(platform)
                    .FirstOrDefault(u => string.Equals(u.UserId, userId, StringComparison.Ordinal));
            }
        }

        public UserStatistics? GetByName(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return null;
            }

            var target = username.TrimStart('@');
            lock (repository._lock)
            {
                return repository.GetPlatformUsersLocked(platform)
                    .FirstOrDefault(u => string.Equals(u.Name, target, StringComparison.OrdinalIgnoreCase));
            }
        }

        public IReadOnlyList<UserStatistics> GetAll()
        {
            lock (repository._lock)
            {
                return repository.GetPlatformUsersLocked(platform);
            }
        }

        public IReadOnlyList<UserStatistics> GetTop(int count, UserTopMode mode = UserTopMode.Points)
        {
            if (count <= 0)
            {
                return [];
            }

            lock (repository._lock)
            {
                var users = repository.GetPlatformUsersLocked(platform);
                IEnumerable<UserStatistics> ordered = mode == UserTopMode.Messages
                    ? users.OrderByDescending(static x => x.MessageCount)
                    : users.OrderByDescending(static x => x.Points);

                return [.. ordered.Take(count)];
            }
        }
    }
}
