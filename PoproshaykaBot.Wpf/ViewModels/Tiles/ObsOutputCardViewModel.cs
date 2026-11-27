using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Tiles;

public enum ObsOutputCardKind
{
    None = 0,
    Stream = 1,
    Record = 2,
}

public enum ObsOutputCardState
{
    None = 0,
    Unknown = 1,
    Idle = 2,
    Active = 3,
    Paused = 4,
    Error = 5,
}

public sealed partial class ObsOutputCardViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(8);

    private readonly ObsIntegrationService _obsIntegration;
    private readonly ILogger _logger;
    private readonly InlineConfirmationState _stopConfirmation;

    private bool _actionInFlight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(ShowsSecondaryButton))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonToolTip))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonAutomationName))]
    [NotifyPropertyChangedFor(nameof(SecondaryButtonText))]
    private ObsOutputCardState _state = ObsOutputCardState.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimecode))]
    private string? _timecode;

    [ObservableProperty]
    private string _meta = string.Empty;

    [ObservableProperty]
    private bool _metaIsError;

    [ObservableProperty]
    private string? _health;

    [ObservableProperty]
    private string? _healthSeverity;

    public ObsOutputCardViewModel(
        ObsOutputCardKind kind,
        ObsIntegrationService obsIntegration,
        ILogger logger,
        IUiDispatcher? uiDispatcher = null,
        TimeProvider? timeProvider = null)
    {
        Kind = kind;
        _obsIntegration = obsIntegration;
        _logger = logger;
        _stopConfirmation = new(uiDispatcher, OnStopConfirmationChanged, timeProvider: timeProvider);
    }

    // TODO: Active/Paused подсвечены статично токенами State.*, без пульсации точки и анимированной обводки –
    //  заводить Storyboard, когда идущую запись перестанут замечать по статичной подсветке
    public ObsOutputCardKind Kind { get; }

    public bool IsRecordMode => Kind == ObsOutputCardKind.Record;

    public bool HasTimecode => !string.IsNullOrWhiteSpace(Timecode);

    public bool IsRunning => State is ObsOutputCardState.Active or ObsOutputCardState.Paused;

    public bool IsStopAction => (Kind, State) is (ObsOutputCardKind.Stream, ObsOutputCardState.Active)
        or (ObsOutputCardKind.Record, ObsOutputCardState.Active or ObsOutputCardState.Paused);

    public bool IsStopConfirmationArmed => _stopConfirmation.IsArmed && IsStopAction;

    public bool ShowsSecondaryButton => IsRecordMode && IsRunning;

    public string Title => (Kind, State) switch
    {
        (ObsOutputCardKind.Stream, ObsOutputCardState.Active) => "В эфире",
        (ObsOutputCardKind.Stream, ObsOutputCardState.Idle) => "Эфир не идёт",
        (ObsOutputCardKind.Stream, ObsOutputCardState.Error) => "Эфир: ошибка",
        (ObsOutputCardKind.Stream, _) => "Эфир",
        (ObsOutputCardKind.Record, ObsOutputCardState.Active) => "Идёт запись",
        (ObsOutputCardKind.Record, ObsOutputCardState.Paused) => "Запись на паузе",
        (ObsOutputCardKind.Record, ObsOutputCardState.Idle) => "Запись не идёт",
        (ObsOutputCardKind.Record, ObsOutputCardState.Error) => "Запись: ошибка",
        (ObsOutputCardKind.Record, _) => "Запись",
        _ => "–",
    };

    public string PrimaryButtonText => IsStopConfirmationArmed
        ? "Точно стоп?"
        : (Kind, State) switch
        {
            (ObsOutputCardKind.Stream, ObsOutputCardState.Idle) => "▶ Старт",
            (ObsOutputCardKind.Stream, ObsOutputCardState.Active) => "■ Стоп",
            (ObsOutputCardKind.Record, ObsOutputCardState.Idle) => "● Старт",
            (ObsOutputCardKind.Record, ObsOutputCardState.Active) => "■ Стоп",
            (ObsOutputCardKind.Record, ObsOutputCardState.Paused) => "■ Стоп",
            _ => "–",
        };

    public string PrimaryButtonAutomationName => IsStopConfirmationArmed
        ? Kind == ObsOutputCardKind.Stream ? "Подтвердить остановку эфира" : "Подтвердить остановку записи"
        : PrimaryButtonText;

    public string? PrimaryButtonToolTip => IsStopConfirmationArmed
        ? "Нажмите ещё раз в течение 3 секунд, чтобы остановить, или Esc для отмены"
        : IsStopAction
            ? "Нажмите для подтверждения остановки"
            : null;

    public string SecondaryButtonText => State switch
    {
        ObsOutputCardState.Active => "⏸ Пауза",
        ObsOutputCardState.Paused => "▶ Дальше",
        _ => "⏸ Пауза",
    };

    public void ApplySnapshot(
        bool? active,
        bool? paused,
        string? timecode,
        string? meta,
        string? health = null,
        string? healthSeverity = null)
    {
        State = ResolveState(active, paused);
        Timecode = string.IsNullOrWhiteSpace(timecode) ? null : timecode;
        MetaIsError = false;
        Meta = meta ?? string.Empty;
        Health = health;
        HealthSeverity = healthSeverity;
    }

    public void ApplyUnknown()
    {
        State = ObsOutputCardState.Unknown;
        Timecode = null;
        MetaIsError = false;
        Meta = string.Empty;
        Health = null;
        HealthSeverity = null;
    }

    private static ObsOutputCardState ResolveState(bool? active, bool? paused)
    {
        return (active, paused) switch
        {
            (true, true) => ObsOutputCardState.Paused,
            (true, _) => ObsOutputCardState.Active,
            (false, _) => ObsOutputCardState.Idle,
            _ => ObsOutputCardState.Unknown,
        };
    }

    private Func<CancellationToken, Task>? ResolvePrimaryAction()
    {
        return (Kind, State) switch
        {
            (ObsOutputCardKind.Stream, ObsOutputCardState.Idle) => _obsIntegration.StartStreamAsync,
            (ObsOutputCardKind.Stream, ObsOutputCardState.Active) => _obsIntegration.StopStreamAsync,
            (ObsOutputCardKind.Record, ObsOutputCardState.Idle) => _obsIntegration.StartRecordAsync,
            (ObsOutputCardKind.Record, ObsOutputCardState.Active) => _obsIntegration.StopRecordAsync,
            (ObsOutputCardKind.Record, ObsOutputCardState.Paused) => _obsIntegration.StopRecordAsync,
            _ => null,
        };
    }

    [RelayCommand(CanExecute = nameof(CanInvokePrimary))]
    private Task InvokePrimaryAsync()
    {
        var action = ResolvePrimaryAction();
        if (action is null)
        {
            return Task.CompletedTask;
        }

        if (IsStopAction && !_stopConfirmation.TryConfirmOrArm())
        {
            return Task.CompletedTask;
        }

        return InvokeObsActionAsync(action);
    }

    [RelayCommand]
    private void CancelStopConfirmation() => _stopConfirmation.Disarm();

    private bool CanInvokePrimary()
    {
        return !_actionInFlight && ResolvePrimaryAction() is not null;
    }

    [RelayCommand(CanExecute = nameof(CanInvokeSecondary))]
    private Task InvokeSecondaryAsync()
    {
        return InvokeObsActionAsync(token => _obsIntegration.ToggleRecordPauseAsync(token));
    }

    private bool CanInvokeSecondary()
    {
        return !_actionInFlight
               && IsRecordMode
               && State is ObsOutputCardState.Active or ObsOutputCardState.Paused;
    }

    private async Task InvokeObsActionAsync(Func<CancellationToken, Task> action)
    {
        _actionInFlight = true;
        RaiseCommandStates();

        try
        {
            using var cts = new CancellationTokenSource(ActionTimeout);
            await action(cts.Token);
        }
        catch (OperationCanceledException)
        {
            ShowMetaError("превышено время ожидания");
        }
        catch (Exception exception)
        {
            _logger.ObsOutputActionFailed(exception, Kind);
            ShowMetaError("не удалось выполнить");
        }
        finally
        {
            _actionInFlight = false;
            RaiseCommandStates();
        }
    }

    private void ShowMetaError(string message)
    {
        MetaIsError = true;
        Meta = $"⚠ {message}";
    }

    private void RaiseCommandStates()
    {
        InvokePrimaryCommand.NotifyCanExecuteChanged();
        InvokeSecondaryCommand.NotifyCanExecuteChanged();
    }

    partial void OnStateChanged(ObsOutputCardState value)
    {
        if (!IsStopAction)
        {
            _stopConfirmation.Disarm();
        }

        OnPropertyChanged(nameof(IsStopAction));
        OnPropertyChanged(nameof(IsStopConfirmationArmed));
        RaiseCommandStates();
    }

    private void OnStopConfirmationChanged()
    {
        OnPropertyChanged(nameof(IsStopConfirmationArmed));
        OnPropertyChanged(nameof(PrimaryButtonText));
        OnPropertyChanged(nameof(PrimaryButtonToolTip));
        OnPropertyChanged(nameof(PrimaryButtonAutomationName));
    }

    public void Dispose()
    {
        _stopConfirmation.Dispose();
    }
}
