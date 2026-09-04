using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace PoproshaykaBot.Core.Settings.Stores;

internal sealed class JsonStore<T>
    where T : class, new()
{
    private readonly string _filePath;
    private readonly ILogger? _logger;
    private readonly Func<string, string>? _backupRedactor;
    private readonly Func<string, T?>? _parser;
    private readonly object _syncLock = new();

    private T _state;

    public JsonStore(
        string filePath,
        ILogger? logger = null,
        Func<string, string>? backupRedactor = null,
        Func<string, T?>? parser = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        _filePath = filePath;
        _logger = logger;
        _backupRedactor = backupRedactor;
        _parser = parser;
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

        Exception? failure;

        lock (_syncLock)
        {
            var snapshot = JsonStoreClone.DeepClone(value);
            var json = JsonSerializer.Serialize(snapshot, JsonStoreOptions.Default);
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

        MutateCore(state =>
            {
                mutator(state);
                return true;
            },
            _ => true);
    }

    public TResult Mutate<TResult>(Func<T, TResult> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        return MutateCore(mutator, _ => true);
    }

    public bool MutateIf(Func<T, bool> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        return MutateCore(mutator, applied => applied);
    }

    private TResult MutateCore<TResult>(Func<T, TResult> mutator, Func<TResult, bool> shouldWrite)
    {
        TResult result;
        Exception? failure = null;
        var written = false;

        lock (_syncLock)
        {
            var draft = JsonStoreClone.DeepClone(_state);
            result = mutator(draft);

            if (shouldWrite(result))
            {
                var json = JsonSerializer.Serialize(draft, JsonStoreOptions.Default);
                failure = TryWrite(json);

                if (failure == null)
                {
                    _state = draft;
                    written = true;
                }
            }
        }

        if (failure != null)
        {
            ReportFailure(failure);
        }

        if (written)
        {
            _logger?.LogDebug("Применена мутация, состояние сохранено в {FilePath}", _filePath);
        }

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

            var state = _parser == null
                ? JsonSerializer.Deserialize<T>(json, JsonStoreOptions.Default)
                : _parser(json);

            return state ?? new();
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "Ошибка чтения {FilePath}, применяются дефолты", _filePath);
            JsonStoreBackup.CreateBackup(_filePath, "invalid", _logger, _backupRedactor);
            return new();
        }
    }
}
