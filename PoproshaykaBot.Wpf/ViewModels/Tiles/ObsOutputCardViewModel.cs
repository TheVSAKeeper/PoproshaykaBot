using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Obs;

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

public sealed partial class ObsOutputCardViewModel : ObservableObject
{
    private static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(8);

    private readonly ObsIntegrationService _obsIntegration;
    private readonly ILogger _logger;

    private bool _actionInFlight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText))]
    [NotifyPropertyChangedFor(nameof(StatusSeverity))]
    [NotifyPropertyChangedFor(nameof(PrimaryButtonText))]
    [NotifyPropertyChangedFor(nameof(SecondaryButtonText))]
    [NotifyPropertyChangedFor(nameof(ChipText))]
    [NotifyPropertyChangedFor(nameof(ChipSeverityResolved))]
    private ObsOutputCardState _state = ObsOutputCardState.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTimecode))]
    private string? _timecode;

    [ObservableProperty]
    private string _meta = string.Empty;

    [ObservableProperty]
    private bool _metaIsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipText))]
    [NotifyPropertyChangedFor(nameof(ChipSeverityResolved))]
    private string? _chip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChipSeverityResolved))]
    private string? _chipSeverity;

    [ObservableProperty]
    private string _kicker = string.Empty;

    public ObsOutputCardViewModel(ObsOutputCardKind kind, ObsIntegrationService obsIntegration, ILogger logger)
    {
        Kind = kind;
        _obsIntegration = obsIntegration;
        _logger = logger;
        Kicker = kind switch
        {
            ObsOutputCardKind.Stream => "ЭФИР",
            ObsOutputCardKind.Record => "ЗАПИСЬ",
            _ => string.Empty,
        };
    }

    // TODO: добавить пульсацию точки/ореол и анимированную обводку карточки (Storyboard) для Active/Paused – пока статичная подсветка токенами State.*.
    public ObsOutputCardKind Kind { get; }

    public bool IsStreamMode => Kind == ObsOutputCardKind.Stream;

    public bool IsRecordMode => Kind == ObsOutputCardKind.Record;

    public bool HasTimecode => !string.IsNullOrWhiteSpace(Timecode);

    public string StateText => (Kind, State) switch
    {
        (_, ObsOutputCardState.Idle) => "Готов",
        (ObsOutputCardKind.Stream, ObsOutputCardState.Active) => "В ЭФИРЕ",
        (ObsOutputCardKind.Record, ObsOutputCardState.Active) => "ЗАПИСЬ",
        (_, ObsOutputCardState.Paused) => "ПАУЗА",
        (_, ObsOutputCardState.Error) => "Ошибка",
        _ => "—",
    };

    public string? StatusSeverity => State switch
    {
        ObsOutputCardState.Active => "Success",
        ObsOutputCardState.Paused => "Warning",
        ObsOutputCardState.Error => "Error",
        _ => null,
    };

    public string PrimaryButtonText => (Kind, State) switch
    {
        (ObsOutputCardKind.Stream, ObsOutputCardState.Idle) => "▶ Старт",
        (ObsOutputCardKind.Stream, ObsOutputCardState.Active) => "■ Стоп",
        (ObsOutputCardKind.Record, ObsOutputCardState.Idle) => "● Старт",
        (ObsOutputCardKind.Record, ObsOutputCardState.Active) => "■ Стоп",
        (ObsOutputCardKind.Record, ObsOutputCardState.Paused) => "■ Стоп",
        _ => "—",
    };

    public string SecondaryButtonText => State switch
    {
        ObsOutputCardState.Active => "⏸ Пауза",
        ObsOutputCardState.Paused => "▶ Дальше",
        _ => "⏸ Пауза",
    };

    public string ChipText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Chip))
            {
                return Chip;
            }

            return State switch
            {
                ObsOutputCardState.Active => "в эфире",
                ObsOutputCardState.Idle => "офлайн",
                ObsOutputCardState.Error => "OBS WS",
                _ => "—",
            };
        }
    }

    public string? ChipSeverityResolved
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Chip))
            {
                return ChipSeverity;
            }

            return State switch
            {
                ObsOutputCardState.Active => "Success",
                ObsOutputCardState.Error => "Error",
                _ => null,
            };
        }
    }

    public void ApplySnapshot(
        bool? active,
        bool? paused,
        string? timecode,
        string? meta,
        string? chip = null,
        string? chipSeverity = null)
    {
        State = ResolveState(active, paused);
        Timecode = string.IsNullOrWhiteSpace(timecode) ? null : timecode;
        MetaIsError = false;
        Meta = meta ?? string.Empty;
        Chip = chip;
        ChipSeverity = chipSeverity;
    }

    public void ApplyUnavailable(string? errorMessage)
    {
        State = ObsOutputCardState.Error;
        Timecode = null;
        MetaIsError = false;
        Meta = string.IsNullOrWhiteSpace(errorMessage) ? "OBS недоступен" : errorMessage;
        Chip = null;
        ChipSeverity = null;
    }

    public void ApplyUnknown()
    {
        State = ObsOutputCardState.Unknown;
        Timecode = null;
        MetaIsError = false;
        Meta = string.Empty;
        Chip = null;
        ChipSeverity = null;
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
        return action is null ? Task.CompletedTask : InvokeObsActionAsync(action);
    }

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
            _logger.LogWarning(exception, "Не удалось выполнить OBS-действие для карточки {Kind}", Kind);
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
        RaiseCommandStates();
    }
}
