using PoproshaykaBot.Core.Domain.Models.Statistics;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using System.Timers;
using Timer = System.Timers.Timer;

namespace PoproshaykaBot.Core.Application.Statistics;

/// <summary>
/// Сервис для сбора и хранения статистики бота и пользователей.
/// Использует FileRepository для персистентности данных.
/// </summary>
public class StatisticsCollector : IAsyncDisposable
{
    private static readonly TimeSpan DefaultAutoSaveInterval = TimeSpan.FromMinutes(1);

    private readonly JsonFileService _jsonService;
    private readonly FileBackupService _backupService;
    private readonly string _userStatisticsFilePath;
    private readonly string _botStatisticsFilePath;
    private readonly Timer _autoSaveTimer;
    private readonly Dictionary<string, UserStatistics> _userStatistics;
    private BotStatistics _botStatistics;
    private bool _hasChanges;
    private bool _disposed;

    public StatisticsCollector(TimeSpan? autoSaveInterval = null)
        : this(new(), new(), autoSaveInterval)
    {
    }

    public StatisticsCollector(JsonFileService jsonService, FileBackupService backupService, TimeSpan? autoSaveInterval = null)
    {
        _jsonService = jsonService ?? throw new ArgumentNullException(nameof(jsonService));
        _backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));

        var interval = autoSaveInterval ?? DefaultAutoSaveInterval;

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentException("Интервал автосохранения должен быть больше нуля.", nameof(autoSaveInterval));
        }

        var statisticsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoproshaykaBot");

        _userStatisticsFilePath = Path.Combine(statisticsDirectory, "users_statistics.json");
        _botStatisticsFilePath = Path.Combine(statisticsDirectory, "bot_statistics.json");

        _userStatistics = new();
        _botStatistics = BotStatistics.Create();
        _hasChanges = false;

        _autoSaveTimer = new(interval.TotalMilliseconds);
        _autoSaveTimer.Elapsed += OnAutoSaveTimerElapsed;
        _autoSaveTimer.AutoReset = true;
    }

    public async Task StartAsync()
    {
        try
        {
            await LoadStatisticsAsync();
            _autoSaveTimer.Start();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Ошибка запуска сборщика статистики: {exception.Message}", exception);
        }
    }

    public async Task StopAsync()
    {
        _autoSaveTimer.Stop();

        try
        {
            await SaveStatisticsAsync(true);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Ошибка остановки сборщика статистики: {exception.Message}", exception);
        }
    }

    public void TrackMessage(string userId, string username)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("ID пользователя не может быть null или пустым.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            throw new ArgumentException("Имя пользователя не может быть null или пустым.", nameof(username));
        }

        if (_userStatistics.TryGetValue(userId, out var statistic))
        {
            statistic.UpdateName(username);
        }
        else
        {
            _userStatistics[userId] = UserStatistics.Create(userId, username);
        }

        _userStatistics[userId].IncrementMessageCount();
        _botStatistics.IncrementMessagesProcessed();

        MarkAsChanged();
    }

    public UserStatistics? GetUserStatistics(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        _userStatistics.TryGetValue(userId, out var userStats);
        return userStats;
    }

    public BotStatistics GetBotStatistics()
    {
        _botStatistics.UpdateUptime();
        return _botStatistics;
    }

    public List<UserStatistics> GetTopUsers(int count = 10)
    {
        var topUsers = _userStatistics.Values
            .OrderByDescending(x => x.MessageCount)
            .Take(count)
            .ToList();

        return topUsers;
    }

    public List<UserStatistics> GetAllUsers()
    {
        return _userStatistics.Values.ToList();
    }

    public bool IncrementUserMessages(string userId, ulong delta)
    {
        return UpdateUserMessages(userId, delta, (stats, d) => stats.MessageCount += d);
    }

    public bool DecrementUserMessages(string userId, ulong delta)
    {
        return UpdateUserMessages(userId, delta, (stats, d) =>
        {
            if (stats.MessageCount >= d)
            {
                stats.MessageCount -= d;
            }
            else
            {
                stats.MessageCount = 0;
            }
        });
    }

    public Task SaveNowAsync()
    {
        return SaveStatisticsAsync(true);
    }

    public void ResetBotStartTime()
    {
        _botStatistics.ResetStartTime();
        MarkAsChanged();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    public async Task LoadStatisticsAsync()
    {
        try
        {
            var userStats = await LoadUserStatisticsAsync();
            _userStatistics.Clear();

            foreach (var (id, userStatistic) in userStats)
            {
                _userStatistics[id] = userStatistic;
            }

            _botStatistics = await LoadBotStatisticsAsync();
            _hasChanges = false;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Ошибка загрузки статистики: {exception.Message}", exception);
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync();

        _autoSaveTimer.Stop();
        _autoSaveTimer.Dispose();
        _disposed = true;
    }

    private async void OnAutoSaveTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        try
        {
            await SaveStatisticsAsync();
        }
        catch (Exception)
        {
            // TODO: Добавить логирование ошибок автосохранения
        }
    }

    // TODO: Канкаренси
    private bool UpdateUserMessages(string userId, ulong delta, Action<UserStatistics, ulong> updateAction)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("ID пользователя не может быть null или пустым.", nameof(userId));
        }

        if (delta == 0)
        {
            return false;
        }

        if (!_userStatistics.TryGetValue(userId, out var stats))
        {
            return false;
        }

        updateAction(stats, delta);
        stats.LastSeen = DateTime.UtcNow;

        MarkAsChanged();
        return true;
    }

    private async Task SaveStatisticsAsync(bool force = false)
    {
        if (!force && !_hasChanges)
        {
            return;
        }

        try
        {
            var userStatisticsList = _userStatistics.Values.ToList();
            await _jsonService.SaveAsync(_userStatisticsFilePath, userStatisticsList, createBackup: true);
            await _jsonService.SaveAsync(_botStatisticsFilePath, _botStatistics, createBackup: true);
            _hasChanges = false;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Ошибка сохранения статистики: {exception.Message}", exception);
        }
    }

    private async Task<Dictionary<string, UserStatistics>> LoadUserStatisticsAsync()
    {
        try
        {
            var userStatisticsList = await _jsonService.LoadAsync<List<UserStatistics>>(_userStatisticsFilePath);

            if (userStatisticsList != null)
            {
                return userStatisticsList.ToDictionary(x => x.UserId, x => x);
            }

            return new();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Ошибка загрузки статистики пользователей: {exception.Message}");
            _backupService.CreateBackup(_userStatisticsFilePath, "invalid");
            return new();
        }
    }

    private async Task<BotStatistics> LoadBotStatisticsAsync()
    {
        try
        {
            var botStatistics = await _jsonService.LoadAsync<BotStatistics>(_botStatisticsFilePath);
            return botStatistics ?? BotStatistics.Create();
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Ошибка загрузки статистики бота: {exception.Message}");
            _backupService.CreateBackup(_botStatisticsFilePath, "invalid");
            return BotStatistics.Create();
        }
    }

    private void MarkAsChanged()
    {
        _hasChanges = true;
    }
}
