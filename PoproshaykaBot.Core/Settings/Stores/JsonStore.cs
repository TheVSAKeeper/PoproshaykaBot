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
    private readonly Func<T, string>? _describe;
    private readonly Lock _syncLock = new();

    private T _state;

    public JsonStore(
        string filePath,
        ILogger? logger = null,
        Func<string, string>? backupRedactor = null,
        Func<string, T?>? parser = null,
        Func<T, string>? describe = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);

        _filePath = filePath;
        _logger = logger;
        _backupRedactor = backupRedactor;
        _parser = parser;
        _describe = describe;
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

    public bool VerifyPersisted(Func<T, T, bool> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        string reason;
        lock (_syncLock)
        {
            try
            {
                var json = File.ReadAllText(_filePath);
                var persisted = JsonSerializer.Deserialize<T>(json, JsonStoreOptions.Default);
                if (persisted is not null && matches(_state, persisted))
                {
                    return true;
                }

                reason = "файл отличается от ожидаемого состояния";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                reason = exception.GetType().Name;
            }
        }

        _logger?.LogWarning("Не удалось подтвердить запись {FilePath}: {Reason}", _filePath, reason);
        return false;
    }

    public bool Save(T value, Func<T, bool>? matches = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        Exception? failure;
        var snapshot = JsonStoreClone.DeepClone(value);

        lock (_syncLock)
        {
            if (matches is not null && !matches(_state))
            {
                return false;
            }

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

        LogSaved(LogLevel.Information, "замена", snapshot);

        return true;
    }

    public bool Mutate(Action<T> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        MutateCore(state =>
            {
                mutator(state);
                return true;
            },
            _ => true,
            out var written);

        return written;
    }

    public TResult Mutate<TResult>(Func<T, TResult> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        return MutateCore(mutator, _ => true, out _);
    }

    public bool MutateIf(Func<T, bool> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        return MutateCore(mutator, applied => applied, out _);
    }

    public (bool Applied, bool Written) MutateIfWithResult(Func<T, bool> mutator)
    {
        ArgumentNullException.ThrowIfNull(mutator);

        var applied = MutateCore(mutator, static result => result, out var written);
        return (applied, written);
    }

    private TResult MutateCore<TResult>(Func<T, TResult> mutator, Func<TResult, bool> shouldWrite, out bool written)
    {
        TResult result;
        Exception? failure = null;
        T draft;

        written = false;

        lock (_syncLock)
        {
            draft = JsonStoreClone.DeepClone(_state);
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
            LogSaved(LogLevel.Debug, "мутация", draft);
        }

        return result;
    }

    private void LogSaved(LogLevel level, string operation, T state)
    {
        var logger = _logger;

        if (logger?.IsEnabled(level) != true)
        {
            return;
        }

        if (_describe == null)
        {
            logger.Log(level, "Сохранён {Store} ({Operation}) → {FilePath}", typeof(T).Name, operation, _filePath);
            return;
        }

        string description;

        try
        {
            description = _describe(state);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Описание {Store} не построено", typeof(T).Name);
            description = "описание недоступно";
        }

        logger.Log(level, "Сохранён {Store} ({Operation}): {Description}", typeof(T).Name, operation, description);
    }

    private Exception? TryWrite(string json)
    {
        try
        {
            AtomicFile.Save(_filePath, json, _logger, keepBackup: _backupRedactor == null);
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
