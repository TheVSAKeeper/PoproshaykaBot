using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

internal sealed class LegacyUserStatisticsMigrator(
    StatisticsFileStore fileStore,
    KnownChannelIdentityResolver identityResolver,
    ILogger<LegacyUserStatisticsMigrator> logger,
    BroadcastHistoryStore? broadcastHistoryStore = null,
    ActiveBroadcastStore? activeBroadcastStore = null,
    TimeProvider? timeProvider = null)
{
    private static readonly TimeSpan PreStreamChatGrace = TimeSpan.FromHours(24);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<LegacyMigrationOutcome> TryMigrateAsync(CancellationToken cancellationToken)
    {
        var existingDescriptors = UserStatisticsPlatformCatalog.All
            .Where(descriptor => fileStore.FileExists(descriptor.RelativePath))
            .ToList();

        if (existingDescriptors.Count == 0)
        {
            return LegacyMigrationOutcome.NoLegacyFiles;
        }

        var loadedByPlatform = new Dictionary<StreamingPlatform, List<UserStatistics>>();

        foreach (var descriptor in existingDescriptors)
        {
            var readResult = await fileStore.LoadUsersAsync(descriptor.Platform, cancellationToken).ConfigureAwait(false);
            if (readResult.Failed)
            {
                logger.LogError(
                    "Однократная миграция статистики прервана: старый файл {RelativePath} повреждён; каталог не создаётся",
                    descriptor.RelativePath);
                return LegacyMigrationOutcome.Failed;
            }

            loadedByPlatform[descriptor.Platform] = readResult.Value ?? [];
        }

        var history = ReadHistorySafely();
        var activeBroadcast = ReadActiveBroadcastSafely();
        var identities = identityResolver.BuildSnapshot(history, activeBroadcast);
        var platformHistories = BuildPlatformHistories(history, activeBroadcast, identities);

        var document = new UserStatisticsCatalogDocument
        {
            SchemaVersion = UserStatisticsCatalogDocument.CurrentSchemaVersion,
            Migration = new()
            {
                Version = UserStatisticsCatalogDocument.CurrentMigrationVersion,
                CompletedAtUtc = _timeProvider.GetUtcNow(),
                MigratedPlatforms = [.. loadedByPlatform.Keys.OrderBy(static p => p)],
            },
        };

        var inputByKey = new Dictionary<PlatformUserKey, UserStatistics>();

        foreach (var (platform, rawUsers) in loadedByPlatform)
        {
            var deduplicated = DeduplicateUsers(platform, rawUsers);
            platformHistories.TryGetValue(platform, out var platformHistory);
            var singleConfiguredChannel = identities.GetSingleConfiguredChannel(platform);

            foreach (var (userKey, user, syntheticNameOnly) in deduplicated)
            {
                inputByKey[userKey] = user;

                document.Accounts.Add(new()
                {
                    Platform = platform,
                    UserId = userKey.UserId,
                    Name = user.Name,
                    FirstSeen = user.FirstSeen,
                    LastSeen = user.LastSeen,
                });

                ChannelKey? targetChannel = null;
                if (!syntheticNameOnly && platformHistory is not null)
                {
                    targetChannel = ResolveTargetChannel(user, platformHistory, singleConfiguredChannel);
                }

                if (targetChannel is { } channelKey)
                {
                    document.ChannelCounters.Add(new()
                    {
                        Platform = platform,
                        ChannelId = channelKey.ChannelId,
                        UserId = userKey.UserId,
                        MessageCount = user.MessageCount,
                        BonusPoints = user.BonusPoints,
                        PenaltyPoints = user.PenaltyPoints,
                        FirstSeen = user.FirstSeen,
                        LastSeen = user.LastSeen,
                    });
                }
                else
                {
                    document.UnattributedCounters.Add(new()
                    {
                        Platform = platform,
                        UserId = userKey.UserId,
                        MessageCount = user.MessageCount,
                        BonusPoints = user.BonusPoints,
                        PenaltyPoints = user.PenaltyPoints,
                        FirstSeen = user.FirstSeen,
                        LastSeen = user.LastSeen,
                    });
                }
            }
        }

        VerifyConservation(inputByKey, document);

        await fileStore.SaveUserStatisticsCatalogAsync(document, cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Выполнена однократная миграция статистики: аккаунтов {Accounts}, канальных счётчиков {ChannelCounters}, без канала {Unattributed}",
            document.Accounts.Count,
            document.ChannelCounters.Count,
            document.UnattributedCounters.Count);

        return LegacyMigrationOutcome.Migrated(document);
    }

    private BroadcastHistoryDocument? ReadHistorySafely()
    {
        if (broadcastHistoryStore is null)
        {
            return null;
        }

        if (broadcastHistoryStore.SourceReadState == FileReadState.Failed)
        {
            logger.LogWarning("История эфиров повреждена – старая статистика переносится без привязки к каналам");
            return null;
        }

        return broadcastHistoryStore.Load();
    }

    private ActiveBroadcast? ReadActiveBroadcastSafely()
    {
        if (activeBroadcastStore is null)
        {
            return null;
        }

        if (activeBroadcastStore.ReadState == FileReadState.Failed)
        {
            logger.LogWarning("Черновик активного эфира повреждён – не учитывается при атрибуции каналов");
            return null;
        }

        return activeBroadcastStore.Load();
    }

    private static List<(PlatformUserKey Key, UserStatistics User, bool SyntheticNameOnly)> DeduplicateUsers(
        StreamingPlatform platform,
        List<UserStatistics> rawUsers)
    {
        var map = new Dictionary<PlatformUserKey, (UserStatistics User, bool SyntheticNameOnly)>();

        foreach (var raw in rawUsers)
        {
            var syntheticNameOnly = false;
            string userId;

            if (!string.IsNullOrWhiteSpace(raw.UserId))
            {
                userId = raw.UserId.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(raw.Name))
            {
                userId = $"legacy-name:{raw.Name.Trim().ToLowerInvariant()}";
                syntheticNameOnly = true;
            }
            else
            {
                continue;
            }

            var key = new PlatformUserKey(platform, userId);
            map[key] = (raw, syntheticNameOnly);
        }

        return [.. map.Select(pair => (pair.Key, pair.Value.User, pair.Value.SyntheticNameOnly))];
    }

    private static Dictionary<StreamingPlatform, PlatformHistoryIndex> BuildPlatformHistories(
        BroadcastHistoryDocument? history,
        ActiveBroadcast? activeBroadcast,
        KnownChannelIdentityResolver.ChannelIdentitySnapshot identities)
    {
        var result = new Dictionary<StreamingPlatform, PlatformHistoryIndex>();

        if (history is not null)
        {
            foreach (var broadcast in history.Broadcasts)
            {
                foreach (var entry in broadcast.Platforms)
                {
                    if (!Enum.IsDefined(entry.Platform))
                    {
                        continue;
                    }

                    var index = GetOrCreateIndex(result, entry.Platform);
                    var resolved = identities.Resolve(entry.Platform, entry.ChannelId, entry.Channel);
                    index.AddRecord(
                        resolved,
                        entry.StartedAt.UtcDateTime,
                        entry.Chatters.Select(static c => (c.UserId, c.MessageCount)));
                }
            }
        }

        if (activeBroadcast is not null)
        {
            foreach (var entry in activeBroadcast.Platforms)
            {
                if (!Enum.IsDefined(entry.Platform))
                {
                    continue;
                }

                var index = GetOrCreateIndex(result, entry.Platform);
                var resolved = identities.Resolve(entry.Platform, entry.ChannelId, entry.Channel);
                index.AddRecord(
                    resolved,
                    entry.StartedAt.UtcDateTime,
                    entry.Chatters.Select(static c => (c.UserId, c.MessageCount)));
            }
        }

        return result;
    }

    private static PlatformHistoryIndex GetOrCreateIndex(
        Dictionary<StreamingPlatform, PlatformHistoryIndex> result,
        StreamingPlatform platform)
    {
        if (!result.TryGetValue(platform, out var index))
        {
            index = new();
            result[platform] = index;
        }

        return index;
    }

    private static ChannelKey? ResolveTargetChannel(
        UserStatistics user,
        PlatformHistoryIndex history,
        ChannelKey? singleConfiguredChannel)
    {
        if (history.UserChannels.TryGetValue(user.UserId, out var activity))
        {
            // P1: 100 % сообщений пользователя из истории привязаны к единственному разрешённому каналу.
            if (!activity.HasUnresolved
                && activity.MessagesByChannel.Count == 1
                && user.MessageCount > 0)
            {
                var (channel, totalMessages) = activity.MessagesByChannel.Single();
                if (totalMessages == user.MessageCount)
                {
                    return channel;
                }
            }

            // P2: единственный доказанный канал площадки без доисторического хвоста.
            if (!history.HasUnresolvedRecord
                && history.SingleResolvedChannel is { } soleHistoryChannel
                && singleConfiguredChannel is { } configured
                && soleHistoryChannel == configured
                && !activity.HasUnresolved
                && activity.MessagesByChannel.ContainsKey(soleHistoryChannel)
                && history.EarliestStartedAtUtc is { } earliest
                && user.FirstSeen.ToUniversalTime() >= earliest - PreStreamChatGrace)
            {
                return soleHistoryChannel;
            }
        }

        // P3: недоказуемый или многоканальный итог сохраняется без канала.
        return null;
    }

    private static void VerifyConservation(
        Dictionary<PlatformUserKey, UserStatistics> inputByKey,
        UserStatisticsCatalogDocument document)
    {
        var totals = new Dictionary<PlatformUserKey, (ulong Messages, ulong Bonus, ulong Penalty)>();

        foreach (var counter in document.ChannelCounters)
        {
            AddTotal(totals, counter.UserKey, counter.MessageCount, counter.BonusPoints, counter.PenaltyPoints);
        }

        foreach (var counter in document.UnattributedCounters)
        {
            AddTotal(totals, counter.UserKey, counter.MessageCount, counter.BonusPoints, counter.PenaltyPoints);
        }

        foreach (var (key, expected) in inputByKey)
        {
            if (!totals.TryGetValue(key, out var actual)
                || actual.Messages != expected.MessageCount
                || actual.Bonus != expected.BonusPoints
                || actual.Penalty != expected.PenaltyPoints)
            {
                throw new InvalidOperationException(
                    $"Нарушен инвариант сохранения сумм при миграции для {key}.");
            }
        }
    }

    private static void AddTotal(
        Dictionary<PlatformUserKey, (ulong Messages, ulong Bonus, ulong Penalty)> totals,
        PlatformUserKey key,
        ulong messages,
        ulong bonus,
        ulong penalty)
    {
        totals.TryGetValue(key, out var current);
        totals[key] = (current.Messages + messages, current.Bonus + bonus, current.Penalty + penalty);
    }

    private sealed class PlatformHistoryIndex
    {
        private bool _hasMultipleResolved;

        public bool HasUnresolvedRecord { get; private set; }
        public ChannelKey? SingleResolvedChannel { get; private set; }
        public DateTime? EarliestStartedAtUtc { get; private set; }
        public Dictionary<string, UserHistoryActivity> UserChannels { get; } = new(StringComparer.Ordinal);

        public void AddRecord(
            ChannelKey? resolvedChannel,
            DateTime startedAtUtc,
            IEnumerable<(string UserId, long MessageCount)> chatters)
        {
            if (EarliestStartedAtUtc is null || startedAtUtc < EarliestStartedAtUtc.Value)
            {
                EarliestStartedAtUtc = startedAtUtc;
            }

            if (resolvedChannel is null)
            {
                HasUnresolvedRecord = true;
            }
            else if (!_hasMultipleResolved)
            {
                if (SingleResolvedChannel is null)
                {
                    SingleResolvedChannel = resolvedChannel;
                }
                else if (SingleResolvedChannel.Value != resolvedChannel.Value)
                {
                    SingleResolvedChannel = null;
                    _hasMultipleResolved = true;
                }
            }

            foreach (var (userId, messageCount) in chatters)
            {
                if (string.IsNullOrWhiteSpace(userId))
                {
                    continue;
                }

                if (!UserChannels.TryGetValue(userId, out var activity))
                {
                    activity = new();
                    UserChannels[userId] = activity;
                }

                if (resolvedChannel is null)
                {
                    activity.HasUnresolved = true;
                }
                else
                {
                    activity.MessagesByChannel.TryGetValue(resolvedChannel.Value, out var existing);
                    var delta = messageCount > 0 ? (ulong)messageCount : 0UL;
                    activity.MessagesByChannel[resolvedChannel.Value] = existing + delta;
                }
            }
        }
    }

    private sealed class UserHistoryActivity
    {
        public bool HasUnresolved { get; set; }
        public Dictionary<ChannelKey, ulong> MessagesByChannel { get; } = [];
    }
}

internal enum LegacyMigrationStatus
{
    None = 0,
    NoLegacyFiles = 1,
    Migrated = 2,
    Failed = 3,
}

internal readonly record struct LegacyMigrationOutcome(
    LegacyMigrationStatus Status,
    UserStatisticsCatalogDocument? Document)
{
    public static LegacyMigrationOutcome NoLegacyFiles => new(LegacyMigrationStatus.NoLegacyFiles, null);
    public static LegacyMigrationOutcome Failed => new(LegacyMigrationStatus.Failed, null);
    public static LegacyMigrationOutcome Migrated(UserStatisticsCatalogDocument document) => new(LegacyMigrationStatus.Migrated, document);
}
