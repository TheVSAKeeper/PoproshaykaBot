using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

// TODO: ограничение new() не пропустит стораджи с приватным вложенным DTO (AccountsStore,
//  DashboardLayoutStore, RecentCategoriesStore); заводить фабрику дефолта параметром, когда дойдёт
//  очередь переводить первый из них
internal sealed class JsonStore<T>
    where T : class, new()
{
    private readonly string _filePath;
    private readonly ILogger? _logger;
    private readonly Func<string, string>? _backupRedactor;
    private readonly object _syncLock = new();

    private T _state;

    public JsonStore(string filePath, ILogger? logger = null, Func<string, string>? backupRedactor = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        _filePath = filePath;
        _logger = logger;
        _backupRedactor = backupRedactor;
        _state = ReadFile();
    }

    public event EventHandler<JsonStoreWriteFailedEventArgs>? WriteFailed;

    public T Load()
    {
        lock (_syncLock)
        {
            return JsonStoreClone.DeepClone(_state);
        }
    }

    public void Save(T value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var snapshot = JsonStoreClone.DeepClone(value);
        var json = JsonSerializer.Serialize(snapshot, JsonStoreOptions.Default);
        Exception? failure;

        lock (_syncLock)
        {
            failure = TryWrite(json);

            if (failure == null)
            {
                _state = snapshot;
            }
        }

        if (failure != null)
        {
            ReportFailure(failure);
        }

        _logger?.LogInformation("Состояние заменено целиком и сохранено в {FilePath}", _filePath);
    }

    public void Mutate(Action<T> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        Mutate(state =>
        {
            mutator(state);
            return true;
        });
    }

    public TResult Mutate<TResult>(Func<T, TResult> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        TResult result;
        Exception? failure;

        lock (_syncLock)
        {
            var draft = JsonStoreClone.DeepClone(_state);
            result = mutator(draft);

            var json = JsonSerializer.Serialize(draft, JsonStoreOptions.Default);
            failure = TryWrite(json);

            if (failure == null)
            {
                _state = draft;
            }
        }

        if (failure != null)
        {
            ReportFailure(failure);
        }

        _logger?.LogDebug("Применена мутация, состояние сохранено в {FilePath}", _filePath);
        return result;
    }

    private Exception? TryWrite(string json)
    {
        try
        {
            AtomicFile.Save(_filePath, json, _logger);
            return null;
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception,
                "Ошибка записи {FilePath}, в памяти осталась последняя записанная на диск версия",
                _filePath);

            return exception;
        }
    }

    [DoesNotReturn]
    private void ReportFailure(Exception failure)
    {
        var handler = WriteFailed;

        if (handler != null)
        {
            try
            {
                handler(this, new(_filePath, failure));
            }
            catch (Exception handlerException)
            {
                _logger?.LogError(handlerException,
                    "Подписчик на сбой записи {FilePath} сам завершился ошибкой",
                    _filePath);
            }
        }

        ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private T ReadFile()
    {
        if (!File.Exists(_filePath))
        {
            _logger?.LogDebug("Файл {FilePath} не найден, используются дефолты", _filePath);
            return new();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<T>(json, JsonStoreOptions.Default) ?? new();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Ошибка чтения {FilePath}, применяются дефолты", _filePath);
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger, _backupRedactor);
            return new();
        }
    }
}
