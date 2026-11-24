using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed record YouTubeQuotaCategoryItem(string Label, int Units, string UnitsText);

public sealed partial class YouTubeQuotaStatusViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(5);

    private static readonly (YouTubeQuotaCategory Category, string Label)[] CategoryDisplayOrder =
    [
        (YouTubeQuotaCategory.ChatSend, "Отправка в чат"),
        (YouTubeQuotaCategory.StreamMonitoring, "Мониторинг эфира"),
        (YouTubeQuotaCategory.ChatRead, "Чтение чата (API)"),
        (YouTubeQuotaCategory.ChannelAndBroadcastInfo, "Проверка канала и метаданные"),
        (YouTubeQuotaCategory.Other, "Прочие запросы API"),
    ];

    private readonly IYouTubeQuotaGovernor? _governor;
    private readonly IYouTubeChatDiagnostics? _chatDiagnostics;
    private readonly IYouTubeMonitorDiagnostics? _monitorDiagnostics;
    private readonly TimeProvider _timeProvider;
    private readonly IUiTimer _refreshTimer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoUsageToday))]
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

    [ObservableProperty]
    private string _chatReadTransportText = string.Empty;

    [ObservableProperty]
    private string _monitoringTransportText = string.Empty;

    [ObservableProperty]
    private string _chatSendTransportText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCategoryItems))]
    [NotifyPropertyChangedFor(nameof(HasNoUsageToday))]
    private IReadOnlyList<YouTubeQuotaCategoryItem> _categoryItems = [];

    [ObservableProperty]
    private string _categorySummaryText = string.Empty;

    public YouTubeQuotaStatusViewModel(
        IYouTubeQuotaGovernor? governor,
        IUiDispatcher uiDispatcher,
        TimeProvider timeProvider,
        IYouTubeChatDiagnostics? chatDiagnostics = null,
        IYouTubeMonitorDiagnostics? monitorDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _governor = governor;
        _chatDiagnostics = chatDiagnostics;
        _monitorDiagnostics = monitorDiagnostics;
        _timeProvider = timeProvider;
        _refreshTimer = uiDispatcher.CreateTimer(AutoRefreshInterval, Refresh);

        Refresh();
    }

    public bool HasWarning => !string.IsNullOrEmpty(WarningText);

    public bool HasCategoryItems => CategoryItems.Count > 0;

    public bool HasNoUsageToday => HasData && CategoryItems.Count == 0;

    public string NoUsageTodayText => "За сегодня квота не расходовалась.";

    public void SetAutoRefresh(bool active)
    {
        if (active && (_governor is not null || _chatDiagnostics is not null || _monitorDiagnostics is not null))
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
        RefreshTransports();

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
            CategoryItems = [];
            CategorySummaryText = string.Empty;
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

        var items = BuildCategoryItems(status, committed, culture);
        CategoryItems = items;
        CategorySummaryText = items.Count == 0
            ? NoUsageTodayText
            : string.Join(", ", items.Select(static item => $"{item.Label}: {item.UnitsText}"));
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
    }

    private void RefreshTransports()
    {
        var chatStatus = _chatDiagnostics?.CaptureDiagnostics();
        var monitorStatus = _monitorDiagnostics?.CaptureDiagnostics();

        ChatReadTransportText = FormatChatReadTransport(chatStatus);
        MonitoringTransportText = FormatMonitoringTransport(monitorStatus);
        ChatSendTransportText = monitorStatus is { Enabled: false }
            ? $"YouTube Data API ({YouTubeQuotaCosts.ChatSendUnits} ед. за сообщение; сейчас YouTube отключён)"
            : $"YouTube Data API ({YouTubeQuotaCosts.ChatSendUnits} ед. за сообщение); при работе без ответов бота квота не тратится";
    }

    internal static string FormatChatReadTransport(YouTubeChatStatus? status)
    {
        if (status is null)
        {
            return "данные недоступны";
        }

        return status.Transport switch
        {
            YouTubeChatTransport.InnerTube => "InnerTube (без расхода квоты)",
            YouTubeChatTransport.GrpcStream => $"YouTube Data API (gRPC, ~{YouTubeQuotaCosts.ChatReadUnits} ед./запрос)",
            YouTubeChatTransport.RestPolling => $"YouTube Data API (REST, {YouTubeQuotaCosts.ChatReadUnits} ед./запрос)",
            _ => status.Issue switch
            {
                ChatReadIssue.MissingBotAuth => "не подключён – нужна авторизация бота",
                ChatReadIssue.WaitingForActiveBroadcast => "не подключён – ожидание активного эфира",
                ChatReadIssue.QuotaExhausted => "не подключён – квота исчерпана",
                ChatReadIssue.TransientError => "не подключён – ошибка чтения, повтор подключения",
                _ => "не подключён",
            },
        };
    }

    internal static string FormatMonitoringTransport(YouTubeMonitoringStatus? status)
    {
        if (status is null)
        {
            return "данные недоступны";
        }

        if (!status.Enabled)
        {
            return "не запущен (YouTube отключён)";
        }

        return status.Transport switch
        {
            YouTubeMonitoringTransport.InnerTubeDiscovery => "InnerTube (0 ед. квоты)",
            YouTubeMonitoringTransport.HybridInnerTubeAndDataApi => $"InnerTube (0 ед.) + Data API для метаданных ({YouTubeQuotaCosts.MonitoringPollUnits} ед./мин)",
            YouTubeMonitoringTransport.DataApi => $"YouTube Data API ({YouTubeQuotaCosts.MonitoringPollUnits} ед./запрос)",
            _ => status.Issue switch
            {
                YouTubeMonitorIssue.MissingBroadcasterAuth => "не запущен – нужна авторизация стримера",
                YouTubeMonitorIssue.MissingBotAuth => "не запущен – нужна авторизация бота",
                YouTubeMonitorIssue.QuotaExhausted => "приостановлен – квота исчерпана",
                YouTubeMonitorIssue.AccessUnverified => "не запущен – доступ аккаунта не подтверждён",
                YouTubeMonitorIssue.InvalidTargetChannel => "не запущен – канал YouTube не найден",
                _ => "не запущен",
            },
        };
    }

    private static IReadOnlyList<YouTubeQuotaCategoryItem> BuildCategoryItems(
        YouTubeQuotaStatus status,
        int committed,
        IFormatProvider culture)
    {
        if (committed <= 0)
        {
            return [];
        }

        var items = new List<YouTubeQuotaCategoryItem>(CategoryDisplayOrder.Length + 1);
        var accounted = 0;

        if (status.CategoryUnits is { Count: > 0 } categories)
        {
            foreach (var (category, label) in CategoryDisplayOrder)
            {
                if (categories.TryGetValue(category, out var units) && units > 0)
                {
                    accounted += units;
                    items.Add(new(label, units, string.Format(culture, "{0:N0} ед.", units)));
                }
            }
        }

        var remainder = committed - accounted;
        if (remainder > 0)
        {
            var remainderLabel = accounted == 0
                ? "Без детализации по операциям"
                : "Прочие и ранние запросы";
            items.Add(new(remainderLabel, remainder, string.Format(culture, "{0:N0} ед.", remainder)));
        }

        return items;
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
