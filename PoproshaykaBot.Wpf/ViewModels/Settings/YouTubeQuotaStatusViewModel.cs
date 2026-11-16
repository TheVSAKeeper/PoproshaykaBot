using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class YouTubeQuotaStatusViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(5);

    private readonly IYouTubeQuotaGovernor? _governor;
    private readonly TimeProvider _timeProvider;
    private readonly IUiTimer _refreshTimer;

    [ObservableProperty]
    private bool _hasData;

    [ObservableProperty]
    private string _usageText = string.Empty;

    [ObservableProperty]
    private string _breakdownText = string.Empty;

    [ObservableProperty]
    private double _fillPercent;

    [ObservableProperty]
    private StatusSeverity _statusSeverity = StatusSeverity.None;

    [ObservableProperty]
    private string _nextResetText = string.Empty;

    [ObservableProperty]
    private string _reliabilityText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarning))]
    private string _warningText = string.Empty;

    public YouTubeQuotaStatusViewModel(
        IYouTubeQuotaGovernor? governor,
        IUiDispatcher uiDispatcher,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _governor = governor;
        _timeProvider = timeProvider;
        _refreshTimer = uiDispatcher.CreateTimer(AutoRefreshInterval, Refresh);

        Refresh();
    }

    public bool HasWarning => !string.IsNullOrEmpty(WarningText);

    public void SetAutoRefresh(bool active)
    {
        if (active && _governor is not null)
        {
            Refresh();
            _refreshTimer.Start();
            return;
        }

        _refreshTimer.Stop();
    }

    [RelayCommand]
    public void Refresh()
    {
        if (_governor is null)
        {
            HasData = false;
            UsageText = "Данные о квоте недоступны.";
            BreakdownText = string.Empty;
            FillPercent = 0;
            StatusSeverity = StatusSeverity.None;
            NextResetText = string.Empty;
            ReliabilityText = string.Empty;
            WarningText = string.Empty;
            return;
        }

        var status = _governor.CaptureDiagnostics();
        var culture = UiCulture.Russian;
        var budget = Math.Max(0, status.DailyBudget);
        var committed = Math.Max(0, status.CommittedUnits);
        var percent = budget > 0
            ? Math.Clamp(committed * 100.0 / budget, 0, 100)
            : (status.ExhaustedByServer ? 100 : 0);

        HasData = true;
        UsageText = string.Format(
            culture,
            "{0:N0} из {1:N0} ед.",
            committed,
            budget);

        var breakdown = string.Format(
            culture,
            "бот: {0:N0}, стример: {1:N0}",
            Math.Max(0, status.BotCommittedUnits),
            Math.Max(0, status.BroadcasterCommittedUnits));
        BreakdownText = status.ReservedUnits > 0
            ? string.Format(culture, "{0}, в резерве: {1:N0}", breakdown, status.ReservedUnits)
            : breakdown;

        FillPercent = percent;
        StatusSeverity = ResolveSeverity(status, percent);

        var localReset = TimeZoneInfo.ConvertTime(status.NextResetAt, _timeProvider.LocalTimeZone);
        NextResetText = string.Format(
            culture,
            "Сброс {0:dd.MM HH:mm} (00:00 тихоокеанского времени)",
            localReset);

        ReliabilityText = status.Reliability switch
        {
            YouTubeQuotaReliability.Reliable => "Учёт квоты: подтверждён локальным файлом",
            YouTubeQuotaReliability.CorruptFile => "Учёт квоты: файл повреждён, расчёт текущего запуска",
            _ => "Учёт квоты: оценочный учёт текущего запуска",
        };

        WarningText = BuildWarning(status);
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }

    private static StatusSeverity ResolveSeverity(YouTubeQuotaStatus status, double fillPercent)
    {
        if (status.ExhaustedByServer || status.Tier1RemainingUnits <= 0)
        {
            return StatusSeverity.Error;
        }

        return fillPercent >= 75.0
            ? StatusSeverity.Warning
            : StatusSeverity.Success;
    }

    private static string BuildWarning(YouTubeQuotaStatus status)
    {
        if (status.ExhaustedByServer && !status.CanWriteFile)
        {
            return "Сервер YouTube сообщил об исчерпании квоты; файл квоты недоступен для записи.";
        }

        if (status.ExhaustedByServer)
        {
            return "Сервер YouTube сообщил об исчерпании дневной квоты.";
        }

        return !status.CanWriteFile
            ? "Нет записи в файл квоты – счётчик не сохранится после перезапуска."
            : string.Empty;
    }
}
