using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Statistics;

internal abstract class SingleFileStatisticsUnit<TSnapshot>(
    string relativePath,
    string displayName,
    ILogger logger)
    : IPeriodicStatisticsUnit, IExternalWriteParticipant, IDisposable
    where TSnapshot : class
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _state;
    private bool _rewrittenExternally;
    private bool _rewriteReported;
    private bool _disposed;

    public StatisticsLoadState LoadState => (StatisticsLoadState)Volatile.Read(ref _state);

    public bool IsLoaded => LoadState == StatisticsLoadState.Loaded;

    public IReadOnlyCollection<string> OwnedRelativePaths { get; } = [relativePath];

    protected abstract bool HasChanges { get; }

    protected virtual bool ReloadWhenAlreadyLoadedOnStart => false;

    protected abstract Task<StatisticsReadResult<TSnapshot>> ReadAsync(CancellationToken cancellationToken);

    protected abstract void ApplyLoaded(TSnapshot? data);

    protected abstract TSnapshot CreateSnapshotAndMarkSaved();

    protected abstract void MarkChanged();

    protected abstract Task WriteAsync(TSnapshot snapshot, CancellationToken cancellationToken);

    protected virtual void OnAfterLoaded(TSnapshot? data)
    {
    }

    public virtual void OnStarted()
    {
    }

    public async Task LoadForStartAsync(CancellationToken cancellationToken)
    {
        if (ReloadWhenAlreadyLoadedOnStart && LoadState == StatisticsLoadState.Loaded)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                if (LoadState == StatisticsLoadState.Loaded)
                {
                    SetState(StatisticsLoadState.NotRead);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (LoadState != StatisticsLoadState.NotRead)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (LoadState != StatisticsLoadState.NotRead)
            {
                return;
            }

            if (Volatile.Read(ref _rewrittenExternally))
            {
                if (!_rewriteReported)
                {
                    _rewriteReported = true;
                    logger.LogInformation(
                        "Статистика ({DisplayName}) переписана мимо приложения. До перезапуска она не перечитывается и не сохраняется, чтобы не затереть перенесённое",
                        displayName);
                }

                return;
            }

            try
            {
                var result = await ReadAsync(cancellationToken).ConfigureAwait(false);

                if (result.Failed)
                {
                    SetState(StatisticsLoadState.Abandoned);
                    logger.LogError(
                        "Файл статистики ({DisplayName}) не прочитан. Накопленное в этом сеансе сохраняться не будет, чтобы не затереть файл, и перечитывать его до перезапуска приложение не станет; рядом с файлом оставлена копия с суффиксом invalid",
                        displayName);
                    return;
                }

                ApplyLoaded(result.Value);
                SetState(StatisticsLoadState.Loaded);
                OnAfterLoaded(result.Value);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                SetState(StatisticsLoadState.Abandoned);
                logger.LogError(exception, "Сбой при загрузке статистики ({DisplayName})", displayName);
                throw new InvalidOperationException($"Ошибка загрузки статистики ({displayName}): {exception.Message}", exception);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask<bool> InvalidateAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var wasLoaded = LoadState == StatisticsLoadState.Loaded;

            if (wasLoaded)
            {
                SetState(StatisticsLoadState.Abandoned);
                logger.LogInformation(
                    "Статистика ({DisplayName}) больше не считается прочитанной: её файл меняют мимо приложения, накопленное в этом сеансе сохраняться не будет, а перечитывания до перезапуска не будет",
                    displayName);
            }

            return wasLoaded;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask RestoreAsync(bool wasLoaded)
    {
        if (!wasLoaded)
        {
            return;
        }

        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            SetState(StatisticsLoadState.Loaded);
            logger.LogInformation(
                "Статистика ({DisplayName}) снова считается прочитанной: её файл никто не менял",
                displayName);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void LatchExternalRewrite()
    {
        _gate.Wait(CancellationToken.None);

        try
        {
            Volatile.Write(ref _rewrittenExternally, true);
            SetState(StatisticsLoadState.Abandoned);
        }
        finally
        {
            _gate.Release();
        }
    }

    public ISnapshotSaveWork? PrepareSave(bool force)
    {
        if (!IsLoaded || (!force && !HasChanges))
        {
            return null;
        }

        var snapshot = CreateSnapshotAndMarkSaved();
        return new SnapshotSaveWork(this, snapshot);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            _gate.Dispose();
        }

        _disposed = true;
    }

    private void SetState(StatisticsLoadState state)
    {
        Volatile.Write(ref _state, (int)state);
    }

    private sealed class SnapshotSaveWork(SingleFileStatisticsUnit<TSnapshot> owner, TSnapshot snapshot) : ISnapshotSaveWork
    {
        public Task CommitAsync(CancellationToken cancellationToken)
        {
            return owner.WriteAsync(snapshot, cancellationToken);
        }

        public void Rollback()
        {
            owner.MarkChanged();
        }
    }
}
