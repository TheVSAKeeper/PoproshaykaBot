using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Wpf.Infrastructure;
using System;
using System.Threading.Tasks;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class ObsSecondaryOutputViewModel : ObservableObject, IDisposable
{
    private readonly Func<ObsSecondaryOutputViewModel, Task> _toggleAction;
    private readonly InlineConfirmationState _stopConfirmation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateSeverity))]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    [NotifyPropertyChangedFor(nameof(ActionButtonToolTip))]
    [NotifyPropertyChangedFor(nameof(ActionButtonAutomationName))]
    [NotifyPropertyChangedFor(nameof(BytesText))]
    [NotifyPropertyChangedFor(nameof(HasBytes))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateSeverity))]
    private bool _isReconnecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateSeverity))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool _isDisconnected;

    [ObservableProperty]
    private bool _isVertical;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnly))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool _canControl;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleCommand))]
    private bool _isActionInFlight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimecode))]
    private string? _timecode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BytesText))]
    [NotifyPropertyChangedFor(nameof(HasBytes))]
    private long? _bytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHealth))]
    private string? _health;

    [ObservableProperty]
    private string? _healthSeverity;

    public ObsSecondaryOutputViewModel(
        ObsSecondaryOutputSnapshot snapshot,
        Func<ObsSecondaryOutputViewModel, Task> toggleAction,
        IUiDispatcher? uiDispatcher = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(toggleAction);

        Name = snapshot.Name;
        OutputId = snapshot.OutputId;
        Vendor = snapshot.Vendor;
        _toggleAction = toggleAction;
        _stopConfirmation = new(uiDispatcher, OnStopConfirmationChanged);

        ApplySnapshot(snapshot);
    }

    public string Name { get; }

    public string OutputId { get; }

    public ObsMultistreamVendor Vendor { get; private set; }

    public bool IsReadOnly => !CanControl;

    public bool HasTimecode => !string.IsNullOrWhiteSpace(Timecode);

    public string? BytesText => ObsStreamHealth.FormatBytes(IsActive, Bytes);

    public bool HasBytes => !string.IsNullOrWhiteSpace(BytesText);

    public bool HasHealth => !string.IsNullOrWhiteSpace(Health);

    public bool IsStopConfirmationArmed => _stopConfirmation.IsArmed && IsActive;

    public string StateText => (IsActive, IsReconnecting || IsDisconnected) switch
    {
        (true, true) => "Переподключение…",
        (true, false) => "В эфире",
        (false, true) => "Переподключение…",
        _ => "Не идёт",
    };

    public string? StateSeverity => (IsActive, IsReconnecting || IsDisconnected) switch
    {
        (true, true) => "Warning",
        (true, false) => "Success",
        (false, true) => "Warning",
        _ => null,
    };

    public string ActionButtonText => IsStopConfirmationArmed
        ? "Точно стоп?"
        : IsActive ? "■ Стоп" : "▶ Старт";

    public string? ActionButtonToolTip => IsStopConfirmationArmed
        ? "Нажмите ещё раз в течение 3 секунд, чтобы остановить, или Esc для отмены"
        : IsActive
            ? "Нажмите для подтверждения остановки доп. выхода"
            : null;

    public string ActionButtonAutomationName => IsStopConfirmationArmed
        ? $"Подтвердить остановку {Name}"
        : ActionButtonText;

    public void ApplySnapshot(ObsSecondaryOutputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Vendor = snapshot.Vendor;
        IsDisconnected = false;
        IsActive = snapshot.IsActive;
        IsReconnecting = snapshot.IsReconnecting;
        IsVertical = snapshot.IsVertical;
        CanControl = snapshot.CanControl;
        Timecode = snapshot.IsActive && !string.IsNullOrWhiteSpace(snapshot.Timecode)
            ? snapshot.Timecode
            : null;
        Bytes = snapshot.IsActive ? snapshot.Bytes : null;

        var health = ObsStreamHealth.Describe(
            snapshot.IsActive,
            snapshot.Congestion,
            snapshot.SkippedFrames,
            snapshot.TotalFrames);

        Health = health?.Text;
        HealthSeverity = health?.Severity;
        IsActionInFlight = false;
        if (!IsActive)
        {
            _stopConfirmation.Disarm();
        }
    }

    public void ApplyActiveState(bool active)
    {
        IsDisconnected = false;
        IsActive = active;
        IsActionInFlight = false;
        if (!active)
        {
            _stopConfirmation.Disarm();
            IsReconnecting = false;
            Timecode = null;
            Bytes = null;
            Health = null;
            HealthSeverity = null;
        }
    }

    internal void ApplyDisconnected()
    {
        IsDisconnected = true;
        IsActionInFlight = false;
        _stopConfirmation.Disarm();
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private Task ToggleAsync()
    {
        if (IsActive && !_stopConfirmation.TryConfirmOrArm())
        {
            return Task.CompletedTask;
        }

        return _toggleAction(this);
    }

    [RelayCommand]
    private void CancelStopConfirmation() => _stopConfirmation.Disarm();

    private bool CanToggle()
    {
        return CanControl && !IsActionInFlight && !IsDisconnected;
    }

    private void OnStopConfirmationChanged()
    {
        OnPropertyChanged(nameof(IsStopConfirmationArmed));
        OnPropertyChanged(nameof(ActionButtonText));
        OnPropertyChanged(nameof(ActionButtonToolTip));
        OnPropertyChanged(nameof(ActionButtonAutomationName));
    }

    public void Dispose()
    {
        _stopConfirmation.Dispose();
    }
}
