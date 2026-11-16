using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using PoproshaykaBot.Core.Settings.Stores;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace PoproshaykaBot.Core.Chat.History;

public sealed class ChatHistoryCacheStore : IChatHistoryPersistenceSink, IDisposable
{
    public const string CacheFolderName = "chat-history";
    public const long MaxTotalCacheBytes = 32L * 1024 * 1024;

    private enum FileReadState
    {
        None = 0,
        Loaded = 1,
        Abandoned = 2,
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver
        {
            Modifiers = { JsonStoreNullDefaults.Apply },
        },
    };

    private readonly string _cacheDirectory;
    private readonly ILogger<ChatHistoryCacheStore>? _logger;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly SemaphoreSlim _dirtySignal = new(0, int.MaxValue);
    private readonly Lock _stateLock = new();
    private readonly Dictionary<string, FileReadState> _fileStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<ChatHistoryScope, ulong> _committedVersions = [];
    private readonly HashSet<ChatHistoryScope> _dirtyScopes = [];

    private int _externalWriteDepth;
    private bool _rewrittenExternally;
    private bool _disposed;

    public ChatHistoryCacheStore(
        ILogger<ChatHistoryCacheStore>? logger = null,
        string? cacheDirectory = null,
        TimeProvider? timeProvider = null)
    {
        _logger = logger;
        _cacheDirectory = string.IsNullOrWhiteSpace(cacheDirectory)
            ? AppPaths.Combine(CacheFolderName)
            : cacheDirectory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string CacheDirectory => _cacheDirectory;

    public bool IsWriteRevoked
    {
        get
        {
            lock (_stateLock)
            {
                return _externalWriteDepth > 0 || _rewrittenExternally;
            }
        }
    }

    public static bool IsCacheFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || fileName.Length != 69)
        {
            return false;
        }

        for (var i = 0; i < 64; i++)
        {
            if (!Uri.IsHexDigit(fileName[i]))
            {
                return false;
            }
        }

