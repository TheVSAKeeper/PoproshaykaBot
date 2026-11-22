using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoproshaykaBot.Core.Infrastructure.Runtime;

public sealed class RunStateStore(string? filePath = null, TimeProvider? timeProvider = null)
{
    public const string FileName = "run-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath = filePath ?? AppPaths.Combine(FileName);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Lock _sync = new();

    private RunStateRecord? _current;

    public string FilePath => _filePath;

    public PreviousRunState BeginSession(string version, string? commit = null, ILogger? logger = null)
    {
        var previous = ReadPrevious(logger);
        var now = _timeProvider.GetUtcNow();
        var record = new RunStateRecord(
            Environment.ProcessId,
            now,
            string.IsNullOrWhiteSpace(version) ? "unknown" : version.Trim(),
            string.IsNullOrWhiteSpace(commit) ? null : commit.Trim(),
            RunStateStatus.Running,
            null,
            now);

        lock (_sync)
        {
            _current = record;
            TryPersistLocked(record, logger);
        }

        return previous;
    }

    public void MarkFailure(RunStateStatus status, Exception? exception = null, ILogger? logger = null)
    {
        var now = _timeProvider.GetUtcNow();
        var exceptionAnchor = FormatExceptionAnchor(exception);

        lock (_sync)
        {
            var baseRecord = _current ?? new RunStateRecord(
                Environment.ProcessId,
                now,
                "unknown",
                null,
                status,
                exceptionAnchor,
                now);

            var updated = baseRecord with
            {
                Status = status,
                ExceptionType = exceptionAnchor ?? baseRecord.ExceptionType,
                UpdatedAtUtc = now,
            };

            _current = updated;
            TryPersistLocked(updated, logger);
        }
    }

    public void ClearCleanExit(ILogger? logger = null)
    {
        lock (_sync)
        {
            _current = null;

            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning(exception, "Не удалось удалить маркер запуска {FilePath}", _filePath);
            }
        }
    }

    internal static string? FormatExceptionAnchor(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        var typeName = exception.GetType().FullName ?? exception.GetType().Name;
        var frame = ExtractTopFrame(exception);

        return string.IsNullOrEmpty(frame) ? typeName : $"{typeName} @ {frame}";
    }

    internal static string? ExtractTopFrame(Exception exception)
    {
        try
        {
            var trace = new StackTrace(exception, fNeedFileInfo: false);

            foreach (var frame in trace.GetFrames())
            {
                var method = frame.GetMethod();

                if (method is null)
                {
                    continue;
                }

                var owner = method.DeclaringType?.Name;

                return string.IsNullOrEmpty(owner) ? method.Name : $"{owner}.{method.Name}";
            }
        }
        catch (Exception)
        {
            return null;
        }

        return null;
    }

    private PreviousRunState ReadPrevious(ILogger? logger)
    {
        if (!File.Exists(_filePath))
        {
            return PreviousRunState.Clean;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            var record = JsonSerializer.Deserialize<RunStateRecord>(json, JsonOptions);

            return new(WasUnclean: true, record);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger?.LogWarning(exception, "Обнаружен маркер прошлого запуска {FilePath}, но прочитать его не удалось", _filePath);

            return new(WasUnclean: true, Record: null);
        }
    }

    private void TryPersistLocked(RunStateRecord record, ILogger? logger)
    {
        try
        {
            var json = JsonSerializer.Serialize(record, JsonOptions);
            AtomicFile.Save(_filePath, json, logger, keepBackup: false);
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Не удалось записать маркер запуска {FilePath}", _filePath);
        }
    }
}
