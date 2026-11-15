using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PoproshaykaBot.Core.YouTube.Quota;

internal sealed class YouTubeQuotaStore(
    ILogger<YouTubeQuotaStore> logger,
    string? filePath = null,
    Action<string, string>? persistFile = null)
    : IExternalWriteParticipant, IFlushableHistoryStore, IDisposable
{
    private readonly string _filePath = filePath ?? Path.Combine(AppPaths.BaseDirectory, LegacyDataCatalog.YouTubeQuotaFileName);
    private readonly Lock _sync = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private StatisticsLoadState _readState = StatisticsLoadState.NotRead;
    private YouTubeQuotaReliability _reliability = YouTubeQuotaReliability.Reliable;
    private YouTubeQuotaDocument _latestDocument = YouTubeQuotaDocument.CreateEmpty();
    private long _latestSnapshotSequence;
    private long _snapshotVersion;
    private bool _canPersist;
    private bool _rewrittenExternally;
    private bool _hasUnsavedChanges;
    private bool _disposed;

    public string FilePath => _filePath;

    public YouTubeQuotaReliability Reliability
    {
        get
        {
            lock (_sync)
            {
                return _reliability;
            }
        }
    }

    public bool CanWriteFile
    {
        get
        {
            lock (_sync)
            {
                return _canPersist;
            }
        }
    }

    IReadOnlyCollection<string> IExternalWriteParticipant.OwnedRelativePaths { get; } = [LegacyDataCatalog.YouTubeQuotaFileName];

    public YouTubeQuotaDocument EnsureLoaded()
    {
        if (_disposed)
        {
            lock (_sync)
            {
                return _latestDocument.Clone();
            }
        }

        try
        {
            _writeGate.Wait();
        }
        catch (ObjectDisposedException)
        {
            lock (_sync)
            {
                return _latestDocument.Clone();
            }
        }

        try
        {
            return EnsureLoadedUnderWriteGate();
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public bool SaveSnapshot(YouTubeQuotaDocument snapshot, long sequence = 0)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_sync)
        {
            if (sequence > 0 && sequence < _latestSnapshotSequence)
            {
                return false;
            }

            if (sequence > _latestSnapshotSequence)
            {
                _latestSnapshotSequence = sequence;
            }

            _latestDocument = snapshot.Clone();
            _snapshotVersion++;
            _hasUnsavedChanges = true;

            if (!_canPersist)
            {
                return false;
            }
        }

        return FlushUnsavedChanges();
    }

    public bool TryFlush()
    {
        return FlushUnsavedChanges();
    }

    async ValueTask<bool> IExternalWriteParticipant.InvalidateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            EnsureLoadedUnderWriteGate();

            lock (_sync)
            {
                if (!_canPersist)
                {
                    return false;
                }

                _canPersist = false;
                _rewrittenExternally = true;

                logger.LogInformation(
                    "YouTubeQuotaStore: файл {FilePath} изменяется внешней записью, сохранение квоты приостановлено",
                    _filePath);

                return true;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private YouTubeQuotaDocument EnsureLoadedUnderWriteGate()
    {
        lock (_sync)
        {
            if (_readState != StatisticsLoadState.NotRead)
            {
                return _latestDocument.Clone();
            }

            if (!File.Exists(_filePath))
            {
                _readState = StatisticsLoadState.Loaded;
                _reliability = YouTubeQuotaReliability.Reliable;
                _canPersist = !_rewrittenExternally;
                _latestDocument = YouTubeQuotaDocument.CreateEmpty();
                return _latestDocument.Clone();
            }

            var loaded = TryLoadFromDisk(out var document, out var backupPath);

            if (!loaded)
            {
                _readState = StatisticsLoadState.Abandoned;
                _reliability = YouTubeQuotaReliability.CorruptFile;
                _canPersist = false;
                _latestDocument = YouTubeQuotaDocument.CreateEmpty();

                logger.LogError(
                    "YouTubeQuotaStore: файл квоты {FilePath} повреждён (резервная копия: {BackupPath}). Перезапись на диск отключена до перезапуска",
                    _filePath,
                    backupPath ?? "-");

                return _latestDocument.Clone();
            }

            _readState = StatisticsLoadState.Loaded;
            _reliability = YouTubeQuotaReliability.Reliable;
            _canPersist = !_rewrittenExternally;
            _latestDocument = document;
            return _latestDocument.Clone();
        }
    }

    ValueTask IExternalWriteParticipant.RestoreAsync(bool wasLoaded)
    {
        if (!wasLoaded)
        {
            return ValueTask.CompletedTask;
        }

        lock (_sync)
        {
            if (_readState == StatisticsLoadState.Abandoned)
            {
                return ValueTask.CompletedTask;
            }

            _canPersist = true;
            _rewrittenExternally = false;
        }

        FlushUnsavedChanges();
        return ValueTask.CompletedTask;
    }

    void IExternalWriteParticipant.LatchExternalRewrite()
    {
        lock (_sync)
        {
            _canPersist = false;
            _rewrittenExternally = true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeGate.Dispose();
    }

    private bool FlushUnsavedChanges()
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            _writeGate.Wait();
        }
        catch (ObjectDisposedException)
        {
            return false;
        }

        try
        {
            while (true)
            {
                YouTubeQuotaDocument snapshot;
                long snapshotVersion;

                lock (_sync)
                {
                    if (!_canPersist)
                    {
                        return !_hasUnsavedChanges;
                    }

                    if (!_hasUnsavedChanges)
                    {
                        return true;
                    }

                    snapshot = _latestDocument.Clone();
                    snapshotVersion = _snapshotVersion;
                }

                var json = JsonSerializer.Serialize(snapshot, YouTubeQuotaDocument.SerializerOptions);

                try
                {
                    if (persistFile is not null)
                    {
                        persistFile(_filePath, json);
                    }
                    else
                    {
                        AtomicFile.Save(_filePath, json, logger);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "YouTubeQuotaStore: не удалось сохранить {FilePath}", _filePath);
                    return false;
                }

                lock (_sync)
                {
                    if (!_canPersist)
                    {
                        return !_hasUnsavedChanges;
                    }

                    if (_snapshotVersion == snapshotVersion)
                    {
                        _hasUnsavedChanges = false;
                        return true;
                    }
                }
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private bool TryLoadFromDisk(out YouTubeQuotaDocument document, out string? backupPath)
    {
        document = YouTubeQuotaDocument.CreateEmpty();
        backupPath = null;

        try
        {
            var raw = File.ReadAllText(_filePath, Encoding.UTF8);

            if (string.IsNullOrWhiteSpace(raw)
                || string.Equals(raw.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            {
                JsonStoreBackup.TryCreateBackup(_filePath, "invalid", logger, null, out backupPath);
                return false;
            }

            var parsed = JsonSerializer.Deserialize<YouTubeQuotaDocument>(raw, YouTubeQuotaDocument.SerializerOptions);

            if (!IsValidDocument(parsed, out var normalized))
            {
                JsonStoreBackup.TryCreateBackup(_filePath, "invalid", logger, null, out backupPath);
                return false;
            }

            document = normalized;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            JsonStoreBackup.TryCreateBackup(_filePath, "invalid", logger, null, out backupPath);
            return false;
        }
    }

    private static bool IsValidDocument(YouTubeQuotaDocument? parsed, out YouTubeQuotaDocument normalized)
    {
        normalized = YouTubeQuotaDocument.CreateEmpty();

        if (parsed is null
            || parsed.SchemaVersion != YouTubeQuotaDocument.CurrentSchemaVersion
            || parsed.Projects is null)
        {
            return false;
        }

        foreach (var (key, record) in parsed.Projects)
        {
            if (string.IsNullOrWhiteSpace(key) || record is null)
            {
                return false;
            }

            if (record.DailyBudget <= 0
                || record.CommittedUnits < 0
                || record.BotCommittedUnits < 0
                || record.BroadcasterCommittedUnits < 0)
            {
                return false;
            }

            if (!DateOnly.TryParseExact(
                    record.PacificDate,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                return false;
            }

            normalized.Projects[key.Trim().ToLowerInvariant()] = record.Clone();
        }

        return true;
    }
}
