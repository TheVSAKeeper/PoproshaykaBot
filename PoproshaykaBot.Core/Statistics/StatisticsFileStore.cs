using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text;
using System.Text.Json;

namespace PoproshaykaBot.Core.Statistics;

public sealed class StatisticsFileStore
{
    private const string UserStatisticsFileName = "users_statistics.json";
    private const string BotStatisticsFileName = "bot_statistics.json";

    private readonly ILogger<StatisticsFileStore> _logger;
    private readonly string _userStatisticsFilePath;
    private readonly string _botStatisticsFilePath;

    public StatisticsFileStore(ILogger<StatisticsFileStore> logger)
        : this(logger, AppPaths.BaseDirectory)
    {
    }

    internal StatisticsFileStore(ILogger<StatisticsFileStore> logger, string baseDirectory)
    {
        _logger = logger;
        _userStatisticsFilePath = Path.Combine(baseDirectory, UserStatisticsFileName);
        _botStatisticsFilePath = Path.Combine(baseDirectory, BotStatisticsFileName);
    }

    public Task<List<UserStatistics>> LoadUsersAsync(CancellationToken cancellationToken = default)
    {
        var loaded = LoadFromFile<List<UserStatistics>>(_userStatisticsFilePath, "пользователей");
        return Task.FromResult(loaded ?? []);
    }

    public Task<BotStatistics?> LoadBotAsync(CancellationToken cancellationToken = default)
    {
        var loaded = LoadFromFile<BotStatistics>(_botStatisticsFilePath, "бота");
        return Task.FromResult(loaded);
    }

    public Task SaveUsersAsync(IReadOnlyList<UserStatistics> snapshot, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SaveToFile(snapshot, _userStatisticsFilePath, "пользователей"), cancellationToken);
    }

    public Task SaveBotAsync(BotStatistics snapshot, CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SaveToFile(snapshot, _botStatisticsFilePath, "бота"), cancellationToken);
    }

    private T? LoadFromFile<T>(string filePath, string entityName)
        where T : class
    {
        _logger.LogDebug("Загрузка статистики {EntityName} из файла {FilePath}", entityName, filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Файл статистики {EntityName} не найден. Будут использованы значения по умолчанию", entityName);
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<T>(json, JsonStoreOptions.Default);

            if (data == null)
            {
                _logger.LogWarning("Десериализация статистики {EntityName} вернула null", entityName);
            }

            return data;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка чтения или десериализации файла статистики {EntityName}", entityName);
            JsonStoreBackup.CreateBackup(filePath, "invalid", _logger);
            return null;
        }
    }

    private void SaveToFile<T>(T data, string targetFilePath, string entityName)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, JsonStoreOptions.Default);
            AtomicFile.Save(targetFilePath, json, _logger);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка при записи статистики {EntityName} в файл {Path}", entityName, targetFilePath);
            throw new InvalidOperationException($"Не удалось сохранить статистику ({entityName}) в {targetFilePath}", exception);
        }
    }
}
