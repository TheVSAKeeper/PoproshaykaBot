using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Json;

namespace PoproshaykaBot.Core.Statistics;

public class BroadcastHistoryStore : IFlushableHistoryStore
{
    private readonly ILogger<BroadcastHistoryStore>? _logger;
    private readonly string _broadcastsPath;
    private readonly string _projectionPath;
    private readonly Lock _syncLock = new();

    private BroadcastHistoryDocument _state = new();
    private HashSet<Guid> _persistedSourceIds = [];
    private List<StreamSessionRecord> _lastPersistedProjection = [];

    private FileReadState _sourceReadState;
    private FileReadState _projectionReadState;
    private bool _canWriteSource;
    private bool _canWriteProjection;
    private bool _sourceDirty;
    private bool _projectionDirty;

    public BroadcastHistoryStore(
        ILogger<BroadcastHistoryStore>? logger = null,
        string? broadcastsFilePath = null,
        string? streamSessionsFilePath = null)
    {
        _logger = logger;
        _broadcastsPath = broadcastsFilePath ?? Path.Combine(AppPaths.BaseDirectory, SettingsFileNames.BroadcastsFileName);
        _projectionPath = streamSessionsFilePath ?? Path.Combine(AppPaths.BaseDirectory, SettingsFileNames.StreamSessionsFileName);

        var sourceRead = ReadRawDocument<BroadcastHistoryDocument>(_broadcastsPath, "история эфиров");
        var projectionRead = ReadRawDocument<StreamSessionHistory>(_projectionPath, "проекция сессий Twitch");

        _sourceReadState = sourceRead.State;
        _projectionReadState = projectionRead.State;

        InitializeFromReads(sourceRead, projectionRead);

        _logger?.LogDebug(
            "BroadcastHistoryStore инициализирован (broadcasts={SourceState}, projection={ProjectionState}, эфиров={Count}, запись источника={CanWriteSource}, запись проекции={CanWriteProjection})",
            _sourceReadState,
            _projectionReadState,
            _state.Broadcasts.Count,
            _canWriteSource,
            _canWriteProjection);
    }

    public FileReadState SourceReadState
    {
        get
        {
            lock (_syncLock)
            {
                return _sourceReadState;
            }
        }
    }

    public FileReadState ProjectionReadState
    {
        get
        {
            lock (_syncLock)
            {
                return _projectionReadState;
            }
        }
    }

    public bool CanWriteSource
    {
        get
        {
            lock (_syncLock)
            {
                return _canWriteSource;
            }
        }
    }

    public bool CanWriteProjection
    {
        get
        {
            lock (_syncLock)
            {
                return _canWriteProjection;
            }
        }
    }

    public bool IsLoaded => CanWriteSource;

    public bool HasPendingFlush
    {
        get
        {
            lock (_syncLock)
            {
                return (_sourceDirty && _canWriteSource) || (_projectionDirty && _canWriteProjection);
            }
        }
    }

    public BroadcastHistoryDiagnosticsSummary CaptureDiagnosticsSummary(int maxRecent = 8)
    {
        var limit = Math.Max(0, maxRecent);

        lock (_syncLock)
        {
            var recent = limit == 0
                ? Array.Empty<BroadcastHistoryRecentItemSummary>()
                : _state.Broadcasts
                    .OrderByDescending(static b => b.StartedAt)
                    .Take(limit)
                    .Select(static b =>
                    {
                        var primary = b.PrimaryPlatform;
                        var segments = primary?.Segments ?? [];
                        var segmentsTotal = segments.Aggregate(TimeSpan.Zero, static (sum, segment) => sum + segment.Duration);

                        return new BroadcastHistoryRecentItemSummary(
                            b.Id,
                            [.. b.Platforms.Select(static p => p.Platform).Distinct()],
                            primary?.ChannelId,
                            b.StartedAt,
                            b.EndedAt,
                            segments.Count,
                            segmentsTotal,
                            b.TrackedDuration,
                            b.IsHidden);
                    })
                    .ToArray();

            return new(
                SourceReadState,
                ProjectionReadState,
                _canWriteSource,
                _canWriteProjection,
                _state.ProjectionOutOfDate,
                (_sourceDirty && _canWriteSource) || (_projectionDirty && _canWriteProjection),
                _state.Broadcasts.Count,
                recent);
        }
    }

