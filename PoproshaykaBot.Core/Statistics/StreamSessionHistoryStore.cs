using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Json;

namespace PoproshaykaBot.Core.Statistics;

public class StreamSessionHistoryStore
{
    private const string FileName = "stream_sessions.json";

    private readonly ILogger<StreamSessionHistoryStore>? _logger;
    private readonly string _filePath;
    private readonly object _syncLock = new();

    private readonly StreamSessionHistory _state;

    public StreamSessionHistoryStore(ILogger<StreamSessionHistoryStore>? logger = null, string? filePath = null)
    {
        _logger = logger;
        _filePath = filePath ?? Path.Combine(AppPaths.BaseDirectory, FileName);

        var read = ReadFile();
        _state = read.Value ?? new();
        IsLoaded = !read.Failed;

        if (IsLoaded)
        {
            MergeInterruptedSessions();
        }

        _logger?.LogDebug("StreamSessionHistoryStore инициализирован из {FilePath} (сессий: {SessionCount}, чтение удалось: {IsLoaded})",
            _filePath,
            _state.Sessions.Count,
            IsLoaded);
    }

    public bool IsLoaded { get; }

    public virtual StreamSessionHistory Load()
    {
        lock (_syncLock)
        {
            return JsonStoreClone.DeepClone(_state);
        }
    }

    public virtual StreamSessionHistory LoadVisible()
    {
        lock (_syncLock)
        {
            var visible = JsonStoreClone.DeepClone(_state);
            visible.Sessions.RemoveAll(session => session.IsHidden);
            return visible;
        }
    }

    public virtual void Append(StreamSessionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_syncLock)
        {
            if (!IsLoaded)
            {
                _logger?.LogWarning(
                    "StreamSessionHistoryStore: чтение {FilePath} сорвалось, история этого сеанса не сохраняется – сессия {SessionId} не записана",
                    _filePath,
                    record.Id);

                return;
            }

            var duplicate = _state.Sessions.Find(session =>
                string.Equals(session.Channel, record.Channel, StringComparison.OrdinalIgnoreCase)
                && session.StartedAt == record.StartedAt);

            if (duplicate != null)
            {
                _logger?.LogWarning(
                    "StreamSessionHistoryStore: сессия канала {Channel} со стартом {StartedAt:O} уже лежит в истории как {StoredId}, повторная запись {SessionId} отклонена",
                    record.Channel,
                    record.StartedAt,
                    duplicate.Id,
                    record.Id);

                return;
            }

            var stored = JsonStoreClone.DeepClone(record);
            stored.EnsureSegments();
            _state.Sessions.Add(stored);
            PersistInternal();

            _logger?.LogInformation("StreamSessionHistoryStore: добавлена сессия {SessionId} (всего сессий: {SessionCount})",
                record.Id,
                _state.Sessions.Count);
        }
    }

    public virtual bool TrySetHidden(Guid id, bool isHidden)
    {
        lock (_syncLock)
        {
            if (!IsLoaded)
            {
                _logger?.LogWarning(
                    "StreamSessionHistoryStore: чтение {FilePath} сорвалось, история этого сеанса не сохраняется – признак скрытия сессии {SessionId} не изменён",
                    _filePath,
                    id);

                return false;
            }

            var session = _state.Sessions.Find(item => item.Id == id);

            if (session == null)
            {
                _logger?.LogWarning("StreamSessionHistoryStore: сессия {SessionId} в истории не найдена, признак скрытия не изменён", id);
                return false;
            }

            if (session.IsHidden == isHidden)
            {
                return true;
            }

            var previous = session.IsHidden;
            session.IsHidden = isHidden;

            try
            {
                PersistInternal();
            }
            catch (Exception exception)
            {
                session.IsHidden = previous;

                _logger?.LogError(exception,
                    "Не удалось записать {FilePath}: признак скрытия сессии {SessionId} возвращён к прежнему, история в памяти совпадает с файлом",
                    _filePath,
                    id);

                return false;
            }

            _logger?.LogInformation("StreamSessionHistoryStore: сессия {SessionId} {HiddenState}",
                id,
                isHidden ? "скрыта из статистики" : "возвращена в статистику");

            return true;
        }
    }

    private void MergeInterruptedSessions()
    {
        var result = StreamSessionMerge.Merge(_state.Sessions);

        if (result.MergedPairs == 0)
        {
            return;
        }

        JsonStoreBackup.CreateBackup(_filePath, "premerge", _logger);
        _state.Sessions = [.. result.Sessions];

        try
        {
            PersistInternal();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception,
                "Не удалось записать склеенную историю в {FilePath}: склейка осталась только в памяти до ближайшей удачной записи",
                _filePath);

            return;
        }

        _logger?.LogInformation(
            "StreamSessionHistoryStore: склеено {MergedPairs} пар записей одного эфира, в истории осталось {SessionCount} сессий",
            result.MergedPairs,
            _state.Sessions.Count);
    }

    private void PersistInternal()
    {
        var json = JsonSerializer.Serialize(_state, JsonStoreOptions.Default);
        AtomicFile.Save(_filePath, json, _logger);
    }

    private StatisticsReadResult<StreamSessionHistory> ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger?.LogDebug("StreamSessionHistoryStore: файл {FilePath} не найден, используются дефолты", _filePath);
            return StatisticsReadResult<StreamSessionHistory>.NoFile;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var history = JsonSerializer.Deserialize<StreamSessionHistory>(json, JsonStoreOptions.Default);

            if (history == null)
            {
                _logger?.LogError("Файл {FilePath} разобран в пустое значение, история этого сеанса не сохраняется", _filePath);
                JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger);
                return StatisticsReadResult<StreamSessionHistory>.Failure;
            }

            foreach (var session in history.Sessions)
            {
                session.EnsureSegments();
            }

            return StatisticsReadResult<StreamSessionHistory>.FromFile(history);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Ошибка чтения {FilePath}, история этого сеанса не сохраняется", _filePath);
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger);
            return StatisticsReadResult<StreamSessionHistory>.Failure;
        }
    }
}
