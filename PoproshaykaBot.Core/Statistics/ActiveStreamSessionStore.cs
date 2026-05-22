using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Json;

namespace PoproshaykaBot.Core.Statistics;

public class ActiveStreamSessionStore(ILogger<ActiveStreamSessionStore>? logger = null, string? filePath = null)
{
    private const string FileName = "active_session.json";

    private readonly string _filePath = filePath ?? Path.Combine(AppPaths.BaseDirectory, FileName);
    private readonly object _syncLock = new();

    public virtual ActiveStreamSession? Load()
    {
        lock (_syncLock)
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<ActiveStreamSession>(json, JsonStoreOptions.Default);
            }
            catch (Exception exception)
            {
                logger?.LogError(exception, "Ошибка чтения {FilePath}, черновик активной сессии отброшен", _filePath);
                JsonStoreBackup.CreateBackup(_filePath, "invalid", logger);
                return null;
            }
        }
    }

    public virtual void Save(ActiveStreamSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        lock (_syncLock)
        {
            var json = JsonSerializer.Serialize(session, JsonStoreOptions.Default);
            AtomicFile.Save(_filePath, json, logger);
        }
    }

    public virtual void Delete()
    {
        lock (_syncLock)
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch (Exception exception)
            {
                logger?.LogWarning(exception, "Не удалось удалить черновик активной сессии {FilePath}", _filePath);
            }
        }
    }
}
