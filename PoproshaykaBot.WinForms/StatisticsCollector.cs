using PoproshaykaBot.WinForms.Models;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Timers;
using Timer = System.Timers.Timer;

namespace PoproshaykaBot.WinForms;

public class StatisticsCollector : IAsyncDisposable
{
    private static readonly TimeSpan DefaultAutoSaveInterval = TimeSpan.FromMinutes(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _statisticsDirectory;
    private readonly string _userStatisticsFilePath;
    private readonly string _botStatisticsFilePath;
    private readonly Timer _autoSaveTimer;
    private readonly Dictionary<string, UserStatistics> _userStatistics;
    private BotStatistics _botStatistics;
    private bool _hasChanges;
    private bool _disposed;

    public StatisticsCollector(TimeSpan? autoSaveInterval = null)
    {
        var interval = autoSaveInterval ?? DefaultAutoSaveInterval;

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentException("Интервал автосохранения должен быть больше нуля.", nameof(autoSaveInterval));
        }

        _statisticsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PoproshaykaBot");

        _userStatisticsFilePath = Path.Combine(_statisticsDirectory, "users_statistics.json");
        _botStatisticsFilePath = Path.Combine(_statisticsDirectory, "bot_statistics.json");

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

    private async Task SaveStatisticsAsync(bool force = false)
    {
        if (force == false && _hasChanges == false)
        {
            return;
        }

        try
        {
            await SaveUserStatisticsAsync(_userStatistics);
            await SaveBotStatisticsAsync(_botStatistics);
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
            if (File.Exists(_userStatisticsFilePath) == false)
            {
                return new();
            }

            var json = await File.ReadAllTextAsync(_userStatisticsFilePath, Encoding.UTF8);
            var userStatisticsList = JsonSerializer.Deserialize<List<UserStatistics>>(json, JsonOptions);

            if (userStatisticsList != null)
            {
                return userStatisticsList.ToDictionary(x => x.UserId, x => x);
            }

            return new();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Ошибка загрузки статистики пользователей: {exception.Message}", exception);
        }
    }

    private async Task SaveUserStatisticsAsync(Dictionary<string, UserStatistics> userStatistics)
    {
        try
        {
            Directory.CreateDirectory(_statisticsDirectory);

            var userStatisticsList = userStatistics.Values.ToList();
            var json = JsonSerializer.Serialize(userStatisticsList, JsonOptions);

            await File.WriteAllTextAsync(_userStatisticsFilePath, json, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Ошибка сохранения статистики пользователей: {exception.Message}", exception);
        }
    }

    private async Task<BotStatistics> LoadBotStatisticsAsync()
    {
        try
        {
            if (File.Exists(_botStatisticsFilePath) == false)
            {
                return BotStatistics.Create();
            }

            var json = await File.ReadAllTextAsync(_botStatisticsFilePath, Encoding.UTF8);
            var botStatistics = JsonSerializer.Deserialize<BotStatistics>(json, JsonOptions);

            return botStatistics ?? BotStatistics.Create();
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Ошибка загрузки статистики бота: {exception.Message}", exception);
        }
    }

    private async Task SaveBotStatisticsAsync(BotStatistics botStatistics)
    {
        try
        {
            Directory.CreateDirectory(_statisticsDirectory);

            var json = JsonSerializer.Serialize(botStatistics, JsonOptions);
            await File.WriteAllTextAsync(_botStatisticsFilePath, json, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Ошибка сохранения статистики бота: {exception.Message}", exception);
        }
    }

    private void MarkAsChanged()
    {
        _hasChanges = true;
    }
}
