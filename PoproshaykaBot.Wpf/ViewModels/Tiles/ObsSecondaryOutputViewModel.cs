using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Obs;
using System;
using System.Threading.Tasks;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public sealed partial class ObsSecondaryOutputViewModel : ObservableObject
{
    private readonly Func<ObsSecondaryOutputViewModel, Task> _toggleAction;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateSeverity))]
    [NotifyPropertyChangedFor(nameof(ActionButtonText))]
    [NotifyPropertyChangedFor(nameof(BytesText))]
    [NotifyPropertyChangedFor(nameof(HasBytes))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StateSeverity))]
    private bool _isReconnecting;

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
        Func<ObsSecondaryOutputViewModel, Task> toggleAction)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(toggleAction);

        Name = snapshot.Name;
        OutputId = snapshot.OutputId;
        Vendor = snapshot.Vendor;
        _toggleAction = toggleAction;

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

    public string StateText => (IsActive, IsReconnecting) switch
    {
        (true, true) => "Переподключение…",
        (true, false) => "В эфире",
        _ => "Не идёт",
    };

    public string? StateSeverity => (IsActive, IsReconnecting) switch
    {
        (true, true) => "Warning",
        (true, false) => "Success",
        _ => null,
    };

    public string ActionButtonText => IsActive ? "■ Стоп" : "▶ Старт";

    public void ApplySnapshot(ObsSecondaryOutputSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Vendor = snapshot.Vendor;
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
    }

    public void ApplyActiveState(bool active)
    {
        IsActive = active;
        IsActionInFlight = false;
        if (!active)
        {
            IsReconnecting = false;
            Timecode = null;
            Bytes = null;
            Health = null;
            HealthSeverity = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanToggle))]
    private Task ToggleAsync()
    {
        return _toggleAction(this);
    }

    private bool CanToggle()
    {
        return CanControl && !IsActionInFlight;
    }
}
