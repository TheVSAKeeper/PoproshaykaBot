using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Statistics;

internal sealed class UserStatisticsPartition(StreamingPlatform platform, ILogger logger) : IPlatformUserStatistics
{
    private readonly Dictionary<string, UserStatistics> _users = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private bool _hasChanges;

    public StreamingPlatform Platform => platform;

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

    public void TrackMessage(string userId, string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        lock (_lock)
        {
            if (_users.TryGetValue(userId, out var statistic))
            {
                statistic.UpdateName(username);
            }
            else
            {
                statistic = UserStatistics.Create(platform, userId, username);
                _users[userId] = statistic;
                logger.LogInformation(
                    "Зарегистрирован новый пользователь в статистике: {Platform} {UserId}",
                    platform,
                    userId);
            }

            statistic.IncrementMessageCount();
            _hasChanges = true;
        }
    }

    public UserStatistics? GetById(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        lock (_lock)
        {
            return _users.TryGetValue(userId, out var stats) ? stats.Clone() : null;
        }
    }

    public UserStatistics? GetByName(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var target = username.TrimStart('@');

        lock (_lock)
        {
            var found = _users.Values.FirstOrDefault(x => string.Equals(x.Name, target, StringComparison.OrdinalIgnoreCase));
            return found?.Clone();
        }
    }

    public IReadOnlyList<UserStatistics> GetAll()
    {
        lock (_lock)
        {
            return _users.Values.Select(static u => u.Clone()).ToList();
        }
    }

    public IReadOnlyList<UserStatistics> GetTop(int count, UserTopMode mode = UserTopMode.Points)
    {
        lock (_lock)
        {
            IEnumerable<UserStatistics> ordered = mode == UserTopMode.Messages
                ? _users.Values.OrderByDescending(static x => x.MessageCount)
                : _users.Values.OrderByDescending(static x => x.Points);

            return ordered
                .Take(count)
                .Select(static u => u.Clone())
                .ToList();
        }
    }

    public bool IncrementBonusPoints(string userId, ulong delta)
    {
        return AdjustPoints(userId, delta, static (stats, d) => stats.BonusPoints += d);
    }

    public bool IncrementPenaltyPoints(string userId, ulong delta)
    {
        return AdjustPoints(userId, delta, static (stats, d) => stats.PenaltyPoints += d);
    }

    public void Replace(IEnumerable<UserStatistics> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        lock (_lock)
        {
            _users.Clear();

            foreach (var user in users)
            {
                user.Platform = platform;
                _users[user.UserId] = user;
            }

            _hasChanges = false;
        }
    }

    public List<UserStatistics> CreateSnapshotAndMarkSaved()
    {
        lock (_lock)
        {
            var snapshot = _users.Values.Select(static u => u.Clone()).ToList();
            _hasChanges = false;
            return snapshot;
        }
    }

    public void MarkChanged()
    {
        lock (_lock)
        {
            _hasChanges = true;
        }
    }

    private bool AdjustPoints(string userId, ulong delta, Action<UserStatistics, ulong> adjust)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (delta == 0)
        {
            return false;
        }

        lock (_lock)
        {
            if (!_users.TryGetValue(userId, out var stats))
            {
                logger.LogWarning(
                    "Попытка обновить очки для неизвестного пользователя {Platform} {UserId}. Операция пропущена",
                    platform,
                    userId);
                return false;
            }

            adjust(stats, delta);
            stats.LastSeen = DateTime.UtcNow;
            _hasChanges = true;
        }

        return true;
    }
}
