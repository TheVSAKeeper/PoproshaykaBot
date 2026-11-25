using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class ObsInfoTileViewModel : DashboardTileViewModel, IDisposable
{
    private const string ConnectedStatus = "connected";
    private const string UnavailableStatus = "error";
    private const int ConnectedRefreshIntervalMs = 5000;
    private const int DisconnectedRefreshIntervalMs = 15000;
    private const int MinVolumeMeterDelayMs = 30;
    private const int MaxVolumeMeterDelayMs = 1000;
    private const int MaxAutoConnectBackoffSteps = 5;
    private const double MaxAutoConnectIntervalSeconds = 300;
    private static readonly TimeSpan AutoConnectRetryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan SecondaryRefreshDelay = TimeSpan.FromMilliseconds(500);

    private readonly ObsIntegrationStore _store;
    private readonly ObsIntegrationService _obsIntegration;
    private readonly IObsWebSocketClient _obsClient;
    private readonly IEventBus _bus;
    private readonly ILogger<ObsInfoTileViewModel> _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ToastHostViewModel? _toasts;
    private readonly ShellPreferences? _preferences;

    private readonly List<IDisposable> _subs = [];
    private readonly Lock _volumeMeterLock = new();

    private readonly Dictionary<string, (double Level, DateTimeOffset UpdatedAt)> _volumeTargets =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IUiTimer _refreshTimer;
    private readonly IUiTimer _volumeMeterTimer;
    private readonly IUiTimer _secondaryRefreshTimer;

    private readonly CancellationTokenSource _disposeCts = new();
    private ObsIntegrationSettings _settings = new();
    private CancellationTokenSource? _refreshCts;
    private DateTimeOffset _lastAutoConnectAttempt = DateTimeOffset.MinValue;
    private int _autoConnectFailures;
    private bool _refreshing;
    private bool _refreshQueued;
    private bool _disposed;
    private bool _previewing;

    [ObservableProperty]
    private string _sceneName = "–";

    [ObservableProperty]
    private string _connectionText = "○ OBS выкл.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOutputs))]
    [NotifyPropertyChangedFor(nameof(IsUnavailable))]
    [NotifyPropertyChangedFor(nameof(ShowSecondaryOutputs))]
    private string _connectionStatus = "off";

    [ObservableProperty]
    private string _connectionToolTip = "Статус подключения OBS";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAudioSources))]
    private bool _audioSourcesEmpty = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSecondaryOutputs))]
    [NotifyPropertyChangedFor(nameof(ShowSecondaryOutputs))]
    private bool _secondaryOutputsEmpty = true;

    public ObsInfoTileViewModel(
        ObsIntegrationStore store,
        ObsIntegrationService obsIntegration,
        IObsWebSocketClient obsClient,
        IEventBus bus,
        ILogger<ObsInfoTileViewModel> logger,
        IUiDispatcher uiDispatcher,
        ToastHostViewModel? toasts = null,
        ShellPreferences? preferences = null)
        : base("obs-info", "OBS", maxHeight: 720, minHeight: 154)
    {
        _store = store;
        _obsIntegration = obsIntegration;
        _obsClient = obsClient;
        _bus = bus;
        _logger = logger;
        _uiDispatcher = uiDispatcher;
        _toasts = toasts;
        _preferences = preferences;

        StreamCard = new(ObsOutputCardKind.Stream, obsIntegration, logger);
        RecordCard = new(ObsOutputCardKind.Record, obsIntegration, logger);

        HeaderActions.Add(new(MahApps.Metro.IconPacks.PackIconLucideKind.RefreshCw, RefreshCommand, toolTip: "Обновить данные OBS"));
        HeaderActions.Add(new(MahApps.Metro.IconPacks.PackIconLucideKind.RefreshCcw, RefreshChatSourcesCommand, toolTip: "Жёстко обновить чат-источники OBS (refreshnocache)"));

        _refreshTimer = uiDispatcher.CreateTimer(TimeSpan.FromMilliseconds(DisconnectedRefreshIntervalMs), OnRefreshTimerTick);
        _volumeMeterTimer = uiDispatcher.CreateTimer(TimeSpan.FromMilliseconds(MinVolumeMeterDelayMs), OnVolumeMeterTimerTick);
        _secondaryRefreshTimer = uiDispatcher.CreateTimer(SecondaryRefreshDelay, OnSecondaryRefreshTimerTick);

        Initialize();
    }

    public ObsOutputCardViewModel StreamCard { get; }

    public ObsOutputCardViewModel RecordCard { get; }

    public ObservableCollection<ObsSecondaryOutputViewModel> SecondaryOutputs { get; } = [];

    public ObservableCollection<ObsSourceMeterViewModel> AudioSources { get; } = [];

    public bool HasSecondaryOutputs => !SecondaryOutputsEmpty;

    public bool ShowSecondaryOutputs => HasOutputs && HasSecondaryOutputs;

    public bool HasAudioSources => !AudioSourcesEmpty;

    public bool HasOutputs => string.Equals(ConnectionStatus, ConnectedStatus, StringComparison.Ordinal);

    public bool IsUnavailable => string.Equals(ConnectionStatus, UnavailableStatus, StringComparison.Ordinal);

    public override PackIconLucideKind Icon => PackIconLucideKind.Video;

    public override bool SizesToContent => true;

    private void Initialize()
    {
        _settings = _store.Load();

        _subs.Add(_bus.SubscribeOnUi<ObsIntegrationSettingsChangedEvent>(OnObsIntegrationSettingsChanged));
        _obsClient.EventReceived += OnObsEventReceived;

        _volumeMeterTimer.Interval = TimeSpan.FromMilliseconds(ResolveVolumeMeterDelayMs());
        _volumeMeterTimer.Start();

        ApplyCurrentConnectionState();
        _ = RefreshSnapshotAsync(connectIfNeeded: ShouldTryAutoConnect());
    }

    private void OnRefreshTimerTick()
    {
        _ = RefreshSnapshotAsync(connectIfNeeded: ShouldTryAutoConnect());
    }

    private void OnVolumeMeterTimerTick()
    {
        if (AudioSources.Count == 0)
        {
            return;
        }

        var delayMs = ResolveVolumeMeterDelayMs();

        foreach (var meter in AudioSources)
        {
            var target = ReadRowTarget(meter.SourceName, meter.Muted, meter.Found, delayMs);
            if (target <= 0D && meter.Level <= 0D)
            {
                continue;
            }

            meter.ApplyLevel(target);
        }
    }

    // TODO: уровень присваивается напрямую и прыгает скачками – сглаживание WinForms-хоста (33 мс) не перенесено –
    //  вернуть через CompositionTarget.Rendering, когда пожалуются на дёрганые индикаторы

    private void OnObsEventReceived(object? sender, ObsWebSocketEventArgs evt)
    {
        if (string.Equals(evt.EventType, "InputVolumeMeters", StringComparison.Ordinal))
        {
            UpdateVolumeTargetsFromObsEvent(evt);
            return;
        }

        if (ObsMultistreamOutputsClient.TryParseVendorEvent(evt, out var vendorEvent) && vendorEvent is not null)
        {
            HandleSecondaryOutputVendorEvent(vendorEvent);
            return;
        }

        if (!IsDashboardEvent(evt.EventType))
        {
            return;
        }

        ScheduleRefreshFromObsEvent();
    }

    private void ScheduleRefreshFromObsEvent()
    {
        _uiDispatcher.Invoke(() => _ = RefreshSnapshotAsync(connectIfNeeded: false));
    }

    [RelayCommand]
    private Task RefreshAsync()
    {
        return RefreshSnapshotAsync(connectIfNeeded: true);
    }

    [RelayCommand]
    private async Task RefreshChatSourcesAsync()
    {
        var configuredCount = _settings.GetChatRefreshSourceNames().Count;

        try
        {
            using var cts = new CancellationTokenSource(SnapshotTimeout);
            var refreshed = await _obsIntegration.RefreshConfiguredChatSourcesAsync(_settings, cts.Token);

            if (configuredCount == 0)
            {
                ShowToast("Источники чата OBS не настроены", StatusSeverity.Warning);
                _logger.ObsChatSourcesNotConfigured();
            }
            else if (refreshed == 0)
            {
                ShowToast($"Ни один из {configuredCount} источников чата OBS не обновлён", StatusSeverity.Error);
                _logger.ObsChatSourcesAllRejected(configuredCount);
            }
            else if (refreshed < configuredCount)
            {
                ShowToast($"Обновлено источников чата OBS: {refreshed}/{configuredCount}", StatusSeverity.Warning);
            }
            else
            {
                ShowToast($"Обновлено источников чата OBS: {refreshed}", StatusSeverity.Success);
            }
        }
        catch (OperationCanceledException exception)
        {
            ShowToast("Таймаут обновления источников чата OBS", StatusSeverity.Error);
            _logger.ObsChatSourcesRefreshTimeout(exception);
        }
        catch (Exception exception)
        {
            ShowToast("Не удалось обновить источники чата OBS", StatusSeverity.Error);
            _logger.ObsChatSourcesRefreshFailed(exception);
        }
    }

    private void ShowToast(string text, StatusSeverity severity)
    {
        _toasts.ShowIfEnabled(_preferences, text, severity);
    }

    private void OnObsIntegrationSettingsChanged(ObsIntegrationSettingsChangedEvent evt)
    {
        _settings = evt.Settings;
        _lastAutoConnectAttempt = DateTimeOffset.MinValue;
        _volumeMeterTimer.Interval = TimeSpan.FromMilliseconds(ResolveVolumeMeterDelayMs());
        ClearRows();
        ApplyCurrentConnectionState();
        _ = RefreshSnapshotAsync(connectIfNeeded: ShouldTryAutoConnect());
    }

    private async Task RefreshSnapshotAsync(bool connectIfNeeded)
    {
        if (!TryBeginRefresh())
        {
            return;
        }

        using var cts = new CancellationTokenSource(SnapshotTimeout);
        Interlocked.Exchange(ref _refreshCts, cts)?.Dispose();

        try
        {
            var snapshot = await _obsIntegration.GetDashboardSnapshotAsync(_settings, connectIfNeeded, cts.Token);
            if (_disposed || _previewing)
            {
                return;
            }

            if (connectIfNeeded)
            {
                _autoConnectFailures = snapshot.IsConnected
                    ? 0
                    : Math.Min(_autoConnectFailures + 1, MaxAutoConnectBackoffSteps);
            }

            ApplySnapshot(snapshot);
            UpdateRefreshTimer(true, snapshot.IsConnected);
        }
        catch (OperationCanceledException)
        {
            if (_disposed || _previewing)
            {
                return;
            }

            ApplyUnavailableState("превышено время ожидания");
            UpdateRefreshTimer(true, false);
        }
        catch (Exception exception)
        {
            if (_disposed || _previewing)
            {
                return;
            }

            _logger.ObsTileRefreshFailed(exception);
            ApplyUnavailableState("не удалось получить данные");
            UpdateRefreshTimer(true, false);
        }
        finally
        {
            CompleteRefresh(cts);
        }
    }

    private bool TryBeginRefresh()
    {
        if (_previewing)
        {
            return false;
        }

        if (_refreshing || _disposed)
        {
            if (!_disposed)
            {
                _refreshQueued = true;
            }

            return false;
        }

        if (!_settings.Enabled)
        {
            ApplyDisabledState();
            UpdateRefreshTimer(false, false);
            return false;
        }

        _refreshing = true;
        return true;
    }

    private void CompleteRefresh(CancellationTokenSource cts)
    {
        Interlocked.CompareExchange(ref _refreshCts, null, cts);

        _refreshing = false;

        if (_refreshQueued && !_disposed)
        {
            _refreshQueued = false;
            _ = RefreshSnapshotAsync(connectIfNeeded: false);
        }
    }

    private void ApplyCurrentConnectionState()
    {
        if (!_settings.Enabled)
        {
            ApplyDisabledState();
            UpdateRefreshTimer(false, false);
            return;
        }

        ApplySnapshot(ObsDashboardSnapshot.Unavailable(_obsIntegration.CurrentStatus));
        UpdateRefreshTimer(true, _obsIntegration.CurrentStatus.IsConnected);
    }

    public void BeginPreview(ObsDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        _previewing = true;
        _refreshTimer.Stop();
        _volumeMeterTimer.Stop();

        ApplySnapshot(snapshot, configuredOrder: false);

        foreach (var meter in AudioSources)
        {
            var source = snapshot.AudioSources.FirstOrDefault(item =>
                string.Equals(item.Name, meter.SourceName, StringComparison.OrdinalIgnoreCase));

            if (source is { IsMuted: false })
            {
                meter.ApplyLevel(source.VolumeMultiplier ?? 0D);
            }
        }
    }

    public void EndPreview()
    {
        if (!_previewing)
        {
            return;
        }

        _previewing = false;
        _volumeMeterTimer.Start();

        ClearRows();
        ApplyCurrentConnectionState();
    }

    private void ApplySnapshot(ObsDashboardSnapshot snapshot, bool configuredOrder = true)
    {
        if (!snapshot.IsConnected)
        {
            ApplyUnavailableState(snapshot.Connection.ErrorMessage);
            return;
        }

        UpdateConnectionHeader("● OBS подключён", ConnectedStatus, "OBS WebSocket подключён");

        SceneName = ToDisplayValue(snapshot.CurrentSceneName);

        var streamHealth = ObsStreamHealth.Describe(snapshot.IsStreaming,
            snapshot.StreamCongestion,
            snapshot.StreamSkippedFrames,
            snapshot.StreamTotalFrames);

        StreamCard.ApplySnapshot(snapshot.IsStreaming,
            null,
            snapshot.StreamTimecode,
            null,
            streamHealth?.Text,
            streamHealth?.Severity);

        RecordCard.ApplySnapshot(snapshot.IsRecording,
            snapshot.IsRecordingPaused,
            snapshot.RecordTimecode,
            FormatRecordBytes(snapshot.IsRecording, snapshot.RecordBytes));

        ApplySecondaryOutputs(snapshot.SecondaryOutputs);
        ApplyAudioSources(snapshot.AudioSources, configuredOrder);
    }

    private void ApplySecondaryOutputs(IReadOnlyList<ObsSecondaryOutputSnapshot> secondaryOutputs)
    {
        if (secondaryOutputs.Count == SecondaryOutputs.Count
            && SecondaryOutputs.Select(item => item.OutputId)
                .SequenceEqual(secondaryOutputs.Select(item => item.OutputId), StringComparer.Ordinal))
        {
            for (var index = 0; index < secondaryOutputs.Count; index++)
            {
                SecondaryOutputs[index].ApplySnapshot(secondaryOutputs[index]);
            }

            SecondaryOutputsEmpty = SecondaryOutputs.Count == 0;
            return;
        }

        SecondaryOutputs.Clear();

        foreach (var output in secondaryOutputs)
        {
            SecondaryOutputs.Add(new(output, ToggleSecondaryOutputAsync));
        }

        SecondaryOutputsEmpty = SecondaryOutputs.Count == 0;
    }

    private async Task ToggleSecondaryOutputAsync(ObsSecondaryOutputViewModel row)
    {
        if (!row.CanControl || _disposed || _previewing)
        {
            return;
        }

        row.IsActionInFlight = true;
        var succeeded = false;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
            cts.CancelAfter(SnapshotTimeout);

            if (row.IsActive)
            {
                await _obsIntegration.StopSecondaryOutputAsync(row.Name, cts.Token);
            }
            else
            {
                await _obsIntegration.StartSecondaryOutputAsync(row.Name, cts.Token);
            }

            succeeded = true;
            ScheduleDelayedSecondaryRefresh();
        }
        catch (OperationCanceledException exception)
        {
            if (_disposed)
            {
                return;
            }

            _logger.ObsSecondaryOutputActionFailed(exception, row.Name);
            ShowToast("Превышено время ожидания OBS", StatusSeverity.Error);
        }
        catch (InvalidOperationException exception)
        {
            if (_disposed)
            {
                return;
            }

            _logger.ObsSecondaryOutputActionFailed(exception, row.Name);
            ShowToast(exception.Message, StatusSeverity.Warning);
        }
        catch (Exception exception)
        {
            if (_disposed)
            {
                return;
            }

            _logger.ObsSecondaryOutputActionFailed(exception, row.Name);
            ShowToast("Не удалось управлять дополнительным выходом", StatusSeverity.Error);
        }
        finally
        {
            if (!succeeded || _disposed)
            {
                row.IsActionInFlight = false;
            }
        }
    }

    private void HandleSecondaryOutputVendorEvent(ObsSecondaryOutputEvent vendorEvent)
    {
        _uiDispatcher.Invoke(() =>
        {
            if (_disposed || _previewing)
            {
                return;
            }

            var row = SecondaryOutputs.FirstOrDefault(item =>
                item.Vendor == vendorEvent.Vendor
                && (string.Equals(item.Name, vendorEvent.OutputName, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(item.OutputId, vendorEvent.OutputName, StringComparison.OrdinalIgnoreCase)))
                ?? SecondaryOutputs.FirstOrDefault(item =>
                    item.Vendor == ObsMultistreamVendor.ObsOutput
                    && (string.Equals(item.Name, vendorEvent.OutputName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(item.OutputId, vendorEvent.OutputName, StringComparison.OrdinalIgnoreCase)));

            row?.ApplyActiveState(vendorEvent.Kind == ObsSecondaryOutputEventKind.Started);

            if (vendorEvent.Kind == ObsSecondaryOutputEventKind.Stopped
                && vendorEvent.ErrorCode is not null and not 0
                && !string.IsNullOrWhiteSpace(vendorEvent.LastError))
            {
                ShowToast($"Выход «{vendorEvent.OutputName}» остановлен", StatusSeverity.Warning);
            }

            ScheduleDelayedSecondaryRefresh();
        });
    }

    private void OnSecondaryRefreshTimerTick()
    {
        _secondaryRefreshTimer.Stop();
        if (!_disposed && !_previewing)
        {
            _ = RefreshSnapshotAsync(connectIfNeeded: false);
        }
    }

    private void ScheduleDelayedSecondaryRefresh()
    {
        if (_disposed || _previewing)
        {
            return;
        }

        _secondaryRefreshTimer.Stop();
        _secondaryRefreshTimer.Start();
    }

    private void ApplyAudioSources(IReadOnlyList<ObsAudioSourceSnapshot> audioSources, bool configuredOrder = true)
    {
        IReadOnlyList<string> configured = configuredOrder ? _settings.GetDashboardSourceNames() : [];
        var orderedNames = configured.Count > 0
            ? configured.ToList()
            : audioSources.Select(source => source.Name).ToList();

        EnsureRows(orderedNames);

        foreach (var meter in AudioSources)
        {
            var snapshot = audioSources.FirstOrDefault(source =>
                string.Equals(source.Name, meter.SourceName, StringComparison.OrdinalIgnoreCase));

            if (snapshot is null)
            {
                meter.Found = false;
                meter.Muted = false;
                meter.ShowMissing(meter.SourceName);
                continue;
            }

            meter.Found = true;
            meter.Muted = snapshot.IsMuted;

            if (snapshot.IsMuted)
            {
                meter.ShowMuted(snapshot.Name);
            }
            else
            {
                meter.ShowActive(snapshot.Name, snapshot.VolumeDecibels);
            }
        }
    }

    private void EnsureRows(IReadOnlyList<string> orderedNames)
    {
        var currentNames = AudioSources.Select(meter => meter.SourceName);

        if (currentNames.SequenceEqual(orderedNames, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        AudioSources.Clear();

        foreach (var name in orderedNames)
        {
            AudioSources.Add(new(name));
        }

        AudioSourcesEmpty = AudioSources.Count == 0;

        lock (_volumeMeterLock)
        {
            _volumeTargets.Clear();
        }
    }

    private void ClearRows()
    {
        SecondaryOutputs.Clear();
        SecondaryOutputsEmpty = true;

        AudioSources.Clear();
        AudioSourcesEmpty = true;

        lock (_volumeMeterLock)
        {
            _volumeTargets.Clear();
        }
    }

    private void ApplyDisabledState()
    {
        UpdateConnectionHeader("○ OBS выкл.", "off", "OBS интеграция отключена в настройках");
        SceneName = "–";
        StreamCard.ApplyUnknown();
        RecordCard.ApplyUnknown();
        ClearRows();
    }

    private void ApplyUnavailableState(string? message)
    {
        UpdateConnectionHeader("● OBS нет",
            UnavailableStatus,
            string.IsNullOrWhiteSpace(message) ? "OBS WebSocket не подключён" : $"OBS WebSocket не подключён: {message}");

        SceneName = "–";
        StreamCard.ApplyUnknown();
        RecordCard.ApplyUnknown();
        ClearRows();
    }

    private void UpdateConnectionHeader(string text, string status, string toolTip)
    {
        ConnectionText = text;
        ConnectionStatus = status;
        ConnectionToolTip = toolTip;
    }

    private void UpdateVolumeTargetsFromObsEvent(ObsWebSocketEventArgs evt)
    {
        if (evt.EventData is not { } eventData
            || !eventData.TryGetProperty("inputs", out var inputs)
            || inputs.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var now = DateTimeOffset.Now;

        lock (_volumeMeterLock)
        {
            using var inputEnumerator = inputs.EnumerateArray();
            while (inputEnumerator.MoveNext())
            {
                var input = inputEnumerator.Current;
                var inputName = GetOptionalString(input, "inputName");
                if (string.IsNullOrWhiteSpace(inputName) || !TryGetInputLevel(input, out var level))
                {
                    continue;
                }

                _volumeTargets[inputName] = (level, now);
            }
        }
    }

    private double ReadRowTarget(string name, bool muted, bool found, int delayMs)
    {
        if (!found || muted || string.IsNullOrWhiteSpace(name))
        {
            return 0D;
        }

        lock (_volumeMeterLock)
        {
            if (!_volumeTargets.TryGetValue(name, out var target))
            {
                return 0D;
            }

            var staleAfter = TimeSpan.FromMilliseconds(delayMs * 3D);
            return DateTimeOffset.Now - target.UpdatedAt > staleAfter ? 0D : target.Level;
        }
    }

    private int ResolveVolumeMeterDelayMs()
    {
        return Math.Clamp(_settings.DashboardVolumeMeterDelayMs, MinVolumeMeterDelayMs, MaxVolumeMeterDelayMs);
    }

    private void UpdateRefreshTimer(bool enabled, bool connected)
    {
        _refreshTimer.Interval = TimeSpan.FromMilliseconds(connected ? ConnectedRefreshIntervalMs : DisconnectedRefreshIntervalMs);

        if (enabled)
        {
            if (!_refreshTimer.IsRunning)
            {
                _refreshTimer.Start();
            }

            return;
        }

        if (_refreshTimer.IsRunning)
        {
            _refreshTimer.Stop();
        }
    }

    private bool ShouldTryAutoConnect()
    {
        if (App.IsGallery)
        {
            return false;
        }

        if (!_settings.Enabled || !_settings.AutoConnect || _obsIntegration.CurrentStatus.IsConnected)
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        if (now - _lastAutoConnectAttempt < CurrentAutoConnectInterval())
        {
            return false;
        }

        _lastAutoConnectAttempt = now;
        return true;
    }

    private TimeSpan CurrentAutoConnectInterval()
    {
        var seconds = Math.Min(AutoConnectRetryInterval.TotalSeconds * Math.Pow(2, _autoConnectFailures),
            MaxAutoConnectIntervalSeconds);

        return TimeSpan.FromSeconds(seconds);
    }

    private static string ToDisplayValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "–" : value;
    }

    private static string? FormatRecordBytes(bool? active, long? bytes)
    {
        return ObsStreamHealth.FormatBytes(active, bytes);
    }

    private static string? GetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool IsDashboardEvent(string eventType)
    {
        return eventType is "CurrentProgramSceneChanged"
            or "StreamStateChanged"
            or "RecordStateChanged"
            or "InputMuteStateChanged"
            or "InputVolumeChanged"
            or "InputNameChanged"
            or "InputCreated"
            or "InputRemoved";
    }

    private static bool TryGetMaxNumber(JsonElement element, out double max)
    {
        max = double.NegativeInfinity;

        if (element.ValueKind == JsonValueKind.Number)
        {
            max = element.GetDouble();
            return true;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var found = false;
        using var childEnumerator = element.EnumerateArray();
        while (childEnumerator.MoveNext())
        {
            var child = childEnumerator.Current;
            if (!TryGetMaxNumber(child, out var childMax))
            {
                continue;
            }

            max = Math.Max(max, childMax);
            found = true;
        }

        return found;
    }

    private static bool TryGetInputLevel(JsonElement input, out double level)
    {
        level = 0;

        if (input.TryGetProperty("inputLevelsMul", out var levelsMul)
            && TryGetMaxNumber(levelsMul, out var maxMultiplier))
        {
            level = Math.Clamp(maxMultiplier, 0D, 1D);
            return true;
        }

        if (input.TryGetProperty("inputLevelsDb", out var levelsDb)
            && TryGetMaxNumber(levelsDb, out var maxDecibels))
        {
            level = NormalizeDecibelsToMeter(maxDecibels);
            return true;
        }

        return false;
    }

    private static double NormalizeDecibelsToMeter(double decibels)
    {
        return Math.Clamp((decibels + 60D) / 60D, 0D, 1D);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _refreshTimer.Stop();
        _volumeMeterTimer.Stop();
        _secondaryRefreshTimer.Stop();

        _obsClient.EventReceived -= OnObsEventReceived;

        _disposeCts.Cancel();
        _disposeCts.Dispose();
        Interlocked.Exchange(ref _refreshCts, null)?.Cancel();

        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        _subs.Clear();
    }
}
