using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public sealed class BotConnectionManager : IBotConnectionController, IAsyncDisposable
{
    private enum BotCancellationReason
    {
        None = 0,
        UserRequested = 1,
        Shutdown = 2,
    }

    private readonly record struct PhaseTransition(
        StreamingPlatform Platform,
        BotLifecyclePhase PreviousPlatformPhase,
        BotLifecyclePhase PlatformPhase,
        Exception? PlatformException,
        bool AggregateChanged,
        BotLifecyclePhase PreviousAggregatePhase,
        BotLifecyclePhase AggregatePhase,
        Exception? AggregateException,
        string? Reason);

    private sealed class PlatformSlot(IPlatformConnectionAdapter adapter)
    {
        public IPlatformConnectionAdapter Adapter { get; } = adapter;

        public StreamingPlatform Platform => Adapter.Platform;

        public SemaphoreSlim StopGate { get; } = new(1, 1);

        public Lock StopSync { get; } = new();

        public BotLifecyclePhase Phase { get; set; } = BotLifecyclePhase.Idle;

        public BotCancellationReason CancellationReason { get; set; } = BotCancellationReason.None;

        public CancellationTokenSource? ConnectCts { get; set; }

        public CancellationTokenSource? ActiveStopCts { get; set; }

        public Task? ConnectionTask { get; set; }

        public bool HasActiveSession { get; set; }

        public bool IsConnecting { get; set; }

        public bool IsBusy => IsConnecting || ConnectionTask is { IsCompleted: false };

        public async Task CancelActiveStopAsync()
        {
            CancellationTokenSource? runningStop;

            lock (StopSync)
            {
                runningStop = ActiveStopCts;
                ActiveStopCts = null;
            }

            if (runningStop is null)
            {
                return;
            }

            try
            {
                await runningStop.CancelAsync().ConfigureAwait(false);
            }
            finally
            {
                runningStop.Dispose();
            }
        }

        public void ReleaseOwnStop(CancellationTokenSource stopCts)
        {
            lock (StopSync)
            {
                if (!ReferenceEquals(ActiveStopCts, stopCts))
                {
                    return;
                }

                ActiveStopCts = null;
            }

            stopCts.Dispose();
        }
    }

    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ForcedStopTimeout = TimeSpan.FromSeconds(6);

    private readonly AppHost _sharedHost;
    private readonly ChatPipelineHandler? _chatPipelineHandler;
    private readonly IEventBus _eventBus;
    private readonly ILogger<BotConnectionManager> _logger;
    private readonly Dictionary<StreamingPlatform, PlatformSlot> _slots = [];
    private readonly List<PlatformSlot> _orderedSlots = [];
    private readonly SemaphoreSlim _sharedHostGate = new(1, 1);
    private readonly Lock _stateLock = new();

    private int _activeSharedLeaseCount;
    private BotLifecyclePhase _aggregatePhase = BotLifecyclePhase.Idle;
    private Exception? _aggregateException;
    private BotLifecyclePhase _lastTerminalInactivePhase = BotLifecyclePhase.Idle;
    private Exception? _lastTerminalInactiveException;
    private bool _disposed;

    public BotConnectionManager(
        AppHost sharedHost,
        IEnumerable<IPlatformConnectionAdapter> platforms,
        ChatPipelineHandler? chatPipelineHandler,
        IEventBus eventBus,
        ILogger<BotConnectionManager> logger)
    {
        ArgumentNullException.ThrowIfNull(sharedHost);
        ArgumentNullException.ThrowIfNull(platforms);
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(logger);

        _sharedHost = sharedHost;
        _chatPipelineHandler = chatPipelineHandler;
        _eventBus = eventBus;
        _logger = logger;

        foreach (var adapter in platforms)
        {
            var slot = new PlatformSlot(adapter);
            _slots.Add(adapter.Platform, slot);
            _orderedSlots.Add(slot);
        }

        _logger.LogDebug("Менеджер подключений бота инициализирован ({Count} площадок)", _orderedSlots.Count);
    }

    public bool IsBusy
    {
        get
        {
            lock (_stateLock)
            {
                return _orderedSlots.Any(slot => slot.IsBusy);
            }
        }
    }

    public BotLifecyclePhase CurrentPhase
    {
        get
        {
            lock (_stateLock)
            {
                return _aggregatePhase;
            }
        }
    }

    public bool IsPlatformBusy(StreamingPlatform platform)
    {
        lock (_stateLock)
        {
            return _slots.TryGetValue(platform, out var slot) && slot.IsBusy;
        }
    }

    public bool IsPlatformConnected(StreamingPlatform platform)
    {
        lock (_stateLock)
        {
            return _slots.TryGetValue(platform, out var slot)
                && slot.HasActiveSession
                && slot.Phase == BotLifecyclePhase.Connected;
        }
    }

    public async Task<string?> RetargetPlatformAsync(
        StreamingPlatform platform,
        PlatformChatSessionTarget target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!_slots.TryGetValue(platform, out var slot))
        {
            return null;
        }

        lock (_stateLock)
        {
            if (!slot.HasActiveSession)
            {
                return null;
            }
        }

        var progress = new Progress<string>(ReportProgress);
        var channel = await slot.Adapter.RetargetSessionAsync(target, progress, cancellationToken).ConfigureAwait(false);
        await _eventBus.PublishAsync(new BotJoinedChannel(platform, channel), cancellationToken).ConfigureAwait(false);
        return channel;
    }

    public async Task MarkPlatformSwitchFailedAsync(StreamingPlatform platform, Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (!_slots.TryGetValue(platform, out var slot))
        {
            return;
        }

        var progress = new Progress<string>(ReportProgress);
        var hadSession = false;

        lock (_stateLock)
        {
            hadSession = slot.HasActiveSession;
        }

        if (hadSession)
        {
            if (slot.Adapter.Host.IsRunning)
            {
                await RollbackHostAsync(slot.Adapter.Host, progress).ConfigureAwait(false);
            }

            await ReleaseSharedHostAsync(progress, ForcedStopTimeout).ConfigureAwait(false);
        }

        _chatPipelineHandler?.Reset(platform);
        slot.Adapter.EndSession();

        PhaseTransition transition;
        lock (_stateLock)
        {
            slot.HasActiveSession = false;
            slot.IsConnecting = false;
            transition = TransitionSlotLocked(
                slot,
                BotLifecyclePhase.Failed,
                error,
                "нет активного чата после ошибки переключения");
        }

        PublishTransitionFireAndForget(transition);
    }

    public BotLifecyclePhase GetPhase(StreamingPlatform platform)
    {
        lock (_stateLock)
        {
            return _slots.TryGetValue(platform, out var slot) ? slot.Phase : BotLifecyclePhase.Idle;
        }
    }

    public Task WaitForConnectionAsync()
    {
        Task[] tasks;

        lock (_stateLock)
        {
            tasks = _orderedSlots
                .Select(slot => slot.ConnectionTask)
                .Where(task => task is not null)
                .ToArray()!;
        }

        return tasks.Length == 0 ? Task.CompletedTask : Task.WhenAll(tasks);
    }

    public Task WaitForConnectionAsync(StreamingPlatform platform)
    {
        lock (_stateLock)
        {
            return _slots.TryGetValue(platform, out var slot)
                ? slot.ConnectionTask ?? Task.CompletedTask
                : Task.CompletedTask;
        }
    }

    public void StartConnection()
    {
        _logger.LogDebug("Попытка запуска подключения по всем включённым площадкам");

        var started = 0;

        foreach (var slot in _orderedSlots)
        {
            if (!slot.Adapter.IsEnabled)
            {
                continue;
            }

            if (TryBeginSlotConnection(slot))
            {
                started++;
            }
        }

        if (started == 0)
        {
            _logger.LogWarning("Попытка запуска подключения отклонена: включённые площадки отсутствуют либо уже подключаются или подключены");
            throw new InvalidOperationException("Connection is already in progress");
        }
    }

    public void StartConnection(StreamingPlatform platform)
    {
        _logger.LogDebug("Попытка запуска подключения для площадки {Platform}", platform);

        if (!_slots.TryGetValue(platform, out var slot))
        {
            throw new InvalidOperationException($"Площадка {platform} не зарегистрирована.");
        }

        if (!TryBeginSlotConnection(slot))
        {
            _logger.LogWarning("Попытка запуска подключения площадки {Platform} отклонена: процесс уже выполняется", platform);
            throw new InvalidOperationException("Connection is already in progress");
        }
    }

    public void CancelConnection()
    {
        foreach (var slot in _orderedSlots)
        {
            CancelSlotConnection(slot, BotCancellationReason.UserRequested);
        }
    }

    public void CancelConnection(StreamingPlatform platform)
    {
        if (!_slots.TryGetValue(platform, out var slot))
        {
            return;
        }

        CancelSlotConnection(slot, BotCancellationReason.UserRequested);
    }

    public Task StopAsync(BotStopMode mode = BotStopMode.Graceful)
    {
        return _orderedSlots.Count == 0
            ? Task.CompletedTask
            : Task.WhenAll(_orderedSlots.Select(slot => StopSlotAsync(slot, mode)));
    }

    public Task StopAsync(StreamingPlatform platform, BotStopMode mode = BotStopMode.Graceful)
    {
        return _slots.TryGetValue(platform, out var slot)
            ? StopSlotAsync(slot, mode)
            : Task.CompletedTask;
    }

    public Task ShutdownAsync(BotStopMode mode = BotStopMode.Graceful)
    {
        _logger.LogDebug("Инициализация полной остановки бота (ShutdownAsync, режим {Mode})", mode);
        return StopAsync(mode);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _logger.LogDebug("Освобождение ресурсов BotConnectionManager (DisposeAsync)");

        try
        {
            await ShutdownAsync(BotStopMode.Forced).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Ошибка во время финального ShutdownAsync в DisposeAsync");
        }

        lock (_stateLock)
        {
            _disposed = true;
            foreach (var slot in _orderedSlots)
            {
                slot.ConnectCts?.Dispose();
                slot.ConnectCts = null;
            }
        }

        foreach (var slot in _orderedSlots)
        {
            slot.StopGate.Dispose();
        }

        _sharedHostGate.Dispose();
    }

    private bool TryBeginSlotConnection(PlatformSlot slot)
    {
        CancellationTokenSource cts;
        PhaseTransition transition;

        lock (_stateLock)
        {
            if (_disposed || slot.IsBusy || slot.HasActiveSession || slot.Phase == BotLifecyclePhase.Disconnecting)
            {
                return false;
            }

            slot.ConnectCts?.Dispose();
            cts = new CancellationTokenSource();
            slot.ConnectCts = cts;
            slot.CancellationReason = BotCancellationReason.None;
            slot.IsConnecting = true;
            transition = TransitionSlotLocked(slot, BotLifecyclePhase.Connecting, null, "запрошено подключение бота");
        }

        PublishTransitionFireAndForget(transition);

        var connectTask = ConnectPlatformAsync(slot, cts, cts.Token);

        lock (_stateLock)
        {
            if (ReferenceEquals(slot.ConnectCts, cts))
            {
                slot.ConnectionTask = connectTask;
            }
        }

        return true;
    }

    private void CancelSlotConnection(PlatformSlot slot, BotCancellationReason reason)
    {
        CancellationTokenSource? cts;

        lock (_stateLock)
        {
            cts = slot.ConnectCts;

            if (cts is null || cts.IsCancellationRequested)
            {
                return;
            }

            slot.CancellationReason = reason;
        }

        if (reason == BotCancellationReason.UserRequested)
        {
            _logger.LogInformation("Пользователь запросил отмену подключения площадки {Platform}", slot.Platform);
        }
        else
        {
            _logger.LogDebug("Отмена незавершённого подключения площадки {Platform} при остановке", slot.Platform);
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ConnectPlatformAsync(PlatformSlot slot, CancellationTokenSource cts, CancellationToken ct)
    {
        var platform = slot.Platform;
        var progress = new Progress<string>(ReportProgress);
        var sharedAcquired = false;

        try
        {
            var channel = await slot.Adapter.BeginSessionAsync(progress, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            await AcquireSharedHostAsync(progress, ct).ConfigureAwait(false);
            sharedAcquired = true;
            ct.ThrowIfCancellationRequested();

            await slot.Adapter.Host.StartAsync(progress, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            _logger.LogDebug("Публикация BotJoinedChannel для площадки {Platform}, канала {Channel}", platform, channel);
            await _eventBus.PublishAsync(new BotJoinedChannel(platform, channel), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            PhaseTransition transition;

            lock (_stateLock)
            {
                slot.HasActiveSession = true;
                slot.IsConnecting = false;
                transition = TransitionSlotLocked(slot, BotLifecyclePhase.Connected, null, null);
            }

            ReportProgress("Подключение установлено успешно");
            _logger.LogInformation("Подключение площадки {Platform} успешно завершено (канал {Channel})", platform, channel);
            PublishTransitionFireAndForget(transition);
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Процесс подключения площадки {Platform} был отменён", platform);
            await RollbackConnectAsync(slot, progress, sharedAcquired).ConfigureAwait(false);

            PhaseTransition transition;

            lock (_stateLock)
            {
                slot.IsConnecting = false;
                transition = TransitionSlotLocked(
                    slot,
                    BotLifecyclePhase.Cancelled,
                    null,
                    DescribeCancellation(slot.CancellationReason));
            }

            PublishTransitionFireAndForget(transition);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Произошла ошибка в процессе подключения площадки {Platform}", platform);
            ReportProgress($"Ошибка подключения: {exception.Message}");
            await RollbackConnectAsync(slot, progress, sharedAcquired).ConfigureAwait(false);

            PhaseTransition transition;

            lock (_stateLock)
            {
                slot.IsConnecting = false;
                transition = TransitionSlotLocked(
                    slot,
                    BotLifecyclePhase.Failed,
                    exception,
                    "ошибка подключения");
            }

            PublishTransitionFireAndForget(transition);
        }
    }

    private async Task RollbackConnectAsync(PlatformSlot slot, IProgress<string> progress, bool sharedAcquired)
    {
        if (slot.Adapter.Host.IsRunning)
        {
            await RollbackHostAsync(slot.Adapter.Host, progress).ConfigureAwait(false);
        }

        _chatPipelineHandler?.Reset(slot.Platform);
        slot.Adapter.EndSession();

        if (sharedAcquired)
        {
            await ReleaseSharedHostAsync(progress, ForcedStopTimeout).ConfigureAwait(false);
        }
    }

    private async Task StopSlotAsync(PlatformSlot slot, BotStopMode mode)
    {
        if (mode == BotStopMode.Forced)
        {
            await slot.CancelActiveStopAsync().ConfigureAwait(false);
        }

        CancelSlotConnection(slot, BotCancellationReason.Shutdown);

        Task? pendingConnect;

        lock (_stateLock)
        {
            pendingConnect = slot.ConnectionTask;
        }

        if (pendingConnect is not null)
        {
            try
            {
                await pendingConnect.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Незавершённое подключение площадки {Platform} завершилось с ошибкой при остановке", slot.Platform);
            }
        }

        await slot.StopGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            PhaseTransition disconnectingTransition;

            lock (_stateLock)
            {
                if (!slot.HasActiveSession)
                {
                    return;
                }

                disconnectingTransition = TransitionSlotLocked(
                    slot,
                    BotLifecyclePhase.Disconnecting,
                    null,
                    mode == BotStopMode.Forced ? "принудительная остановка" : "штатная остановка");
            }

            await PublishTransitionAsync(disconnectingTransition).ConfigureAwait(false);

            var progressReporter = new Progress<string>(ReportProgress);
            var stopTimeout = mode == BotStopMode.Forced ? ForcedStopTimeout : GracefulStopTimeout;
            var stopCts = new CancellationTokenSource(stopTimeout);
            var stopToken = stopCts.Token;

            lock (slot.StopSync)
            {
                slot.ActiveStopCts = stopCts;
            }

            try
            {
                await slot.Adapter.Host.StopAsync(progressReporter, stopToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                _logger.LogWarning(
                    exception,
                    "Остановка компонентов PlatformHost {Platform} прервана по таймауту {Timeout} c (режим {Mode})",
                    slot.Platform,
                    stopTimeout.TotalSeconds,
                    mode);
                ReportProgress("Остановка компонентов прервана по таймауту");
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Произошла ошибка при остановке компонентов PlatformHost {Platform}", slot.Platform);
                ReportProgress($"Ошибка остановки компонентов: {exception.Message}");
            }
            finally
            {
                slot.ReleaseOwnStop(stopCts);
            }

            await ReleaseSharedHostAsync(progressReporter, stopTimeout).ConfigureAwait(false);

            _chatPipelineHandler?.Reset(slot.Platform);
            slot.Adapter.EndSession();

            PhaseTransition disconnectedTransition;

            lock (_stateLock)
            {
                slot.HasActiveSession = false;
                disconnectedTransition = TransitionSlotLocked(slot, BotLifecyclePhase.Disconnected, null, null);
            }

            PublishTransitionFireAndForget(disconnectedTransition);
        }
        finally
        {
            slot.StopGate.Release();
        }
    }

    private async Task AcquireSharedHostAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        await _sharedHostGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_activeSharedLeaseCount == 0)
            {
                try
                {
                    await _sharedHost.StartAsync(progress, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    if (_sharedHost.IsRunning)
                    {
                        await RollbackHostAsync(_sharedHost, progress).ConfigureAwait(false);
                    }

                    throw;
                }
            }

            _activeSharedLeaseCount++;
        }
        finally
        {
            _sharedHostGate.Release();
        }
    }

    private async Task ReleaseSharedHostAsync(IProgress<string> progress, TimeSpan stopTimeout)
    {
        await _sharedHostGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            if (_activeSharedLeaseCount <= 0)
            {
                return;
            }

            _activeSharedLeaseCount--;

            if (_activeSharedLeaseCount > 0 || !_sharedHost.IsRunning)
            {
                return;
            }

            try
            {
                using var sharedStopCts = new CancellationTokenSource(stopTimeout);
                await _sharedHost.StopAsync(progress, sharedStopCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception)
            {
                _logger.LogWarning(exception, "Остановка общих компонентов AppHost прервана по таймауту {Timeout} c", stopTimeout.TotalSeconds);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Произошла ошибка при остановке общих компонентов AppHost");
            }
        }
        finally
        {
            _sharedHostGate.Release();
        }
    }

    private async Task RollbackHostAsync(AppHost host, IProgress<string> progress)
    {
        _logger.LogInformation("Подключение не завершено – останавливаю уже запущенные компоненты бота");

        try
        {
            using var stopCts = new CancellationTokenSource(ForcedStopTimeout);
            await host.StopAsync(progress, stopCts.Token).WaitAsync(ForcedStopTimeout, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Не удалось остановить компоненты незавершённого подключения");
        }
    }

    private PhaseTransition TransitionSlotLocked(
        PlatformSlot slot,
        BotLifecyclePhase phase,
        Exception? exception,
        string? reason)
    {
        var previousSlotPhase = slot.Phase;
        slot.Phase = phase;

        if (phase is BotLifecyclePhase.Disconnected or BotLifecyclePhase.Cancelled or BotLifecyclePhase.Failed)
        {
            _lastTerminalInactivePhase = phase;
            _lastTerminalInactiveException = exception;
        }

        var (nextAggregate, nextAggregateException) = ComputeAggregateLocked();
        var previousAggregate = _aggregatePhase;
        var aggregateChanged = nextAggregate != _aggregatePhase || !Equals(nextAggregateException, _aggregateException);

        _aggregatePhase = nextAggregate;
        _aggregateException = nextAggregateException;

        return new PhaseTransition(
            slot.Platform,
            previousSlotPhase,
            phase,
            exception,
            aggregateChanged,
            previousAggregate,
            nextAggregate,
            nextAggregateException,
            reason);
    }

    private (BotLifecyclePhase Phase, Exception? Exception) ComputeAggregateLocked()
    {
        if (_orderedSlots.Any(slot => slot.Phase == BotLifecyclePhase.Connected))
        {
            return (BotLifecyclePhase.Connected, null);
        }

        if (_orderedSlots.Any(slot => slot.Phase == BotLifecyclePhase.Connecting))
        {
            return (BotLifecyclePhase.Connecting, null);
        }

        if (_orderedSlots.Any(slot => slot.Phase == BotLifecyclePhase.Disconnecting))
        {
            return (BotLifecyclePhase.Disconnecting, null);
        }

        return (_lastTerminalInactivePhase, _lastTerminalInactiveException);
    }

    private async Task PublishTransitionAsync(PhaseTransition transition)
    {
        LogTransition(transition);

        await _eventBus.PublishAsync(
            new PlatformConnectionChanged(transition.Platform, transition.PlatformPhase, transition.PlatformException),
            CancellationToken.None).ConfigureAwait(false);

        if (transition.AggregateChanged)
        {
            await _eventBus.PublishAsync(
                new BotLifecyclePhaseChanged(transition.AggregatePhase, transition.AggregateException),
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void PublishTransitionFireAndForget(PhaseTransition transition)
    {
        LogTransition(transition);

        _ = _eventBus.PublishAsync(
            new PlatformConnectionChanged(transition.Platform, transition.PlatformPhase, transition.PlatformException),
            CancellationToken.None);

        if (transition.AggregateChanged)
        {
            _ = _eventBus.PublishAsync(
                new BotLifecyclePhaseChanged(transition.AggregatePhase, transition.AggregateException),
                CancellationToken.None);
        }
    }

    private void LogTransition(PhaseTransition transition)
    {
        if (string.IsNullOrEmpty(transition.Reason))
        {
            _logger.LogInformation(
                "Фаза площадки {Platform}: {PreviousPhase} → {Phase}",
                transition.Platform,
                transition.PreviousPlatformPhase,
                transition.PlatformPhase);
        }
        else
        {
            _logger.LogInformation(
                "Фаза площадки {Platform}: {PreviousPhase} → {Phase} (причина: {Reason})",
                transition.Platform,
                transition.PreviousPlatformPhase,
                transition.PlatformPhase,
                transition.Reason);
        }
    }

    private void ReportProgress(string message)
    {
        _ = _eventBus.PublishAsync(new BotConnectionStatusUpdated(message), CancellationToken.None);
        _logger.LogInformation("{Message}", message);
    }

    private static string DescribeCancellation(BotCancellationReason reason)
    {
        return reason switch
        {
            BotCancellationReason.UserRequested => "отмена по запросу пользователя",
            BotCancellationReason.Shutdown => "отмена при завершении работы",
            _ => "отмена без явного запроса",
        };
    }
}
