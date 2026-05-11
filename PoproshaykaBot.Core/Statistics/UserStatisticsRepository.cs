using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Statistics;

public sealed class UserStatisticsRepository(ILogger<UserStatisticsRepository> logger) : IUserStatisticsRepository
{
    private readonly Dictionary<string, UserStatistics> _users = new();
    private readonly object _lock = new();
    private bool _hasChanges;

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
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("ИД пользователя не может быть null или пустым.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Имя пользователя не может быть null или пустым.", nameof(username));
        }

        lock (_lock)
        {
            if (_users.TryGetValue(userId, out var statistic))
            {
                statistic.UpdateName(username);
            }
            else
            {
                statistic = UserStatistics.Create(userId, username);
                _users[userId] = statistic;
                logger.LogInformation("Зарегистрирован новый пользователь в статистике: {UserId}", userId);
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
            var found = _users.Values
                .FirstOrDefault(x => string.Equals(x.Name, target, StringComparison.OrdinalIgnoreCase));

            return found?.Clone();
        }
    }

    public IReadOnlyList<UserStatistics> GetAll()
    {
        lock (_lock)
        {
            return _users.Values.Select(u => u.Clone()).ToList();
        }
    }

    public IReadOnlyList<UserStatistics> GetTop(int count, UserTopMode mode = UserTopMode.Points)
    {
        lock (_lock)
        {
            IEnumerable<UserStatistics> ordered = mode == UserTopMode.Messages
                ? _users.Values.OrderByDescending(x => x.MessageCount)
                : _users.Values.OrderByDescending(x => x.Points);

            return ordered
                .Take(count)
                .Select(u => u.Clone())
                .ToList();
        }
    }

    public bool IncrementBonusMessages(string userId, ulong delta)
    {
        var updated = AdjustPoints(userId, delta, (stats, d) => stats.BonusPoints += d);

        if (updated)
        {
            logger.LogInformation("Пользователю {UserId} начислено {Delta} бонусных баллов", userId, delta);
        }

        return updated;
    }

    public bool IncrementShtrafMessages(string userId, ulong delta)
    {
        var updated = AdjustPoints(userId, delta, (stats, d) => stats.PenaltyPoints += d);

        if (updated)
        {
            logger.LogInformation("Пользователю {UserId} начислено {Delta} штрафных баллов", userId, delta);
        }

        return updated;
    }

    public void ReplaceAll(IEnumerable<UserStatistics> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        lock (_lock)
        {
            _users.Clear();

            foreach (var user in users)
            {
                _users[user.UserId] = user;
            }

            _hasChanges = false;
        }
    }

    public List<UserStatistics> CreateSnapshotAndMarkSaved()
    {
        lock (_lock)
        {
            var snapshot = _users.Values.Select(u => u.Clone()).ToList();
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
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("ID пользователя не может быть null или пустым.", nameof(userId));
        }

        if (delta == 0)
        {
            return false;
        }

        lock (_lock)
        {
            if (!_users.TryGetValue(userId, out var stats))
            {
                logger.LogWarning("Попытка обновить сообщения для неизвестного пользователя {UserId}. Операция пропущена", userId);
                return false;
            }

            adjust(stats, delta);
            stats.LastSeen = DateTime.UtcNow;
            _hasChanges = true;
        }

        return true;
    }
}
