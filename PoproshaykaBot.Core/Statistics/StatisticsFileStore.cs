using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text;
using System.Text.Json;

namespace PoproshaykaBot.Core.Statistics;

public sealed class StatisticsFileStore(ILogger<StatisticsFileStore> logger)
{
    private const string UserStatisticsFileName = "users_statistics.json";
    private const string BotStatisticsFileName = "bot_statistics.json";

    private readonly string _userStatisticsFilePath = Path.Combine(AppPaths.BaseDirectory, UserStatisticsFileName);
    private readonly string _botStatisticsFilePath = Path.Combine(AppPaths.BaseDirectory, BotStatisticsFileName);

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
        logger.LogDebug("Загрузка статистики {EntityName} из файла {FilePath}", entityName, filePath);

        if (!File.Exists(filePath))
        {
            logger.LogWarning("Файл статистики {EntityName} не найден. Будут использованы значения по умолчанию", entityName);
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<T>(json, JsonStoreOptions.Default);

            if (data == null)
            {
                logger.LogWarning("Десериализация статистики {EntityName} вернула null", entityName);
            }

            return data;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка чтения или десериализации файла статистики {EntityName}", entityName);
            JsonStoreBackup.CreateBackup(filePath, "invalid", logger);
            return null;
        }
    }

    private void SaveToFile<T>(T data, string targetFilePath, string entityName)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, JsonStoreOptions.Default);
            AtomicFile.Save(targetFilePath, json, logger);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Ошибка при записи статистики {EntityName} в файл", entityName);
            throw;
        }
    }
}