    public virtual BroadcastHistoryDocument Load()
    {
        lock (_syncLock)
        {
            return JsonStoreClone.DeepClone(_state);
        }
    }

    public virtual BroadcastHistoryDocument LoadVisible()
    {
        lock (_syncLock)
        {
            var visible = JsonStoreClone.DeepClone(_state);
            visible.Broadcasts.RemoveAll(static b => b.IsHidden);
            return visible;
        }
    }

    public StreamSessionHistory LoadSessionsProjection()
    {
        lock (_syncLock)
        {
            return BuildProjectionLocked();
        }
    }

    public bool IsBroadcastPersistedOnDisk(Guid broadcastId)
    {
        lock (_syncLock)
        {
            return _persistedSourceIds.Contains(broadcastId);
        }
    }

    public bool HasPersistedOrImportedTwitchSession(string channel, DateTimeOffset startedAt)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return false;
        }

        lock (_syncLock)
        {
            foreach (var broadcast in _state.Broadcasts)
            {
                foreach (var platform in broadcast.Platforms)
                {
                    if (platform.Platform == StreamingPlatform.Twitch
                        && string.Equals(platform.Channel, channel, StringComparison.OrdinalIgnoreCase)
                        && (platform.StartedAt == startedAt
                            || platform.MergedStartedAts?.Contains(startedAt) == true
                            || platform.Segments.Exists(segment => segment.StartedAt == startedAt)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public virtual bool TryAppend(BroadcastRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_syncLock)
        {
            var existingIndex = _state.Broadcasts.FindIndex(item => item.Id == record.Id
                || item.MergedBroadcastIds?.Contains(record.Id) == true);

            if (existingIndex >= 0)
            {
                if (_persistedSourceIds.Contains(record.Id))
                {
                    return true;
                }

                if (!_canWriteSource)
                {
                    return false;
                }

                return TrySaveSourceAndProjectionLocked();
            }

            var stored = JsonStoreClone.DeepClone(record);
            stored.EnsureSegments();
            _state.Broadcasts.Add(stored);
            _sourceDirty = true;
            _projectionDirty = stored.TryProjectToTwitchSession(out _) || _projectionDirty;

            if (!_canWriteSource)
            {
                _logger?.LogWarning(
                    "BroadcastHistoryStore: запись {BroadcastId} принята только в память (нет права записи источника {FilePath})",
                    record.Id,
                    _broadcastsPath);

                return false;
            }

            var sourceSaved = TrySaveSourceAndProjectionLocked();

            if (sourceSaved)
            {
                _logger?.LogInformation(
                    "BroadcastHistoryStore: добавлен эфир {BroadcastId} (всего эфиров: {Count})",
                    record.Id,
                    _state.Broadcasts.Count);
            }

            return sourceSaved;
        }
    }

    public virtual bool TrySetHidden(Guid id, bool isHidden)
    {
        lock (_syncLock)
        {
            var broadcast = _state.Broadcasts.Find(item => item.Id == id);

            if (broadcast == null)
            {
                _logger?.LogWarning("BroadcastHistoryStore: эфир {BroadcastId} не найден, признак скрытия не изменён", id);
                return false;
            }

            if (broadcast.IsHidden == isHidden)
            {
                return true;
            }

            var previous = broadcast.IsHidden;
            var hadSourceDirty = _sourceDirty;
            var hadProjectionDirty = _projectionDirty;
            var affectsProjection = broadcast.TryProjectToTwitchSession(out _);

            broadcast.IsHidden = isHidden;
            _sourceDirty = true;

            if (affectsProjection)
            {
                _projectionDirty = true;
            }

            if (!_canWriteSource)
            {
                _logger?.LogWarning(
                    "BroadcastHistoryStore: нет права записи {FilePath}, скрытие эфира {BroadcastId} применено только в памяти",
                    _broadcastsPath,
                    id);

                return true;
            }

            if (!TrySaveSourceLocked())
            {
                broadcast.IsHidden = previous;
                _sourceDirty = hadSourceDirty;
                _projectionDirty = hadProjectionDirty;
                return false;
            }

            if (_projectionDirty && _canWriteProjection)
            {
                TrySaveProjectionLocked();
            }

            _logger?.LogInformation(
                "BroadcastHistoryStore: эфир {BroadcastId} {HiddenState}",
                id,
                isHidden ? "скрыт из статистики" : "возвращён в статистику");

            return true;
        }
    }

    public virtual bool TryFlush()
    {
        lock (_syncLock)
        {
            return TryFlushLocked();
        }
    }

    private void InitializeFromReads(
        RawReadResult<BroadcastHistoryDocument> sourceRead,
        RawReadResult<StreamSessionHistory> projectionRead)
    {
        if (sourceRead.State == FileReadState.Failed
            || (sourceRead.State == FileReadState.Missing && projectionRead.State == FileReadState.Failed))
        {
            _canWriteSource = false;
            _canWriteProjection = false;
            _state = new();
            return;
        }

        if (sourceRead.State == FileReadState.Missing && projectionRead.State == FileReadState.Missing)
        {
            _canWriteSource = true;
            _canWriteProjection = true;
            _state = new();
            return;
        }

        if (sourceRead.State == FileReadState.Missing && projectionRead.State == FileReadState.Loaded)
        {
            _canWriteSource = true;
            _canWriteProjection = true;
            ReconcileFromProjectionOnly(projectionRead.Document!);
            return;
        }

        var sourceDoc = sourceRead.Document!;
        sourceDoc.Broadcasts ??= [];

        foreach (var item in sourceDoc.Broadcasts)
        {
            item.EnsureSegments();
        }

        _persistedSourceIds = [.. sourceDoc.Broadcasts.SelectMany(static b => (b.MergedBroadcastIds ?? []).Prepend(b.Id))];
        _canWriteSource = true;

        if (projectionRead.State == FileReadState.Missing)
        {
            _canWriteProjection = true;
            _state = sourceDoc;
            MergeLoadedSourceLocked(createPremergeBackup: true);
            _projectionDirty = true;
            TryFlushLocked();
            return;
        }

        if (projectionRead.State == FileReadState.Failed)
        {
            _canWriteProjection = projectionRead.BackupSucceeded;
            _state = sourceDoc;
            MergeLoadedSourceLocked(createPremergeBackup: true);
            _projectionDirty = sourceDoc.ProjectionOutOfDate || _canWriteProjection;
            TryFlushLocked();
            return;
        }

        _canWriteProjection = true;
        _state = sourceDoc;
        _lastPersistedProjection = JsonStoreClone.DeepClone(projectionRead.Document!.Sessions ?? []);
        ReconcileLoadedSourceWithProjection(projectionRead.Document!);
    }

    private void ReconcileFromProjectionOnly(StreamSessionHistory projection)
    {
        var rawSessions = projection.Sessions ?? [];
        var projHiddenById = new Dictionary<Guid, bool>(rawSessions.Count);
        var imported = new List<BroadcastRecord>(rawSessions.Count);

        foreach (var session in rawSessions)
        {
            if (session is null)
            {
                continue;
            }

            projHiddenById[session.Id] = session.IsHidden;

            var record = BroadcastRecord.FromTwitchSession(session);
            record.IsHidden = false;
            imported.Add(record);
        }

        var mergeResult = BroadcastHistoryMerge.Merge(imported);
        var finalized = new List<BroadcastRecord>(mergeResult.Broadcasts);

        foreach (var record in finalized)
        {
            if ((projHiddenById.TryGetValue(record.Id, out var isHidden) && isHidden)
                || (record.MergedBroadcastIds?.Any(id => projHiddenById.TryGetValue(id, out var mergedHidden) && mergedHidden) == true))
            {
                record.IsHidden = true;
            }
        }

        _state = new() { Broadcasts = finalized };
        _lastPersistedProjection = JsonStoreClone.DeepClone(rawSessions);
        _sourceDirty = true;

        var expectedProjection = BuildProjectionLocked();
        _projectionDirty = !AreSessionsEqual(_lastPersistedProjection, expectedProjection.Sessions);

        TryFlushLocked();
    }

    private void ReconcileLoadedSourceWithProjection(StreamSessionHistory projection)
    {
        var acceptKnownProjectionChanges = !_state.ProjectionOutOfDate;
        var rawSessions = projection.Sessions ?? [];
        var projHiddenByMatchedId = new Dictionary<Guid, bool>();
        var originalHiddenById = new Dictionary<Guid, bool>();
        var sourceChanged = false;

        foreach (var existing in _state.Broadcasts)
        {
            originalHiddenById[existing.Id] = existing.IsHidden;
        }

        var working = JsonStoreClone.DeepClone(_state.Broadcasts);

        foreach (var session in rawSessions)
        {
            if (session is null)
            {
                continue;
            }

            var matched = FindMatchingBroadcast(working, session);

            if (matched is not null)
            {
                if (!acceptKnownProjectionChanges || matched.MergedBroadcastIds?.Contains(session.Id) == true)
                {
                    if (acceptKnownProjectionChanges && session.IsHidden)
                    {
                        projHiddenByMatchedId[matched.Id] = true;
                    }

                    continue;
                }

                projHiddenByMatchedId[matched.Id] = projHiddenByMatchedId.GetValueOrDefault(matched.Id) || session.IsHidden;

                var twitchIndex = FindMatchingTwitchPlatformIndex(matched, session);
                if (twitchIndex < 0)
                {
                    continue;
                }

                var existingTwitch = matched.Platforms[twitchIndex];

                if (HasProjectionAdvanced(existingTwitch, session))
                {
                    var updatedTwitch = BroadcastPlatformRecord.FromTwitchSession(session);
                    updatedTwitch.StreamId ??= existingTwitch.StreamId;
                    updatedTwitch.ChannelId ??= existingTwitch.ChannelId;
                    updatedTwitch.ProjectionSessionId ??= existingTwitch.ProjectionSessionId;
                    updatedTwitch.MergedStartedAts = existingTwitch.MergedStartedAts;
                    matched.Platforms[twitchIndex] = updatedTwitch;
                    RecalculateBoundsAndViews(matched);
                    sourceChanged = true;
                }

                continue;
            }

            var imported = BroadcastRecord.FromTwitchSession(session);
            imported.IsHidden = false;
            working.Add(imported);
            projHiddenByMatchedId[imported.Id] = session.IsHidden;
            sourceChanged = true;
        }

        var mergeResult = BroadcastHistoryMerge.Merge(working);
        working = [.. mergeResult.Broadcasts];

        if (mergeResult.MergedPairs > 0)
        {
            JsonStoreBackup.TryCreateBackup(_broadcastsPath, "premerge", _logger, null, out _);
            sourceChanged = true;
        }

        foreach (var record in working)
        {
            if (projHiddenByMatchedId.TryGetValue(record.Id, out var isHidden)
                && (!originalHiddenById.TryGetValue(record.Id, out var wasHidden) || !wasHidden))
            {
                record.IsHidden = isHidden;
            }
            else if (originalHiddenById.TryGetValue(record.Id, out var originalHidden))
            {
                record.IsHidden = originalHidden;
            }

            if (!originalHiddenById.TryGetValue(record.Id, out var hadHidden) || hadHidden != record.IsHidden)
            {
                sourceChanged = true;
            }
        }

        _state.Broadcasts = working;

        if (sourceChanged)
        {
            _sourceDirty = true;
        }

        var expectedProjection = BuildProjectionLocked();
        _projectionDirty = _state.ProjectionOutOfDate || !AreSessionsEqual(rawSessions, expectedProjection.Sessions);

        TryFlushLocked();
    }

    private void MergeLoadedSourceLocked(bool createPremergeBackup)
    {
        var result = BroadcastHistoryMerge.Merge(_state.Broadcasts);

        if (result.MergedPairs == 0)
        {
            return;
        }

        if (createPremergeBackup)
        {
            JsonStoreBackup.TryCreateBackup(_broadcastsPath, "premerge", _logger, null, out _);
        }

        _state.Broadcasts = [.. result.Broadcasts];
        _sourceDirty = true;
    }

    private bool TryFlushLocked()
    {
        if (_sourceDirty)
        {
            if (!_canWriteSource)
            {
                return false;
            }

            if (!TrySaveSourceLocked())
            {
                return false;
            }
        }

        if (_projectionDirty && _canWriteProjection && !TrySaveProjectionLocked())
        {
            return false;
        }

        return !_sourceDirty && (!_projectionDirty || !_canWriteProjection);
    }

    private bool TrySaveSourceAndProjectionLocked()
    {
        if (!TrySaveSourceLocked())
        {
            return false;
        }

        if (_projectionDirty && _canWriteProjection)
        {
            TrySaveProjectionLocked();
        }

        return true;
    }

    private bool TrySaveSourceLocked()
    {
        try
        {
            if (!CreatePreMultichannelBackupsOnceLocked())
            {
                return false;
            }

            foreach (var broadcast in _state.Broadcasts)
            {
                broadcast.EnsureProjectionSessionIds();
            }

            _state.SchemaVersion = BroadcastHistoryDocument.CurrentSchemaVersion;
            _state.ProjectionOutOfDate = _projectionDirty;
            var json = JsonSerializer.Serialize(_state, JsonStoreOptions.Default);
            AtomicFile.Save(_broadcastsPath, json, _logger);
            _persistedSourceIds = [.. _state.Broadcasts.SelectMany(static b => (b.MergedBroadcastIds ?? []).Prepend(b.Id))];
            _sourceDirty = false;
            if (_sourceReadState == FileReadState.Missing)
            {
                _sourceReadState = FileReadState.Loaded;
            }
            return true;
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception,
                "BroadcastHistoryStore: не удалось записать источник {FilePath}, данные оставлены в памяти до следующей попытки",
                _broadcastsPath);

            return false;
        }
    }

    private bool CreatePreMultichannelBackupsOnceLocked()
    {
        var shouldBackup = _state.SchemaVersion < BroadcastHistoryDocument.CurrentSchemaVersion
            || _state.Broadcasts.Any(static b =>
                b.Platforms.Any(static p => !string.IsNullOrWhiteSpace(p.ChannelId))
                || b.Platforms.GroupBy(static p => p.Platform).Any(static g => g.Count() > 1));

        if (!shouldBackup)
        {
            return true;
        }

        return TryCopyPreMultichannelBackup(_broadcastsPath)
               && TryCopyPreMultichannelBackup(_projectionPath);
    }

    private bool TryCopyPreMultichannelBackup(string sourcePath)
    {
        return JsonStoreBackup.TryCreateOneTimeBackup(sourcePath, "pre-multichannel", _logger);
    }

    private bool TrySaveProjectionLocked()
    {
        try
        {
            var expected = BuildProjectionLocked();

            if (!_state.ProjectionOutOfDate && File.Exists(_projectionPath) && AreSessionsEqual(_lastPersistedProjection, expected.Sessions))
            {
                _projectionDirty = false;
                return true;
            }

            var json = JsonSerializer.Serialize(expected, JsonStoreOptions.Default);
            AtomicFile.Save(_projectionPath, json, _logger);
            _lastPersistedProjection = expected.Sessions;
            _projectionDirty = false;
            if (_projectionReadState == FileReadState.Missing)
            {
                _projectionReadState = FileReadState.Loaded;
            }
            if (_state.ProjectionOutOfDate)
            {
                _sourceDirty = true;
                return TrySaveSourceLocked();
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception,
                "BroadcastHistoryStore: не удалось обновить проекцию {FilePath}, повтор запланирован в фоне",
                _projectionPath);

            return false;
        }
    }

    private StreamSessionHistory BuildProjectionLocked()
    {
        var sessions = new List<StreamSessionRecord>(_state.Broadcasts.Count);

        foreach (var broadcast in _state.Broadcasts)
        {
            sessions.AddRange(broadcast.ProjectToTwitchSessions());
        }

        return new() { Sessions = sessions };
    }

    private RawReadResult<T> ReadRawDocument<T>(string path, string description)
        where T : class
    {
        if (!File.Exists(path))
        {
            _logger?.LogDebug("BroadcastHistoryStore: файл {Description} {FilePath} отсутствует", description, path);
            return new(FileReadState.Missing, null, false);
        }

        try
        {
            var json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger?.LogError("BroadcastHistoryStore: файл {Description} {FilePath} пуст, запись заблокирована", description, path);
                var emptyBackup = JsonStoreBackup.TryCreateBackup(path, "invalid", _logger, null, out _);
                return new(FileReadState.Failed, null, emptyBackup);
            }

            var value = JsonSerializer.Deserialize<T>(json, JsonStoreOptions.Default);

            if (value == null)
            {
                _logger?.LogError("BroadcastHistoryStore: файл {Description} {FilePath} разобран в null, запись заблокирована", description, path);
                var nullBackup = JsonStoreBackup.TryCreateBackup(path, "invalid", _logger, null, out _);
                return new(FileReadState.Failed, null, nullBackup);
            }

            return new(FileReadState.Loaded, value, false);
        }
        catch (Exception exception)
        {
            _logger?.LogError(exception, "BroadcastHistoryStore: ошибка чтения {Description} {FilePath}", description, path);
            var backupOk = JsonStoreBackup.TryCreateBackup(path, "invalid", _logger, null, out _);
            return new(FileReadState.Failed, null, backupOk);
        }
    }

    private static BroadcastRecord? FindMatchingBroadcast(IReadOnlyList<BroadcastRecord> broadcasts, StreamSessionRecord session)
    {
        foreach (var broadcast in broadcasts)
        {
            if ((broadcast.Id == session.Id
                    || broadcast.MergedBroadcastIds?.Contains(session.Id) == true
                    || broadcast.Platforms.Exists(p => p.Platform == StreamingPlatform.Twitch && p.ProjectionSessionId == session.Id))
                && broadcast.Platforms.Exists(static p => p.Platform == StreamingPlatform.Twitch))
            {
                return broadcast;
            }
        }

        foreach (var broadcast in broadcasts)
        {
            var matched = broadcast.Platforms.Exists(p =>
                p.Platform == StreamingPlatform.Twitch
                && string.Equals(p.Channel, session.Channel, StringComparison.OrdinalIgnoreCase)
                && (p.StartedAt == session.StartedAt
                    || p.MergedStartedAts?.Contains(session.StartedAt) == true));

            if (matched)
            {
                return broadcast;
            }
        }

        return null;
    }

    private static int FindMatchingTwitchPlatformIndex(BroadcastRecord broadcast, StreamSessionRecord session)
    {
        var byProjectionId = broadcast.Platforms.FindIndex(p =>
            p.Platform == StreamingPlatform.Twitch && p.ProjectionSessionId == session.Id);
        if (byProjectionId >= 0)
        {
            return byProjectionId;
        }

        var byChannelAndStart = broadcast.Platforms.FindIndex(p =>
            p.Platform == StreamingPlatform.Twitch
            && string.Equals(p.Channel, session.Channel, StringComparison.OrdinalIgnoreCase)
            && p.StartedAt == session.StartedAt);
        if (byChannelAndStart >= 0)
        {
            return byChannelAndStart;
        }

        var byChannel = broadcast.Platforms.FindIndex(p =>
            p.Platform == StreamingPlatform.Twitch
            && string.Equals(p.Channel, session.Channel, StringComparison.OrdinalIgnoreCase));
        if (byChannel >= 0)
        {
            return byChannel;
        }

        return broadcast.Platforms.FindIndex(static p => p.Platform == StreamingPlatform.Twitch);
    }

    private static bool HasProjectionAdvanced(BroadcastPlatformRecord current, StreamSessionRecord session)
    {
        if (session.EndedAt < current.EndedAt || session.MessageCount < current.MessageCount)
        {
            return false;
        }

        return session.EndedAt != current.EndedAt
               || session.MessageCount != current.MessageCount
               || session.PeakViewers != current.PeakViewers
               || session.AverageViewers != current.AverageViewers
               || !string.Equals(session.Title, current.Title, StringComparison.Ordinal)
               || !string.Equals(session.Game, current.Game, StringComparison.Ordinal)
               || session.Chatters.Count != current.Chatters.Count
               || session.Segments.Count != current.Segments.Count;
    }

    private static void RecalculateBoundsAndViews(BroadcastRecord broadcast)
    {
        if (broadcast.Platforms.Count == 0)
        {
            return;
        }

        broadcast.StartedAt = broadcast.Platforms.Min(static p => p.StartedAt);
        broadcast.EndedAt = broadcast.Platforms.Max(static p => p.EndedAt);

        if (broadcast.Platforms.Count == 1 && broadcast.ViewerSeries is null)
        {
            broadcast.PeakViewers = broadcast.Platforms[0].PeakViewers;
            broadcast.AverageViewers = broadcast.Platforms[0].AverageViewers;
        }
    }

    private static bool AreSessionsEqual(IReadOnlyList<StreamSessionRecord> left, IReadOnlyList<StreamSessionRecord> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!AreSessionRecordsEqual(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreSessionRecordsEqual(StreamSessionRecord a, StreamSessionRecord b)
    {
        if (a.Id != b.Id
            || !string.Equals(a.Channel, b.Channel, StringComparison.Ordinal)
            || a.StartedAt != b.StartedAt
            || a.EndedAt != b.EndedAt
            || !string.Equals(a.Title, b.Title, StringComparison.Ordinal)
            || !string.Equals(a.Game, b.Game, StringComparison.Ordinal)
            || a.MessageCount != b.MessageCount
            || a.ChatterCount != b.ChatterCount
            || a.PeakViewers != b.PeakViewers
            || a.AverageViewers != b.AverageViewers
            || a.IsHidden != b.IsHidden
            || a.Chatters.Count != b.Chatters.Count
            || a.Segments.Count != b.Segments.Count
            || (a.TrackedIntervals is null) != (b.TrackedIntervals is null))
        {
            return false;
        }

        for (var i = 0; i < a.Chatters.Count; i++)
        {
            if (!string.Equals(a.Chatters[i].UserId, b.Chatters[i].UserId, StringComparison.Ordinal)
                || !string.Equals(a.Chatters[i].DisplayName, b.Chatters[i].DisplayName, StringComparison.Ordinal)
                || a.Chatters[i].MessageCount != b.Chatters[i].MessageCount)
            {
                return false;
            }
        }

        for (var i = 0; i < a.Segments.Count; i++)
        {
            var sa = a.Segments[i];
            var sb = b.Segments[i];

            if (sa.StartedAt != sb.StartedAt
                || sa.EndedAt != sb.EndedAt
                || !string.Equals(sa.Title, sb.Title, StringComparison.Ordinal)
                || !string.Equals(sa.Game, sb.Game, StringComparison.Ordinal)
                || sa.MessageCount != sb.MessageCount
                || sa.PeakViewers != sb.PeakViewers
                || sa.AverageViewers != sb.AverageViewers)
            {
                return false;
            }
        }

        if (a.TrackedIntervals is not null && b.TrackedIntervals is not null)
        {
            if (a.TrackedIntervals.Count != b.TrackedIntervals.Count)
            {
                return false;
            }

            for (var i = 0; i < a.TrackedIntervals.Count; i++)
            {
                if (a.TrackedIntervals[i].StartedAt != b.TrackedIntervals[i].StartedAt
                    || a.TrackedIntervals[i].EndedAt != b.TrackedIntervals[i].EndedAt)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private readonly record struct RawReadResult<T>(FileReadState State, T? Document, bool BackupSucceeded);
}
