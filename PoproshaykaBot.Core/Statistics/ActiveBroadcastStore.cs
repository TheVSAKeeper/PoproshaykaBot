using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Migrations.LegacyImport;
using PoproshaykaBot.Core.Settings.Stores;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PoproshaykaBot.Core.Statistics;

public class ActiveBroadcastStore : IExternalWriteParticipant
{
    private const string LegacyFileName = "active_session.json";

    private readonly BroadcastHistoryStore _historyStore;
    private readonly ILogger<ActiveBroadcastStore>? _logger;
    private readonly string _filePath;
    private readonly string _legacyFilePath;
    private readonly Lock _syncLock = new();

    private ActiveBroadcast? _currentDraft;
    private string? _lastSaveError;
    private bool _canWrite;
    private bool _suspendedCanWrite;
    private bool _invalidatedByExternalWrite;

    public ActiveBroadcastStore(
        BroadcastHistoryStore historyStore,
        ILogger<ActiveBroadcastStore>? logger = null,
        string? filePath = null,
        string? legacyFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(historyStore);

        _historyStore = historyStore;
        _logger = logger;
        _filePath = filePath ?? Path.Combine(AppPaths.BaseDirectory, LegacyDataCatalog.ActiveBroadcastFileName);
        _legacyFilePath = legacyFilePath ?? Path.Combine(AppPaths.BaseDirectory, LegacyFileName);

        var (state, loaded) = ReadDraftFile(_filePath);
        ReadState = state;

        switch (state)
        {
            case FileReadState.Failed:
                _canWrite = false;
                _currentDraft = null;
                break;

            case FileReadState.Loaded:
                _canWrite = true;
                _currentDraft = loaded;
                break;

            default:
                _canWrite = true;
                _currentDraft = TryMigrateLegacyDraft();
                break;
        }
    }

    public FileReadState ReadState { get; }

    public string? LastSaveError => Volatile.Read(ref _lastSaveError);

    public bool CanWrite
    {
        get
        {
            lock (_syncLock)
            {
                return _canWrite;
            }
        }
    }

    public ActiveBroadcastDiagnosticsSummary CaptureDiagnosticsSummary()
    {
        lock (_syncLock)
        {
            if (_currentDraft is not { } draft)
            {
                return new(ReadState, _canWrite, false, null, null, null, null, []);
            }

            var platforms = draft.Platforms
                .Select(static p => new ActiveBroadcastPlatformSummary(p.Platform, p.ChannelId, p.StreamId))
                .ToArray();

            return new(
                ReadState,
                _canWrite,
                true,
                draft.BroadcastId,
                draft.StartedAt,
                draft.UpdatedAt,
                draft.EndedAt,
                platforms);
        }
    }

    IReadOnlyCollection<string> IExternalWriteParticipant.OwnedRelativePaths { get; } =
    [
        LegacyDataCatalog.ActiveBroadcastFileName,
    ];

