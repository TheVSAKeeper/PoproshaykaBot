using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Statistics;

public sealed class StatisticsFileStore
{
    internal const string CommandUsageFileName = "command-usage.json";

    private readonly ILogger<StatisticsFileStore> _logger;
    private readonly string _baseDirectory;

    public StatisticsFileStore(ILogger<StatisticsFileStore> logger)
        : this(logger, AppPaths.BaseDirectory)
    {
    }

    internal StatisticsFileStore(ILogger<StatisticsFileStore> logger, string baseDirectory)
    {
        _logger = logger;
        _baseDirectory = baseDirectory;
    }

    public Task<StatisticsReadResult<T>> LoadAsync<T>(
        string relativePath,
        string entityName,
        CancellationToken cancellationToken = default)
        where T : class
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(LoadFromFile<T>(ResolvePath(relativePath), entityName));
    }

    public Task SaveAsync<T>(
        string relativePath,
        string entityName,
        T data,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => SaveToFile(data, ResolvePath(relativePath), entityName), cancellationToken);
    }

    public bool FileExists(string relativePath)
    {
        return File.Exists(ResolvePath(relativePath));
    }

    public Task<StatisticsReadResult<UserStatisticsCatalogDocument>> LoadUserStatisticsCatalogAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filePath = ResolvePath(LegacyDataCatalog.UserStatisticsCatalogFileName);
        _logger.LogDebug("Загрузка каталога статистики пользователей из файла {FilePath}", filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogDebug("Файл каталога статистики пользователей {FilePath} не найден", filePath);
            return Task.FromResult(StatisticsReadResult<UserStatisticsCatalogDocument>.NoFile);
        }

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<UserStatisticsCatalogDocument>(json, JsonStoreOptions.Default);

            if (data == null)
            {
                _logger.LogError("Десериализация каталога статистики пользователей вернула null – файл {FilePath} повреждён", filePath);
                JsonStoreBackup.CreateBackup(filePath, "invalid", _logger);
                return Task.FromResult(StatisticsReadResult<UserStatisticsCatalogDocument>.Failure);
            }

            if (data.SchemaVersion <= 0 || data.SchemaVersion > UserStatisticsCatalogDocument.CurrentSchemaVersion)
            {
                _logger.LogError(
                    "Версия схемы каталога статистики {SchemaVersion} не поддерживается (поддерживается до {MaxVersion}); файл {FilePath} оставлен без изменений",
                    data.SchemaVersion,
                    UserStatisticsCatalogDocument.CurrentSchemaVersion,
                    filePath);
                return Task.FromResult(StatisticsReadResult<UserStatisticsCatalogDocument>.Failure);
            }

            return Task.FromResult(StatisticsReadResult<UserStatisticsCatalogDocument>.FromFile(data));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка чтения или десериализации каталога статистики пользователей {FilePath}", filePath);
            JsonStoreBackup.CreateBackup(filePath, "invalid", _logger);
            return Task.FromResult(StatisticsReadResult<UserStatisticsCatalogDocument>.Failure);
        }
    }

    public Task SaveUserStatisticsCatalogAsync(
        UserStatisticsCatalogDocument snapshot,
        CancellationToken cancellationToken = default)
    {
        return SaveAsync(LegacyDataCatalog.UserStatisticsCatalogFileName, "каталога статистики пользователей", snapshot, cancellationToken);
    }

    public async Task<StatisticsReadResult<List<UserStatistics>>> LoadUsersAsync(
        StreamingPlatform platform,
        CancellationToken cancellationToken = default)
    {
        var descriptor = UserStatisticsPlatformCatalog.Get(platform);
        var result = await LoadAsync<List<UserStatistics>>(descriptor.RelativePath, descriptor.EntityName, cancellationToken)
            .ConfigureAwait(false);

        if (result.Value != null)
        {
            foreach (var user in result.Value)
            {
                user.Platform = platform;
            }
        }

        return result;
    }

    public Task SaveUsersAsync(
        StreamingPlatform platform,
        IReadOnlyList<UserStatistics> snapshot,
        CancellationToken cancellationToken = default)
    {
        var descriptor = UserStatisticsPlatformCatalog.Get(platform);
        return SaveAsync(descriptor.RelativePath, descriptor.EntityName, snapshot, cancellationToken);
    }

    public Task<StatisticsReadResult<BotStatistics>> LoadBotAsync(CancellationToken cancellationToken = default)
    {
        return LoadAsync<BotStatistics>(LegacyDataCatalog.BotStatisticsFileName, "бота", cancellationToken);
    }

    public Task SaveBotAsync(BotStatistics snapshot, CancellationToken cancellationToken = default)
    {
        return SaveAsync(LegacyDataCatalog.BotStatisticsFileName, "бота", snapshot, cancellationToken);
    }

    public Task<StatisticsReadResult<CommandUsageData>> LoadCommandUsageAsync(CancellationToken cancellationToken = default)
    {
        return LoadAsync<CommandUsageData>(CommandUsageFileName, "команд", cancellationToken);
    }

    public Task SaveCommandUsageAsync(CommandUsageData snapshot, CancellationToken cancellationToken = default)
    {
        return SaveAsync(CommandUsageFileName, "команд", snapshot, cancellationToken);
    }

    private string ResolvePath(string relativePath)
    {
        var normalized = LegacyDataCatalog.NormalizeRelativePath(relativePath)
            .Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_baseDirectory, normalized);
    }

    private StatisticsReadResult<T> LoadFromFile<T>(string filePath, string entityName)
        where T : class
    {
        _logger.LogDebug("Загрузка статистики {EntityName} из файла {FilePath}", entityName, filePath);

        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Файл статистики {EntityName} не найден. Будут использованы значения по умолчанию", entityName);
            return StatisticsReadResult<T>.NoFile;
        }

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var data = JsonSerializer.Deserialize<T>(json, JsonStoreOptions.Default);

            if (data == null)
            {
                _logger.LogError("Десериализация статистики {EntityName} вернула null – файл {FilePath} повреждён", entityName, filePath);
                JsonStoreBackup.CreateBackup(filePath, "invalid", _logger);
                return StatisticsReadResult<T>.Failure;
            }

            return StatisticsReadResult<T>.FromFile(data);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка чтения или десериализации файла статистики {EntityName}", entityName);
            JsonStoreBackup.CreateBackup(filePath, "invalid", _logger);
            return StatisticsReadResult<T>.Failure;
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