        return true;
    }

    public void MarkDirty(ChatHistoryScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (string.IsNullOrWhiteSpace(scope.ChannelId))
        {
            return;
        }

        bool shouldRelease;
        lock (_stateLock)
        {
            if (_disposed || _rewrittenExternally)
            {
                return;
            }

            shouldRelease = _dirtyScopes.Add(scope);
        }

        if (shouldRelease)
        {
            _dirtySignal.Release();
        }
    }

    public async Task<bool> WaitForDirtyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var signalTask = _dirtySignal.WaitAsync(linkedCts.Token);
            var delayTask = Task.Delay(timeout, _timeProvider, linkedCts.Token);
            var completed = await Task.WhenAny(signalTask, delayTask).ConfigureAwait(false);
            await linkedCts.CancelAsync().ConfigureAwait(false);

            try
            {
                await signalTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            try
            {
                await delayTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            return ReferenceEquals(completed, signalTask) && signalTask.IsCompletedSuccessfully;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public ChatHistoryClearResult ClearScopes(IReadOnlyList<ChatHistoryClearTarget> targets, bool clearUnloadedScopes = false)
    {
        ArgumentNullException.ThrowIfNull(targets);

        _ioGate.Wait();
        try
        {
            if (IsWriteRevoked)
            {
                return new(false);
            }

            var allClean = true;

            if (clearUnloadedScopes)
            {
                lock (_stateLock)
                {
                    if (_fileStates.Values.Any(static s => s == FileReadState.Abandoned))
                    {
                        allClean = false;
                    }
                }

                if (Directory.Exists(_cacheDirectory))
                {
                    foreach (var filePath in Directory.EnumerateFiles(_cacheDirectory))
                    {
                        var name = Path.GetFileName(filePath);
                        if (IsServiceSidecar(name))
                        {
                            if (!TryDeleteFile(filePath, name))
                            {
                                allClean = false;
                            }
                        }
                        else if (IsCacheFileName(name) && IsAbandoned(name))
                        {
                            allClean = false;
                        }
                    }
                }
            }

            foreach (var target in targets)
            {
                if (string.IsNullOrWhiteSpace(target.Scope.ChannelId))
                {
                    continue;
                }

                var fileName = target.Scope.ComputeCacheFileName();
                if (IsAbandoned(fileName))
                {
                    allClean = false;
                    continue;
                }

                var path = Path.Combine(_cacheDirectory, fileName);

                if (!TryDeleteScopeSidecars(fileName))
                {
                    allClean = false;
                }

                var latest = target.RefreshSnapshot?.Invoke();
                var writeVersion = latest is not null && latest.MutationVersion >= target.MutationVersion
                    ? latest.MutationVersion
                    : target.MutationVersion;
                var documentToWrite = latest is not null && latest.MutationVersion >= target.MutationVersion
                    ? latest.Document
                    : target.Document;

                if (!target.HadStateBeforeClear && writeVersion == target.MutationVersion && !File.Exists(path))
                {
                    continue;
                }

                if (!TryWriteDocument(path, fileName, documentToWrite))
                {
                    allClean = false;
                    MarkDirty(target.Scope);
                    continue;
                }

                var afterVersion = target.RefreshSnapshot?.Invoke()?.MutationVersion ?? writeVersion;
                lock (_stateLock)
                {
                    _fileStates[fileName] = FileReadState.Loaded;
                    _committedVersions[target.Scope] = writeVersion;
                    if (afterVersion == writeVersion)
                    {
                        _dirtyScopes.Remove(target.Scope);
                    }
                    else
                    {
                        _dirtyScopes.Add(target.Scope);
                    }
                }
            }

            if (clearUnloadedScopes && Directory.Exists(_cacheDirectory))
            {
                var targetFileNames = targets
                    .Select(static t => t.Scope.ComputeCacheFileName())
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var filePath in Directory.EnumerateFiles(_cacheDirectory))
                {
                    var name = Path.GetFileName(filePath);
                    if (IsCacheFileName(name)
                        && !targetFileNames.Contains(name)
                        && (IsAbandoned(name) || !TryDeleteFile(filePath, name)))
                    {
                        allClean = false;
                    }
                }
            }

            return new(allClean);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task<T> RunExternalWriteAsync<T>(
        Func<Task<T>> write,
        Func<T, bool> rewritten,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(rewritten);

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_stateLock)
            {
                _externalWriteDepth++;
            }

            try
            {
                var result = await write().ConfigureAwait(false);
                var wasRewritten = rewritten(result);

                lock (_stateLock)
                {
                    _externalWriteDepth--;
                    if (wasRewritten)
                    {
                        _rewrittenExternally = true;
                        _dirtyScopes.Clear();
                    }
                }

                return result;
            }
            catch (Exception exception)
            {
                lock (_stateLock)
                {
                    _externalWriteDepth--;
                    _rewrittenExternally = true;
                }

                _logger?.ChatHistoryCacheExternalWriteAbandoned(exception);
                throw;
            }
        }
        finally
        {
            _ioGate.Release();
        }
    }

    internal async Task<IReadOnlyList<ChatHistoryLoaded>> LoadAndRestoreAsync(
        ChatHistoryManager manager,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manager);

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(_cacheDirectory))
            {
                return [];
            }

            var minAllowedUtc = _timeProvider.GetUtcNow() - ChatHistoryManager.RetentionPeriod;
            var restoredOutcomes = new List<ChatHistoryImportOutcome>();
            var scopesToRewrite = new HashSet<ChatHistoryScope>();
            var filesToDelete = new List<(string Path, ChatHistoryScope Scope)>();

            foreach (var path in Directory.EnumerateFiles(_cacheDirectory).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileName = Path.GetFileName(path);

                if (IsTransientSidecar(fileName))
                {
                    TryDeleteFile(path, fileName);
                    continue;
                }

                if (IsRetentionSidecar(fileName))
                {
                    if (File.GetLastWriteTimeUtc(path) < minAllowedUtc.UtcDateTime)
                    {
                        TryDeleteFile(path, fileName);
                    }

                    continue;
                }

                if (!IsCacheFileName(fileName))
                {
                    continue;
                }

                try
                {
                    if (!TryReadDocument(path, fileName, out var document))
                    {
                        MarkAbandoned(path, fileName);
                        continue;
                    }

                    var scope = new ChatHistoryScope(document.Platform, document.ChannelId, document.SessionScopeId, document.ChannelName);
                    var expectedFileName = scope.ComputeCacheFileName();

                    if (!string.Equals(fileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        MarkAbandoned(path, fileName);
                        continue;
                    }

                    lock (_stateLock)
                    {
                        _fileStates[fileName] = FileReadState.Loaded;
                    }

                    var outcome = manager.RestoreFromDocument(scope, document, out var prunedExpired);

                    if (manager.TryCapturePersistenceSnapshot(scope, out var snapshot))
                    {
                        if (!snapshot.HasAnyState)
                        {
                            filesToDelete.Add((path, scope));
                        }
                        else if (prunedExpired)
                        {
                            scopesToRewrite.Add(scope);
                        }
                        else
                        {
                            lock (_stateLock)
                            {
                                _committedVersions[scope] = snapshot.MutationVersion;
                            }
                        }
                    }

                    if (outcome.AddedEntries.Count > 0)
                    {
                        restoredOutcomes.Add(outcome);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger?.LogDebug(exception, "Ошибка восстановления файла кэша чата {FileName}", fileName);
                    MarkAbandoned(path, fileName);
                }
            }

            if (!IsWriteRevoked)
            {
                foreach (var (path, scope) in filesToDelete)
                {
                    if (!TryDeleteFile(path, Path.GetFileName(path)))
                    {
                        MarkDirty(scope);
                    }
                }

                if (scopesToRewrite.Count > 0)
                {
                    FlushUnderIoGate(manager, scopesToRewrite, null, cancellationToken);
                }

                EnforceSizeBudgetUnderIoGate(manager, cancellationToken);
            }

            var totalMessages = restoredOutcomes.Sum(static outcome => outcome.AddedEntries.Count);
            if (restoredOutcomes.Count > 0)
            {
                _logger?.ChatHistoryCacheRestored(restoredOutcomes.Count, totalMessages);
            }

            return restoredOutcomes
                .Select(static outcome => new ChatHistoryLoaded(outcome.Scope, outcome.AddedEntries, outcome.MergedHistory))
                .ToList();
        }
        finally
        {
            _ioGate.Release();
        }
    }

    internal async Task FlushDirtyAsync(ChatHistoryManager manager, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manager);

        List<ChatHistoryScope> drained;
        lock (_stateLock)
        {
            if (_dirtyScopes.Count == 0)
            {
                return;
            }

            drained = [.. _dirtyScopes];
            _dirtyScopes.Clear();
        }

        try
        {
            await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            RequeueScopes(drained, signal: false);
            throw;
        }

        var remainingScopes = new HashSet<ChatHistoryScope>(drained);
        try
        {
            bool rewritten;
            bool externalActive;
            lock (_stateLock)
            {
                rewritten = _rewrittenExternally;
                externalActive = _externalWriteDepth > 0;
            }

            if (rewritten)
            {
                remainingScopes.Clear();
                return;
            }

            if (externalActive)
            {
                RequeueScopes(drained);
                remainingScopes.Clear();
                return;
            }

            FlushUnderIoGate(manager, drained, remainingScopes, cancellationToken);
            remainingScopes.Clear();
        }
        catch
        {
            RequeueScopes(remainingScopes, signal: false);
            throw;
        }
        finally
        {
            _ioGate.Release();
        }
    }

    internal async Task PruneExpiredAsync(ChatHistoryManager manager, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manager);

        var modifiedScopes = manager.PruneExpired();

        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                var minAllowedUtc = _timeProvider.GetUtcNow() - ChatHistoryManager.RetentionPeriod;
                foreach (var path in Directory.EnumerateFiles(_cacheDirectory))
                {
                    var name = Path.GetFileName(path);
                    if (IsRetentionSidecar(name) && File.GetLastWriteTimeUtc(path) < minAllowedUtc.UtcDateTime)
                    {
                        TryDeleteFile(path, name);
                    }
                }
            }

            if (modifiedScopes.Count > 0 && !IsWriteRevoked)
            {
                FlushUnderIoGate(manager, modifiedScopes, null, cancellationToken);
            }
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _ioGate.Dispose();
        _dirtySignal.Dispose();
    }

    private void FlushUnderIoGate(
        ChatHistoryManager manager,
        IEnumerable<ChatHistoryScope> scopes,
        HashSet<ChatHistoryScope>? remainingScopes,
        CancellationToken cancellationToken)
    {
        var snapshots = new Dictionary<ChatHistoryScope, (string FileName, ChatHistoryPersistenceSnapshot Snapshot, byte[] Bytes)>();

        foreach (var scope in scopes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(scope.ChannelId))
            {
                remainingScopes?.Remove(scope);
                continue;
            }

            var fileName = scope.ComputeCacheFileName();
            if (IsAbandoned(fileName))
            {
                remainingScopes?.Remove(scope);
                continue;
            }

            if (!manager.TryCapturePersistenceSnapshot(scope, out var snapshot))
            {
                remainingScopes?.Remove(scope);
                continue;
            }

            lock (_stateLock)
            {
                if (_committedVersions.TryGetValue(scope, out var committed)
                    && committed == snapshot.MutationVersion
                    && File.Exists(Path.Combine(_cacheDirectory, fileName)))
                {
                    remainingScopes?.Remove(scope);
                    continue;
                }
            }

            var bytes = snapshot.HasAnyState
                ? SerializeDocument(snapshot.Document)
                : [];

            snapshots[scope] = (fileName, snapshot, bytes);
        }

        if (snapshots.Count == 0)
        {
            return;
        }

        var directorySizes = CaptureDirectorySizes();
        var pendingByFile = snapshots.Values.ToDictionary(static v => v.FileName, static v => (long)v.Bytes.Length, StringComparer.OrdinalIgnoreCase);

        EvictToBudgetUnderIoGate(manager, directorySizes, pendingByFile, snapshots, cancellationToken);
        WriteSnapshotsUnderIoGate(manager, snapshots, remainingScopes);
    }

    private void EnforceSizeBudgetUnderIoGate(ChatHistoryManager manager, CancellationToken cancellationToken)
    {
        var directorySizes = CaptureDirectorySizes();
        var pendingByFile = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        if (ComputeTotalBytes(directorySizes, pendingByFile) <= MaxTotalCacheBytes)
        {
            return;
        }

        var snapshots = new Dictionary<ChatHistoryScope, (string FileName, ChatHistoryPersistenceSnapshot Snapshot, byte[] Bytes)>();
        EvictToBudgetUnderIoGate(manager, directorySizes, pendingByFile, snapshots, cancellationToken);

        if (snapshots.Count > 0)
        {
            WriteSnapshotsUnderIoGate(manager, snapshots, null);
        }
    }

    private void EvictToBudgetUnderIoGate(
        ChatHistoryManager manager,
        Dictionary<string, long> directorySizes,
        Dictionary<string, long> pendingByFile,
        Dictionary<ChatHistoryScope, (string FileName, ChatHistoryPersistenceSnapshot Snapshot, byte[] Bytes)> snapshots,
        CancellationToken cancellationToken)
    {
        var excludedFiles = GetAbandonedFileNames();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var excessBytes = ComputeTotalBytes(directorySizes, pendingByFile) - MaxTotalCacheBytes;
            if (excessBytes <= 0)
            {
                break;
            }

            var batchTouched = new HashSet<ChatHistoryScope>();
            var remainingEstimatedExcess = excessBytes;

            while (remainingEstimatedExcess > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!manager.TryEvictOldestAcrossWritableScopes(excludedFiles, out var evictedScope, out var estimatedBytes)
                    || evictedScope is null)
                {
                    break;
                }

                batchTouched.Add(evictedScope);
                remainingEstimatedExcess -= Math.Max(256L, estimatedBytes);
            }

            if (batchTouched.Count == 0)
            {
                break;
            }

            foreach (var touchedScope in batchTouched)
            {
                if (!manager.TryCapturePersistenceSnapshot(touchedScope, out var snapshot))
                {
                    continue;
                }

                var fileName = touchedScope.ComputeCacheFileName();
                var bytes = snapshot.HasAnyState ? SerializeDocument(snapshot.Document) : [];
                snapshots[touchedScope] = (fileName, snapshot, bytes);
                pendingByFile[fileName] = bytes.Length;
            }
        }
    }

    private void WriteSnapshotsUnderIoGate(
        ChatHistoryManager manager,
        Dictionary<ChatHistoryScope, (string FileName, ChatHistoryPersistenceSnapshot Snapshot, byte[] Bytes)> snapshots,
        HashSet<ChatHistoryScope>? remainingScopes)
    {
        foreach (var (scope, entry) in snapshots)
        {
            if (!manager.IsClearGenerationCurrent(scope, entry.Snapshot.ClearGeneration))
            {
                remainingScopes?.Remove(scope);
                continue;
            }

            var path = Path.Combine(_cacheDirectory, entry.FileName);

            if (!entry.Snapshot.HasAnyState)
            {
                if (TryDeleteFile(path, entry.FileName))
                {
                    lock (_stateLock)
                    {
                        _committedVersions[scope] = entry.Snapshot.MutationVersion;
                    }

                    remainingScopes?.Remove(scope);
                }
                else
                {
                    MarkDirty(scope);
                }

                continue;
            }

            if (TryWriteBytes(path, entry.FileName, entry.Bytes))
            {
                lock (_stateLock)
                {
                    _fileStates[entry.FileName] = FileReadState.Loaded;
                    _committedVersions[scope] = entry.Snapshot.MutationVersion;
                }

                remainingScopes?.Remove(scope);
            }
            else
            {
                MarkDirty(scope);
            }
        }
    }

    private Dictionary<string, long> CaptureDirectorySizes()
    {
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(_cacheDirectory))
        {
            return sizes;
        }

        foreach (var path in Directory.EnumerateFiles(_cacheDirectory))
        {
            var name = Path.GetFileName(path);

            try
            {
                sizes[name] = new FileInfo(path).Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return sizes;
    }

    private static long ComputeTotalBytes(
        IReadOnlyDictionary<string, long> directorySizes,
        IReadOnlyDictionary<string, long> pendingFileSizes)
    {
        long total = 0;

        foreach (var (name, length) in directorySizes)
        {
            total += pendingFileSizes.TryGetValue(name, out var overrideLength)
                ? overrideLength
                : length;
        }

        foreach (var (name, length) in pendingFileSizes)
        {
            if (!directorySizes.ContainsKey(name))
            {
                total += length;
            }
        }

        return total;
    }

    private HashSet<string> GetAbandonedFileNames()
    {
        lock (_stateLock)
        {
            return _fileStates
                .Where(static pair => pair.Value == FileReadState.Abandoned)
                .Select(static pair => pair.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }

    private bool IsAbandoned(string fileName)
    {
        lock (_stateLock)
        {
            return _fileStates.TryGetValue(fileName, out var state) && state == FileReadState.Abandoned;
        }
    }

    private void RequeueScopes(IEnumerable<ChatHistoryScope> scopes, bool signal = true)
    {
        var added = false;
        lock (_stateLock)
        {
            if (_disposed || _rewrittenExternally)
            {
                return;
            }

            foreach (var scope in scopes)
            {
                if (_dirtyScopes.Add(scope))
                {
                    added = true;
                }
            }
        }

        if (added && signal)
        {
            _dirtySignal.Release();
        }
    }

    private bool TryReadDocument(string path, string fileName, out ChatHistoryCacheDocument document)
    {
        document = null!;

        try
        {
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            var parsed = JsonSerializer.Deserialize<ChatHistoryCacheDocument>(json, SerializerOptions);
            if (parsed is null
                || parsed.SchemaVersion <= 0
                || string.IsNullOrWhiteSpace(parsed.ChannelId))
            {
                return false;
            }

            document = parsed;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger?.LogDebug(exception, "Ошибка чтения файла кэша чата {FileName}", fileName);
            return false;
        }
    }

    private void MarkAbandoned(string path, string fileName)
    {
        lock (_stateLock)
        {
            _fileStates[fileName] = FileReadState.Abandoned;
        }

        JsonStoreBackup.CreateBackup(path, "invalid", _logger);
        _logger?.ChatHistoryCacheCorrupt(fileName);
    }

    private bool TryWriteDocument(string path, string fileName, ChatHistoryCacheDocument document)
    {
        var bytes = SerializeDocument(document);
        return TryWriteBytes(path, fileName, bytes);
    }

    private bool TryWriteBytes(string path, string fileName, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            AtomicFile.Save(path, bytes, _logger, keepBackup: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger?.ChatHistoryCacheWriteFailed(exception, fileName);
            return false;
        }
    }

    private bool TryDeleteScopeSidecars(string fileName)
    {
        if (!Directory.Exists(_cacheDirectory))
        {
            return true;
        }

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var allDeleted = true;

        foreach (var path in Directory.EnumerateFiles(_cacheDirectory))
        {
            var candidate = Path.GetFileName(path);
            if (IsScopeSidecar(candidate, fileName, stem) && !TryDeleteFile(path, candidate))
            {
                allDeleted = false;
            }
        }

        return allDeleted;
    }

    private bool TryDeleteFile(string path, string fileName)
    {
        if (!File.Exists(path))
        {
            return true;
        }

        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger?.ChatHistoryCacheDeleteFailed(exception, fileName);
            return false;
        }
    }

    private static byte[] SerializeDocument(ChatHistoryCacheDocument document)
    {
        return JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
    }

    private static bool IsServiceSidecar(string fileName)
    {
        return IsTransientSidecar(fileName) || IsRetentionSidecar(fileName);
    }

    private static bool IsTransientSidecar(string fileName)
    {
        return fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".old", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRetentionSidecar(string fileName)
    {
        return fileName.Contains(".invalid-", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".pre-import-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScopeSidecar(string candidate, string fileName, string stem)
    {
        if (string.Equals(candidate, fileName + ".tmp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, fileName + ".bak", StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate, fileName + ".old", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return candidate.StartsWith(stem + ".invalid-", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(stem + ".pre-import-", StringComparison.OrdinalIgnoreCase);
    }
}