    ValueTask<bool> IExternalWriteParticipant.InvalidateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Invalidate());
    }

    ValueTask IExternalWriteParticipant.RestoreAsync(bool wasLoaded)
    {
        Restore(wasLoaded);
        return ValueTask.CompletedTask;
    }

    void IExternalWriteParticipant.LatchExternalRewrite()
    {
        lock (_syncLock)
        {
            _invalidatedByExternalWrite = true;
            _suspendedCanWrite = false;
            _canWrite = false;
        }
    }

    public virtual bool Invalidate()
    {
        lock (_syncLock)
        {
            if (!_invalidatedByExternalWrite)
            {
                _suspendedCanWrite = _canWrite;
                _invalidatedByExternalWrite = true;
            }

            var had = _canWrite;
            _canWrite = false;
            return had;
        }
    }

    public virtual void Restore(bool allowFutureWrites)
    {
        if (!allowFutureWrites)
        {
            return;
        }

        lock (_syncLock)
        {
            if (!_invalidatedByExternalWrite)
            {
                return;
            }

            _invalidatedByExternalWrite = false;
            _canWrite = _suspendedCanWrite;
        }
    }

    public virtual ActiveBroadcast? Load()
    {
        lock (_syncLock)
        {
            return _currentDraft == null ? null : JsonStoreClone.DeepClone(_currentDraft);
        }
    }

    public virtual bool TrySave(ActiveBroadcast draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        lock (_syncLock)
        {
            if (!_canWrite)
            {
                _lastSaveError = "Запись черновика эфира заблокирована до перезапуска.";
                return false;
            }

            if (_historyStore.IsBroadcastPersistedOnDisk(draft.BroadcastId))
            {
                return true;
            }

            if (_currentDraft is { } current && current.BroadcastId == draft.BroadcastId
                && ((current.EndedAt is not null && draft.EndedAt is null)
                    || ((current.EndedAt is null) == (draft.EndedAt is null)
                        && current.UpdatedAt > draft.UpdatedAt)))
            {
                return true;
            }

            if (_currentDraft is { EndedAt: not null } existing
                && existing.BroadcastId != draft.BroadcastId)
            {
                _historyStore.TryFlush();
                if (!_historyStore.IsBroadcastPersistedOnDisk(existing.BroadcastId))
                {
                    _lastSaveError = "Черновик нового эфира не сохраняется: предыдущий эфир ещё не записан в историю.";
                    _logger?.LogWarning(
                        "ActiveBroadcastStore: отказ перезаписи черновика {FilePath} – завершённый эфир {PendingId} ещё не записан в историю",
                        _filePath,
                        existing.BroadcastId);
                    return false;
                }
            }

            try
            {
                var snapshot = JsonStoreClone.DeepClone(draft);
                var json = JsonSerializer.Serialize(snapshot, JsonStoreOptions.Default);
                AtomicFile.Save(_filePath, json, _logger);
                _currentDraft = snapshot;
                _lastSaveError = null;
                return true;
            }
            catch (Exception exception)
            {
                _lastSaveError = exception.Message;
                _logger?.LogWarning(exception, "ActiveBroadcastStore: не удалось сохранить черновик {FilePath}", _filePath);
                return false;
            }
        }
    }

    public bool TryDelete(Guid expectedBroadcastId)
    {
        lock (_syncLock)
        {
            return (_currentDraft is null || _currentDraft.BroadcastId == expectedBroadcastId) && TryDelete();
        }
    }

    public virtual bool TryDelete()
    {
        lock (_syncLock)
        {
            if (!_canWrite)
            {
                return false;
            }

            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }

                _currentDraft = null;
                _lastSaveError = null;
                return true;
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "ActiveBroadcastStore: не удалось удалить черновик {FilePath}", _filePath);
                return false;
            }
        }
    }

    private (FileReadState State, ActiveBroadcast? Draft) ReadDraftFile(string path)
    {
        if (!File.Exists(path))
        {
            return (FileReadState.Missing, null);
        }

        try
        {
            var json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger?.LogError("ActiveBroadcastStore: файл черновика {FilePath} пуст, запись заблокирована", path);
                JsonStoreBackup.TryCreateBackup(path, "invalid", _logger, null, out _);
                return (FileReadState.Failed, null);
            }

            var draft = JsonSerializer.Deserialize<ActiveBroadcast>(json, JsonStoreOptions.Default);

            if (draft == null)
            {
                _logger?.LogError("ActiveBroadcastStore: файл черновика {FilePath} разобран в null, запись заблокирована", path);
                JsonStoreBackup.TryCreateBackup(path, "invalid", _logger, null, out _);
                return (FileReadState.Failed, null);
            }

            return (FileReadState.Loaded, draft);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "ActiveBroadcastStore: ошибка чтения черновика {FilePath}, запись заблокирована", path);
            JsonStoreBackup.TryCreateBackup(path, "invalid", _logger, null, out _);
            return (FileReadState.Failed, null);
        }
    }

    private ActiveBroadcast? TryMigrateLegacyDraft()
    {
        if (!File.Exists(_legacyFilePath))
        {
            return null;
        }

        ActiveStreamSession? legacy;

        try
        {
            var json = File.ReadAllText(_legacyFilePath);

            if (string.IsNullOrWhiteSpace(json))
            {
                JsonStoreBackup.TryCreateBackup(_legacyFilePath, "invalid", _logger, null, out _);
                return null;
            }

            legacy = JsonSerializer.Deserialize<ActiveStreamSession>(json, JsonStoreOptions.Default);

            if (legacy == null)
            {
                JsonStoreBackup.TryCreateBackup(_legacyFilePath, "invalid", _logger, null, out _);
                return null;
            }
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "ActiveBroadcastStore: ошибка чтения старого черновика {FilePath}", _legacyFilePath);
            JsonStoreBackup.TryCreateBackup(_legacyFilePath, "invalid", _logger, null, out _);
            return null;
        }

        if (_historyStore.HasPersistedOrImportedTwitchSession(legacy.Channel, legacy.StartedAt))
        {
            TryDeleteFile(_legacyFilePath);
            return null;
        }

        var broadcastId = ComputeDeterministicBroadcastId(legacy);
        var migrated = ConvertLegacyDraft(legacy, broadcastId);

        try
        {
            var json = JsonSerializer.Serialize(migrated, JsonStoreOptions.Default);
            AtomicFile.Save(_filePath, json, _logger);
            TryDeleteFile(_legacyFilePath);
            return migrated;
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "ActiveBroadcastStore: не удалось записать мигрированный черновик {FilePath}", _filePath);
            return null;
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            _logger?.LogWarning(exception, "ActiveBroadcastStore: не удалось удалить {FilePath}", path);
        }
    }

    internal static Guid ComputeDeterministicBroadcastId(ActiveStreamSession legacy)
    {
        var input = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"active-session-v1|{legacy.Channel.Trim().ToLowerInvariant()}|{legacy.StartedAt.UtcTicks}|{legacy.StreamId ?? string.Empty}");

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(input), hash);
        return new(hash[..16]);
    }

    private static ActiveBroadcast ConvertLegacyDraft(ActiveStreamSession legacy, Guid broadcastId)
    {
        var segments = legacy.Segments.Count > 0
            ? legacy.Segments
                .Select(static s => new ActiveStreamSessionSegment
                {
                    StartedAt = s.StartedAt,
                    EndedAt = s.EndedAt,
                    Title = s.Title,
                    Game = s.Game,
                    MessageCount = s.MessageCount,
                    PeakViewers = s.PeakViewers,
                    ViewerSamplesSum = s.ViewerSamplesSum,
                    ViewerSamplesCount = s.ViewerSamplesCount,
                })
                .ToList()
            :
            [
                new ActiveStreamSessionSegment
                {
                    StartedAt = legacy.StartedAt,
                    Title = legacy.Title,
                    Game = legacy.Game,
                    MessageCount = legacy.MessageCount,
                    PeakViewers = legacy.PeakViewers,
                    ViewerSamplesSum = legacy.ViewerSamplesSum,
                    ViewerSamplesCount = legacy.ViewerSamplesCount,
                },
            ];

        var average = legacy.ViewerSamplesCount > 0
            ? (int)Math.Round((double)legacy.ViewerSamplesSum / legacy.ViewerSamplesCount, MidpointRounding.AwayFromZero)
            : legacy.PeakViewers;

        return new()
        {
            BroadcastId = broadcastId,
            StartedAt = legacy.StartedAt,
            UpdatedAt = legacy.UpdatedAt,
            EndedAt = null,
            Platforms =
            [
                new ActiveBroadcastPlatform
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = legacy.Channel,
                    StreamId = legacy.StreamId,
                    StartedAt = legacy.StartedAt,
                    UpdatedAt = legacy.UpdatedAt,
                    Title = legacy.Title,
                    Game = legacy.Game,
                    PeakViewers = legacy.PeakViewers,
                    ViewerSamplesSum = legacy.ViewerSamplesSum,
                    ViewerSamplesCount = legacy.ViewerSamplesCount,
                    LastViewerCount = legacy.ViewerSamplesCount > 0 ? average : null,
                    LastViewerSampleAt = legacy.ViewerSamplesCount > 0 ? legacy.UpdatedAt : null,
                    Chatters =
                    [
                        .. legacy.Chatters.Select(static c => new ActiveStreamSessionChatter
                        {
                            UserId = c.UserId,
                            DisplayName = c.DisplayName,
                            MessageCount = c.MessageCount,
                        }),
                    ],
                    Segments = segments,
                    TrackedIntervals = legacy.TrackedIntervals?
                        .Select(static i => new ActiveStreamSessionInterval
                        {
                            StartedAt = i.StartedAt,
                            EndedAt = i.EndedAt,
                        })
                        .ToList(),
                },
            ],
        };
    }
}
