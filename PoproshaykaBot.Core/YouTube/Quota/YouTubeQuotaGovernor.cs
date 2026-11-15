using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using System.Globalization;

namespace PoproshaykaBot.Core.YouTube.Quota;

internal sealed class YouTubeQuotaGovernor(
    YouTubeQuotaStore store,
    YouTubeSettingsStore settingsStore,
    TimeProvider timeProvider,
    ILogger<YouTubeQuotaGovernor> logger)
    : IYouTubeQuotaGovernor
{
    private readonly Lock _sync = new();
    private readonly Dictionary<string, BucketState> _buckets = new(StringComparer.OrdinalIgnoreCase);
    private bool _hydratedFromStore;
    private long _snapshotSequence;

    public bool TryReserve(
        PlatformAuthRole role,
        YouTubeQuotaPriority priority,
        int cost,
        out YouTubeQuotaLease lease)
    {
        if (cost <= 0 || priority == YouTubeQuotaPriority.None)
        {
            lease = YouTubeQuotaLease.Empty;
            return false;
        }

        EnsureHydrated();

        var settings = settingsStore.Load();
        var projectKey = YouTubeQuotaPolicy.NormalizeProjectKey(settings);
        var dailyBudget = YouTubeQuotaPolicy.NormalizeBudget(settings.DailyQuotaBudget);
        var window = PacificQuotaClock.GetWindow(timeProvider.GetUtcNow());

        lock (_sync)
        {
            var bucket = GetOrRolloverBucketLocked(projectKey, dailyBudget, window.PacificDate, out _);
            var ceiling = YouTubeQuotaPolicy.GetCeiling(dailyBudget, priority, store.Reliability);

            if (bucket.ExhaustedByServer || bucket.CommittedUnits + bucket.ReservedUnits + cost > ceiling)
            {
                lease = YouTubeQuotaLease.Empty;
                return false;
            }

            bucket.ReservedUnits += cost;
            lease = new(this, projectKey, window.PacificDate, role, cost);
            return true;
        }
    }

    public void MarkExhaustedFromServer(PlatformAuthRole role)
    {
        EnsureHydrated();

        var settings = settingsStore.Load();
        var projectKey = YouTubeQuotaPolicy.NormalizeProjectKey(settings);
        var dailyBudget = YouTubeQuotaPolicy.NormalizeBudget(settings.DailyQuotaBudget);
        var utcNow = timeProvider.GetUtcNow();
        var window = PacificQuotaClock.GetWindow(utcNow);

        YouTubeQuotaDocument? snapshot = null;
        long snapshotSequence = 0;
        var shouldLogWarning = false;

        lock (_sync)
        {
            var bucket = GetOrRolloverBucketLocked(projectKey, dailyBudget, window.PacificDate, out _);

            if (!bucket.ExhaustedByServer || !bucket.ExhaustedWarningLogged)
            {
                shouldLogWarning = !bucket.ExhaustedWarningLogged;
                bucket.ExhaustedByServer = true;
                bucket.ExhaustedWarningLogged = true;
                bucket.UpdatedAtUtc = utcNow;
                snapshot = BuildDocumentSnapshotLocked();
                snapshotSequence = ++_snapshotSequence;
            }
        }

        if (shouldLogWarning)
        {
            logger.LogWarning(
                "YouTube API сообщил об исчерпании суточной квоты (проект={ProjectKey}, роль={Role}, сброс={ResetAt:O})",
                projectKey,
                role,
                window.NextResetAtUtc);
        }

        if (snapshot is not null)
        {
            store.SaveSnapshot(snapshot, snapshotSequence);
        }
    }

    public YouTubeQuotaStatus CaptureDiagnostics()
    {
        EnsureHydrated();

        var settings = settingsStore.Load();
        var projectKey = YouTubeQuotaPolicy.NormalizeProjectKey(settings);
        var dailyBudget = YouTubeQuotaPolicy.NormalizeBudget(settings.DailyQuotaBudget);
        var window = PacificQuotaClock.GetWindow(timeProvider.GetUtcNow());

        lock (_sync)
        {
            var bucket = GetOrRolloverBucketLocked(projectKey, dailyBudget, window.PacificDate, out _);
            var tier1Remaining = bucket.ExhaustedByServer
                ? 0
                : Math.Max(0, dailyBudget - bucket.CommittedUnits - bucket.ReservedUnits);

            return new(
                string.Equals(projectKey, YouTubeQuotaPolicy.UnconfiguredProjectKey, StringComparison.Ordinal) ? null : projectKey,
                window.PacificDate,
                window.NextResetAtUtc,
                dailyBudget,
                bucket.CommittedUnits,
                bucket.ReservedUnits,
                bucket.BotCommittedUnits,
                bucket.BroadcasterCommittedUnits,
                tier1Remaining,
                bucket.ExhaustedByServer,
                store.Reliability,
                store.CanWriteFile);
        }
    }

    internal void Commit(string projectKey, DateOnly leaseDate, PlatformAuthRole role, int cost)
    {
        EnsureHydrated();

        var settings = settingsStore.Load();
        var dailyBudget = YouTubeQuotaPolicy.NormalizeBudget(settings.DailyQuotaBudget);
        var utcNow = timeProvider.GetUtcNow();
        var window = PacificQuotaClock.GetWindow(utcNow);

        YouTubeQuotaDocument snapshot;
        long snapshotSequence;

        lock (_sync)
        {
            var bucket = GetOrRolloverBucketLocked(projectKey, dailyBudget, window.PacificDate, out _);

            if (bucket.PacificDate == leaseDate)
            {
                bucket.ReservedUnits = Math.Max(0, bucket.ReservedUnits - cost);
            }

            bucket.CommittedUnits += cost;

            if (role == PlatformAuthRole.Bot)
            {
                bucket.BotCommittedUnits += cost;
            }
            else if (role == PlatformAuthRole.Broadcaster)
            {
                bucket.BroadcasterCommittedUnits += cost;
            }

            bucket.DailyBudget = dailyBudget;
            bucket.UpdatedAtUtc = utcNow;
            snapshot = BuildDocumentSnapshotLocked();
            snapshotSequence = ++_snapshotSequence;
        }

        store.SaveSnapshot(snapshot, snapshotSequence);
    }

    internal void Refund(string projectKey, DateOnly leaseDate, int cost)
    {
        lock (_sync)
        {
            if (_buckets.TryGetValue(projectKey, out var bucket) && bucket.PacificDate == leaseDate)
            {
                bucket.ReservedUnits = Math.Max(0, bucket.ReservedUnits - cost);
            }
        }
    }

    private void EnsureHydrated()
    {
        if (Volatile.Read(ref _hydratedFromStore))
        {
            return;
        }

        var loaded = store.EnsureLoaded();

        lock (_sync)
        {
            if (_hydratedFromStore)
            {
                return;
            }

            foreach (var (key, record) in loaded.Projects)
            {
                if (!DateOnly.TryParseExact(
                        record.PacificDate,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var date))
                {
                    continue;
                }

                _buckets[key] = new()
                {
                    PacificDate = date,
                    DailyBudget = record.DailyBudget,
                    CommittedUnits = record.CommittedUnits,
                    BotCommittedUnits = record.BotCommittedUnits,
                    BroadcasterCommittedUnits = record.BroadcasterCommittedUnits,
                    ExhaustedByServer = record.ExhaustedByServer,
                    UpdatedAtUtc = record.UpdatedAtUtc,
                };
            }

            _hydratedFromStore = true;
        }
    }

    private BucketState GetOrRolloverBucketLocked(
        string projectKey,
        int dailyBudget,
        DateOnly currentPacificDate,
        out bool rolledOver)
    {
        rolledOver = false;

        if (!_buckets.TryGetValue(projectKey, out var bucket))
        {
            bucket = new()
            {
                PacificDate = currentPacificDate,
                DailyBudget = dailyBudget,
            };

            _buckets[projectKey] = bucket;
            return bucket;
        }

        if (bucket.PacificDate != currentPacificDate)
        {
            bucket.PacificDate = currentPacificDate;
            bucket.DailyBudget = dailyBudget;
            bucket.CommittedUnits = 0;
            bucket.BotCommittedUnits = 0;
            bucket.BroadcasterCommittedUnits = 0;
            bucket.ReservedUnits = 0;
            bucket.ExhaustedByServer = false;
            bucket.ExhaustedWarningLogged = false;
            rolledOver = true;
        }
        else
        {
            bucket.DailyBudget = dailyBudget;
        }

        return bucket;
    }

    private YouTubeQuotaDocument BuildDocumentSnapshotLocked()
    {
        var doc = YouTubeQuotaDocument.CreateEmpty();

        foreach (var (key, bucket) in _buckets)
        {
            if (bucket.CommittedUnits == 0
                && !bucket.ExhaustedByServer
                && bucket.BotCommittedUnits == 0
                && bucket.BroadcasterCommittedUnits == 0)
            {
                continue;
            }

            doc.Projects[key] = new()
            {
                PacificDate = bucket.PacificDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                DailyBudget = bucket.DailyBudget,
                CommittedUnits = bucket.CommittedUnits,
                BotCommittedUnits = bucket.BotCommittedUnits,
                BroadcasterCommittedUnits = bucket.BroadcasterCommittedUnits,
                ExhaustedByServer = bucket.ExhaustedByServer,
                UpdatedAtUtc = bucket.UpdatedAtUtc,
            };
        }

        return doc;
    }

    private sealed class BucketState
    {
        public DateOnly PacificDate { get; set; }

        public int DailyBudget { get; set; }

        public int CommittedUnits { get; set; }

        public int BotCommittedUnits { get; set; }

        public int BroadcasterCommittedUnits { get; set; }

        public int ReservedUnits { get; set; }

        public bool ExhaustedByServer { get; set; }

        public bool ExhaustedWarningLogged { get; set; }

        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
