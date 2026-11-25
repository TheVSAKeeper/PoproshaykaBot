using KeepShell.Updates;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Update;
using PoproshaykaBot.Core.Settings.Stores;
using System.Runtime.ExceptionServices;
using CoreUpdateApplyMode = PoproshaykaBot.Core.Settings.Update.UpdateApplyMode;
using CoreUpdateSettings = PoproshaykaBot.Core.Settings.Update.UpdateSettings;

namespace PoproshaykaBot.Core.Update;

public sealed class UpdateCoordinator(
    UpdateChecker checker,
    IUpdateInstaller installer,
    UpdateStore store,
    IEventBus eventBus,
    IUpdateEnvironment environment,
    ILogger<UpdateCoordinator> logger)
    : IUpdateCoordinator, IDisposable
{
    private readonly SemaphoreSlim _prepareGate = new(1, 1);
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly Lock _syncLock = new();
    private readonly List<DeferredDiscard> _deferredDiscards = [];

    private UpdateCandidate? _latestCandidate;
    private TaskCompletionSource? _activePrepare;
    private int _gateUsers;
    private bool _stopping;
    private bool _disposed;

    internal Action? BeforePrepareGateRelease { get; set; }

    public UpdateKind Kind => environment.Kind;

    public bool IsUpdatable => environment.Kind switch
    {
        UpdateKind.Portable => true,
        UpdateKind.FrameworkDependent => store.Load().AllowFrameworkDependentUpdate,
        _ => false,
    };

    public string DefaultRepositorySlug => environment.RepositorySlug;

    public Version CurrentVersion => environment.CurrentVersion;

    public UpdateCandidate? LatestCandidate
    {
        get
        {
            lock (_syncLock)
            {
                return _latestCandidate;
            }
        }
    }

    public bool HasPreparedUpdate => installer.ReadPending() is not null;

    public string? PreparedVersion => installer.ReadPending()?.Version;

    public void Dispose()
    {
        bool canDisposeResources;

        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopping = true;
            canDisposeResources = _gateUsers == 0 && _activePrepare is null;
        }

        try
        {
            _shutdownCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        if (canDisposeResources)
        {
            _prepareGate.Dispose();
            _shutdownCts.Dispose();
        }
    }

    public async Task CancelPendingDownloadsAsync(CancellationToken cancellationToken)
    {
        Task? active;

        lock (_syncLock)
        {
            if (_disposed)
            {
                return;
            }

            _stopping = true;
            active = _activePrepare?.Task;
        }

        try
        {
            await _shutdownCts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (active is not null)
        {
            await active.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<UpdateCandidate?> CheckNowAsync(CancellationToken cancellationToken)
    {
        var skippedVersion = store.Load().SkippedVersion;

        UpdateCandidate? candidate;

        try
        {
            candidate = await checker.CheckAsync(skippedVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Не удалось проверить наличие обновлений");
            return null;
        }

        var settings = store.Load();
        settings.LastCheckUtc = DateTimeOffset.UtcNow;
        store.Save(settings);

        if (candidate is null)
        {
            return null;
        }

        if (string.Equals(settings.SkippedVersion, candidate.Version.ToString(), StringComparison.Ordinal))
        {
            logger.LogInformation("Версия {Version} пропущена, пока шла проверка обновлений", candidate.Version);
            return null;
        }

        lock (_syncLock)
        {
            _latestCandidate = candidate;
        }

        await eventBus
            .PublishAsync(new UpdateAvailable(candidate.Version.ToString(), candidate.NotesUrl), cancellationToken)
            .ConfigureAwait(false);

        return candidate;
    }

    public async Task PrepareAsync(UpdateCandidate candidate, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (!await PrepareCoreAsync(candidate, progress, false, cancellationToken).ConfigureAwait(false))
        {
            throw new UpdateException($"Версия {candidate.Version} пропущена, обновление отменено.");
        }
    }

    public async Task<bool> TryPrepareSilentlyAsync(UpdateCandidate candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        if (store.Load().ApplyMode != CoreUpdateApplyMode.SilentOnExit || !IsUpdatable)
        {
            return false;
        }

        var version = candidate.Version.ToString();
        var alreadyPrepared = string.Equals(PreparedVersion, version, StringComparison.Ordinal);
        bool prepared;

        try
        {
            prepared = await PrepareCoreAsync(candidate, null, true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Не удалось загрузить обновление {Version} в фоне, оно останется доступным для ручной установки", version);
            return false;
        }

        if (prepared && alreadyPrepared)
        {
            logger.LogDebug("Обновление {Version} уже загружено и ждёт выхода из приложения", version);
        }
        else if (prepared)
        {
            logger.LogInformation("Обновление {Version} загружено в фоне и будет установлено при выходе из приложения", version);
        }

        return prepared;
    }

    public async Task<bool> DiscardPreparedUpdateAsync(CancellationToken cancellationToken)
    {
        var discarded = RequestDiscard(null, "автоустановка при выходе выключена");

        await PublishDiscardedAsync(discarded, cancellationToken).ConfigureAwait(false);

        return discarded.Count > 0;
    }

    public void SkipVersion(string version)
    {
        ArgumentException.ThrowIfNullOrEmpty(version);

        var settings = store.Load();
        settings.SkippedVersion = version;
        store.Save(settings);

        RequestDiscard(version, "версия пропущена");

        lock (_syncLock)
        {
            if (_latestCandidate is not null
                && string.Equals(_latestCandidate.Version.ToString(), version, StringComparison.Ordinal))
            {
                _latestCandidate = null;
            }
        }

        logger.LogInformation("Версия {Version} помечена как пропущенная", version);
    }

    internal static bool DiscardPendingFiles(string stagingDirectory, PendingUpdate pending)
    {
        var fullStagingDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingDirectory));
        var pendingPath = Path.Combine(fullStagingDirectory, PendingUpdateStore.FileName);

        PendingUpdateStore.Remove(fullStagingDirectory);

        if (File.Exists(pendingPath))
        {
            return false;
        }

        var stagedDirectory = Path.GetDirectoryName(Path.GetFullPath(pending.StagedPath));

        if (string.Equals(
                stagedDirectory is null ? null : Path.TrimEndingDirectorySeparator(stagedDirectory),
                fullStagingDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            UpdateDirectory.TryDelete(pending.StagedPath);
        }

        return true;
    }

    private static string? DiscardReason(CoreUpdateSettings settings, string version, bool silent)
    {
        if (string.Equals(settings.SkippedVersion, version, StringComparison.Ordinal))
        {
            return "версия пропущена";
        }

        return silent && settings.ApplyMode != CoreUpdateApplyMode.SilentOnExit
            ? "автоустановка при выходе выключена"
            : null;
    }

    private async Task<bool> PrepareCoreAsync(
        UpdateCandidate candidate,
        IProgress<int>? progress,
        bool silent,
        CancellationToken cancellationToken)
    {
        if (!IsUpdatable)
        {
            throw new UpdateException("Эта сборка не обновляется автоматически.");
        }

        var version = candidate.Version.ToString();

        lock (_syncLock)
        {
            if (_disposed)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            _gateUsers++;
        }

        TaskCompletionSource? activeDone = null;
        var discardedVersions = new List<string>();
        string? discardReason = null;
        string? discardedViaReason = null;
        var matchedCurrentVersion = false;
        var preparedSucceeded = false;
        ExceptionDispatchInfo? failure = null;
        CancellationToken effectiveToken;

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdownCts.Token);
            effectiveToken = linkedCts.Token;

            await _prepareGate.WaitAsync(effectiveToken).ConfigureAwait(false);

            try
            {
                try
                {
                    lock (_syncLock)
                    {
                        if (_stopping)
                        {
                            throw new OperationCanceledException(_shutdownCts.Token);
                        }

                        activeDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
                        _activePrepare = activeDone;
                    }

                    if (string.Equals(PreparedVersion, version, StringComparison.Ordinal))
                    {
                        progress?.Report(100);
                    }
                    else
                    {
                        await installer.PrepareAsync(candidate, progress, effectiveToken).ConfigureAwait(false);
                    }

                    preparedSucceeded = true;
                    discardReason = DiscardReason(store.Load(), version, silent);

                    if (discardReason is not null)
                    {
                        matchedCurrentVersion = true;
                        discardedViaReason = version;

                        if (TryDiscard(version, discardReason))
                        {
                            discardedVersions.Add(version);
                        }
                    }
                }
                catch (Exception exception)
                {
                    failure = ExceptionDispatchInfo.Capture(exception);
                }
                finally
                {
                    try
                    {
                        var preserveVersion = preparedSucceeded && !silent && discardReason is null ? version : null;
                        var fallbackPreparedVersion = preparedSucceeded && discardedViaReason is null ? version : null;

                        DrainDeferredDiscardsLocked(
                            discardedVersions,
                            ref matchedCurrentVersion,
                            currentVersion: preparedSucceeded ? version : null,
                            preserveVersion: preserveVersion,
                            skipVersion: discardedViaReason,
                            fallbackPreparedVersion: fallbackPreparedVersion);

                        BeforePrepareGateRelease?.Invoke();
                    }
                    finally
                    {
                        _prepareGate.Release();
                    }
                }

                if (HasDeferredDiscards())
                {
                    var preserveVersion = preparedSucceeded && !silent && discardReason is null ? version : null;
                    var fallbackPreparedVersion = preparedSucceeded && discardedViaReason is null && !matchedCurrentVersion ? version : null;

                    DrainDeferredDiscardsIfIdle(
                        discardedVersions,
                        ref matchedCurrentVersion,
                        currentVersion: preparedSucceeded ? version : null,
                        preserveVersion: preserveVersion,
                        skipVersion: discardedViaReason,
                        fallbackPreparedVersion: fallbackPreparedVersion);
                }
            }
            finally
            {
                lock (_syncLock)
                {
                    if (ReferenceEquals(_activePrepare, activeDone))
                    {
                        _activePrepare = null;
                    }
                }

                activeDone?.TrySetResult();
            }

            if (failure is not null)
            {
                await PublishDiscardedBestEffortAsync(discardedVersions).ConfigureAwait(false);
                failure.Throw();
            }

            if (discardReason is not null
                || matchedCurrentVersion
                || DiscardReason(store.Load(), version, silent) is not null)
            {
                await PublishDiscardedAsync(discardedVersions, effectiveToken).ConfigureAwait(false);
                return false;
            }

            await PublishDiscardedAsync(discardedVersions, effectiveToken).ConfigureAwait(false);
            await eventBus.PublishAsync(new UpdatePrepared(version), effectiveToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            ReleaseGateUser();
        }
    }

    private List<string> RequestDiscard(string? version, string reason)
    {
        lock (_syncLock)
        {
            _deferredDiscards.Add(new(version, reason));
        }

        var discardedVersions = new List<string>();
        var matched = false;

        if (!DrainDeferredDiscardsIfIdle(discardedVersions, ref matched))
        {
            logger.LogDebug("Идёт загрузка обновления – удаление загруженного ({Reason}) выполнится после её завершения", reason);
        }

        return discardedVersions;
    }

    private bool HasDeferredDiscards()
    {
        lock (_syncLock)
        {
            return _deferredDiscards.Count > 0;
        }
    }

    private bool DrainDeferredDiscardsIfIdle(
        List<string> discardedVersions,
        ref bool matchedCurrentVersion,
        string? currentVersion = null,
        string? preserveVersion = null,
        string? skipVersion = null,
        string? fallbackPreparedVersion = null)
    {
        lock (_syncLock)
        {
            if (_disposed)
            {
                return false;
            }

            _gateUsers++;
        }

        var acquiredAny = false;

        try
        {
            while (true)
            {
                if (!_prepareGate.Wait(0))
                {
                    return acquiredAny;
                }

                acquiredAny = true;

                try
                {
                    DrainDeferredDiscardsLocked(
                        discardedVersions,
                        ref matchedCurrentVersion,
                        currentVersion: currentVersion,
                        preserveVersion: preserveVersion,
                        skipVersion: skipVersion,
                        fallbackPreparedVersion: fallbackPreparedVersion);
                }
                finally
                {
                    _prepareGate.Release();
                }

                if (!HasDeferredDiscards())
                {
                    return true;
                }
            }
        }
        finally
        {
            ReleaseGateUser();
        }
    }

    private void ReleaseGateUser()
    {
        bool disposeResources;

        lock (_syncLock)
        {
            _gateUsers--;
            disposeResources = _disposed && _gateUsers == 0 && _activePrepare is null;
        }

        if (disposeResources)
        {
            _prepareGate.Dispose();
            _shutdownCts.Dispose();
        }
    }

    private void DrainDeferredDiscardsLocked(
        List<string> discardedVersions,
        ref bool matchedCurrentVersion,
        string? currentVersion = null,
        string? preserveVersion = null,
        string? skipVersion = null,
        string? fallbackPreparedVersion = null)
    {
        List<DeferredDiscard> batch;

        lock (_syncLock)
        {
            if (_deferredDiscards.Count == 0)
            {
                return;
            }

            batch = [.. _deferredDiscards];
            _deferredDiscards.Clear();
        }

        var attemptedVersions = new HashSet<string>(StringComparer.Ordinal);

        if (skipVersion is not null)
        {
            attemptedVersions.Add(skipVersion);
        }

        foreach (var version in discardedVersions)
        {
            attemptedVersions.Add(version);
        }

        foreach (var request in batch)
        {
            var prepared = PreparedVersion ?? fallbackPreparedVersion;

            if (prepared is null || attemptedVersions.Contains(prepared))
            {
                continue;
            }

            if (request.Version is not null && !string.Equals(prepared, request.Version, StringComparison.Ordinal))
            {
                continue;
            }

            if (request.Version is null && string.Equals(prepared, preserveVersion, StringComparison.Ordinal))
            {
                continue;
            }

            if (currentVersion is not null && string.Equals(prepared, currentVersion, StringComparison.Ordinal))
            {
                matchedCurrentVersion = true;
            }

            attemptedVersions.Add(prepared);

            if (TryDiscard(prepared, request.Reason) && !discardedVersions.Contains(prepared, StringComparer.Ordinal))
            {
                discardedVersions.Add(prepared);
            }
        }
    }

    private async Task PublishDiscardedAsync(List<string> versions, CancellationToken cancellationToken)
    {
        foreach (var version in versions)
        {
            await eventBus.PublishAsync(new UpdateDiscarded(version), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishDiscardedBestEffortAsync(List<string> versions)
    {
        if (versions.Count == 0)
        {
            return;
        }

        try
        {
            await PublishDiscardedAsync(versions, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Не удалось уведомить об удалении загруженного обновления после ошибки загрузки");
        }
    }

    private bool TryDiscard(string version, string reason)
    {
        var pending = installer.ReadPending();

        if (pending is null || !string.Equals(pending.Version, version, StringComparison.Ordinal))
        {
            return false;
        }

        if (!DiscardPendingFiles(environment.StagingDirectory, pending))
        {
            logger.LogWarning("Не удалось удалить загруженное обновление {Version} ({Reason}) – оно установится при выходе из приложения", version, reason);
            return false;
        }

        logger.LogInformation("Загруженное обновление {Version} удалено: {Reason}", version, reason);
        return true;
    }

    private readonly record struct DeferredDiscard(string? Version, string Reason);
}
