using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Statistics;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Collections.ObjectModel;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class StreamHistoryPageViewModel : ObservableObject, IPageHeader, IDisposable
{
    public const int TrendLength = 40;
    public const double FlatDeltaPercent = 5;

    private const string Placeholder = "–";

    private static readonly PointTerm SessionTerm = new()
    {
        Singular = "стрим",
        Few = "стрима",
        Many = "стримов",
    };

    private readonly StreamSessionHistoryStore _historyStore;
    private readonly List<IDisposable> _subs = [];
    private readonly ObservableCollection<StreamSessionRowViewModel> _sessions = [];

    private double _averageMessages;
    private double _averageChatters;
    private double _averagePeakViewers;
    private double _averageViewers;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private StreamSessionRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _totalSessionsText = "0";

    [ObservableProperty]
    private string _totalSessionsLabel = "стримов";

    [ObservableProperty]
    private string _totalAirTimeText = Placeholder;

    [ObservableProperty]
    private string _lastStreamText = Placeholder;

    [ObservableProperty]
    private string _detailTitle = Placeholder;

    [ObservableProperty]
    private bool _isDetailTitleMissing;

    [ObservableProperty]
    private string _detailGame = Placeholder;

    [ObservableProperty]
    private string _detailPeriodText = Placeholder;

    [ObservableProperty]
    private string _detailDuration = Placeholder;

    [ObservableProperty]
    private string _detailMessages = Placeholder;

    [ObservableProperty]
    private string _detailChatters = Placeholder;

    [ObservableProperty]
    private string _detailPeakViewers = Placeholder;

    [ObservableProperty]
    private string _detailAverageViewers = Placeholder;

    [ObservableProperty]
    private string _detailMessagesDelta = string.Empty;

    [ObservableProperty]
    private TrendTone _detailMessagesDeltaTone;

    [ObservableProperty]
    private string _detailChattersDelta = string.Empty;

    [ObservableProperty]
    private TrendTone _detailChattersDeltaTone;

    [ObservableProperty]
    private string _detailPeakViewersDelta = string.Empty;

    [ObservableProperty]
    private TrendTone _detailPeakViewersDeltaTone;

    [ObservableProperty]
    private string _detailAverageViewersDelta = string.Empty;

    [ObservableProperty]
    private TrendTone _detailAverageViewersDeltaTone;

    public StreamHistoryPageViewModel(StreamSessionHistoryStore historyStore, IEventBus eventBus)
    {
        _historyStore = historyStore;

        _subs.Add(eventBus.SubscribeOnUi<StreamSessionCompleted>(_ => RefreshCommand.Execute(null)));

        Refresh();
    }

    public string PageTitle => "История стримов";
    public string? PageDescription => "Сессии стримов, их сегменты и чаттеры";

    public ObservableCollection<StreamSessionRowViewModel> Sessions => _sessions;
    public ObservableCollection<StreamTrendBarViewModel> Trend { get; } = [];
    public ObservableCollection<StreamSessionSegmentRowViewModel> Segments { get; } = [];
    public ObservableCollection<StreamSessionChatterRowViewModel> Chatters { get; } = [];

    public bool HasSelection => SelectedRow is not null;

    public bool HasSessions => _sessions.Count > 0;
    public bool HasTrend => Trend.Count > 1;
    public bool HasSegments => Segments.Count > 0;
    public bool HasSegmentTimeline => Segments.Count > 1;
    public bool HasChatters => Chatters.Count > 0;

    public string TrendCaption => string.Create(
        UiCulture.Russian,
        $"Пик зрителей, последние {Trend.Count:N0} {SessionTerm.ForCount(Trend.Count)}");

    public string SegmentsHeading => string.Create(UiCulture.Russian, $"Сегменты ({Segments.Count:N0})");
    public string ChattersHeading => string.Create(UiCulture.Russian, $"Чаттеры ({Chatters.Count:N0})");

    public static string FormatDateTime(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", UiCulture.Russian);
    }

    public static string FormatDuration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return string.Create(UiCulture.Russian, $"{(int)span.TotalDays} д {span.Hours} ч {span.Minutes} мин");
        }

        if (span.TotalHours >= 1)
        {
            return string.Create(UiCulture.Russian, $"{span.Hours} ч {span.Minutes} мин");
        }

        if (span.TotalMinutes >= 1)
        {
            return string.Create(UiCulture.Russian, $"{span.Minutes} мин");
        }

        return string.Create(UiCulture.Russian, $"{span.Seconds} с");
    }

    public static string FormatAirTime(TimeSpan span)
    {
        return string.Create(UiCulture.Russian, $"{(int)span.TotalHours:N0} ч {span.Minutes} мин");
    }

    public static string FormatNumber(long value) => value.ToString("N0", UiCulture.Russian);

    public static string FormatPeriod(DateTimeOffset started, DateTimeOffset ended)
    {
        var from = started.ToLocalTime();
        var to = ended.ToLocalTime();

        return from.Date == to.Date
            ? string.Create(UiCulture.Russian, $"{from:dd.MM.yyyy}, {from:HH:mm} – {to:HH:mm}")
            : string.Create(UiCulture.Russian, $"{from:dd.MM.yyyy}, {from:HH:mm} – {to:dd.MM.yyyy}, {to:HH:mm}");
    }

    public static (string Text, TrendTone Tone) DescribeDelta(double value, double average, int sessionCount)
    {
        if (sessionCount < 2 || average <= 0)
        {
            return (string.Empty, TrendTone.None);
        }

        var percent = (value - average) / average * 100;

        if (Math.Abs(percent) < FlatDeltaPercent)
        {
            return ("как обычно", TrendTone.Flat);
        }

        var rounded = (int)Math.Round(percent, MidpointRounding.AwayFromZero);

        return rounded >= 0
            ? (string.Create(UiCulture.Russian, $"+{rounded:N0} % к обычному"), TrendTone.Up)
            : (string.Create(UiCulture.Russian, $"−{-rounded:N0} % к обычному"), TrendTone.Down);
    }

    public void Dispose()
    {
        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        _subs.Clear();
    }

    partial void OnSelectedRowChanged(StreamSessionRowViewModel? value)
    {
        Segments.Clear();
        Chatters.Clear();

        if (value?.Source is { } session)
        {
            FillSegments(session);
            FillChatters(session);
        }

        foreach (var bar in Trend)
        {
            bar.IsSelected = value is not null && ReferenceEquals(bar.Row, value);
        }

        UpdateDetails(value);

        OnPropertyChanged(nameof(HasSegments));
        OnPropertyChanged(nameof(HasSegmentTimeline));
        OnPropertyChanged(nameof(HasChatters));
        OnPropertyChanged(nameof(SegmentsHeading));
        OnPropertyChanged(nameof(ChattersHeading));
    }

    public bool TrySelectAt(int index)
    {
        if (index < 0 || index >= _sessions.Count)
        {
            return false;
        }

        SelectedRow = _sessions[index];

        return true;
    }

    [RelayCommand]
    private void SelectSession(StreamSessionRowViewModel? row)
    {
        if (row is not null)
        {
            SelectedRow = row;
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        var history = _historyStore.Load();
        var selectedId = SelectedRow?.Source.Id;

        _sessions.Clear();

        foreach (var record in history.Sessions.OrderByDescending(record => record.StartedAt))
        {
            _sessions.Add(new(record));
        }

        UpdateAverages();
        UpdateSummary();
        BuildTrend();
        OnPropertyChanged(nameof(HasSessions));

        SelectedRow = selectedId.HasValue
            ? _sessions.FirstOrDefault(row => row.Source.Id == selectedId)
            : null;
    }

    private void FillSegments(StreamSessionRecord session)
    {
        var longest = session.Segments.Aggregate(TimeSpan.Zero, (max, segment) => segment.Duration > max ? segment.Duration : max);
        var index = 0;

        foreach (var segment in session.Segments)
        {
            var share = longest > TimeSpan.Zero ? segment.Duration / longest : 1;

            Segments.Add(new(segment, share, index % 2 == 1));
            index++;
        }
    }

    private void FillChatters(StreamSessionRecord session)
    {
        var ordered = session.Chatters
            .OrderByDescending(chatter => chatter.MessageCount)
            .ThenBy(chatter => chatter.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var leader = ordered.Count > 0 ? ordered[0].MessageCount : 0;

        for (var index = 0; index < ordered.Count; index++)
        {
            Chatters.Add(new(ordered[index], index + 1, leader));
        }
    }

    private void UpdateDetails(StreamSessionRowViewModel? row)
    {
        if (row is null)
        {
            DetailTitle = Placeholder;
            IsDetailTitleMissing = false;
            DetailGame = Placeholder;
            DetailPeriodText = Placeholder;
            DetailDuration = Placeholder;
            DetailMessages = Placeholder;
            DetailChatters = Placeholder;
            DetailPeakViewers = Placeholder;
            DetailAverageViewers = Placeholder;
            DetailMessagesDelta = string.Empty;
            DetailChattersDelta = string.Empty;
            DetailPeakViewersDelta = string.Empty;
            DetailAverageViewersDelta = string.Empty;
            DetailMessagesDeltaTone = TrendTone.None;
            DetailChattersDeltaTone = TrendTone.None;
            DetailPeakViewersDeltaTone = TrendTone.None;
            DetailAverageViewersDeltaTone = TrendTone.None;
            return;
        }

        DetailTitle = row.TitleFormatted;
        IsDetailTitleMissing = !row.HasTitle;
        DetailGame = row.Source.Game is { Length: > 0 } game ? game : Placeholder;
        DetailPeriodText = FormatPeriod(row.StartedAt, row.Source.EndedAt);
        DetailDuration = row.DurationFormatted;
        DetailMessages = row.MessageCountFormatted;
        DetailChatters = row.ChatterCountFormatted;
        DetailPeakViewers = row.PeakViewersFormatted;
        DetailAverageViewers = row.AverageViewersFormatted;

        (DetailMessagesDelta, DetailMessagesDeltaTone) = DescribeDelta(row.MessageCount, _averageMessages, _sessions.Count);
        (DetailChattersDelta, DetailChattersDeltaTone) = DescribeDelta(row.ChatterCount, _averageChatters, _sessions.Count);
        (DetailPeakViewersDelta, DetailPeakViewersDeltaTone) = DescribeDelta(row.PeakViewers, _averagePeakViewers, _sessions.Count);
        (DetailAverageViewersDelta, DetailAverageViewersDeltaTone) = DescribeDelta(row.AverageViewers, _averageViewers, _sessions.Count);
    }

    private void UpdateAverages()
    {
        if (_sessions.Count == 0)
        {
            _averageMessages = 0;
            _averageChatters = 0;
            _averagePeakViewers = 0;
            _averageViewers = 0;
            return;
        }

        _averageMessages = _sessions.Average(row => (double)row.MessageCount);
        _averageChatters = _sessions.Average(row => (double)row.ChatterCount);
        _averagePeakViewers = _sessions.Average(row => (double)row.PeakViewers);
        _averageViewers = _sessions.Average(row => (double)row.AverageViewers);
    }

    private void UpdateSummary()
    {
        var totalDuration = _sessions.Aggregate(TimeSpan.Zero, (sum, row) => sum + row.Duration);

        TotalSessionsText = FormatNumber(_sessions.Count);
        TotalSessionsLabel = SessionTerm.ForCount(_sessions.Count);
        TotalAirTimeText = _sessions.Count > 0 ? FormatAirTime(totalDuration) : Placeholder;
        LastStreamText = _sessions.Count > 0
            ? RelativeTime.Describe(_sessions[0].StartedAt, DateTimeOffset.Now)
            : Placeholder;
    }

    private void BuildTrend()
    {
        Trend.Clear();

        var recent = _sessions.Take(TrendLength).Reverse().ToList();
        var leader = recent.Count > 0 ? recent.Max(row => row.PeakViewers) : 0;

        foreach (var row in recent)
        {
            Trend.Add(new(row, leader));
        }

        OnPropertyChanged(nameof(HasTrend));
        OnPropertyChanged(nameof(TrendCaption));
    }
}

public sealed class StreamSessionRowViewModel
{
    public StreamSessionRecord Source { get; }

    public DateTimeOffset StartedAt => Source.StartedAt;
    public TimeSpan Duration => Source.Duration;
    public long MessageCount => Source.MessageCount;
    public int ChatterCount => Source.ChatterCount;
    public int PeakViewers => Source.PeakViewers;
    public int AverageViewers => Source.AverageViewers;

    public bool HasTitle { get; }
    public string TitleFormatted { get; }
    public string StartedAtFormatted { get; }
    public string DurationFormatted { get; }
    public string GameFormatted { get; }
    public string MessageCountFormatted { get; }
    public string ChatterCountFormatted { get; }
    public string PeakViewersFormatted { get; }
    public string AverageViewersFormatted { get; }

    public StreamSessionRowViewModel(StreamSessionRecord source)
    {
        Source = source;
        HasTitle = source.Title is { Length: > 0 };
        TitleFormatted = HasTitle ? source.Title! : "Без названия";
        StartedAtFormatted = StreamHistoryPageViewModel.FormatDateTime(source.StartedAt);
        DurationFormatted = StreamHistoryPageViewModel.FormatDuration(source.Duration);
        GameFormatted = BuildGameDisplay(source);
        MessageCountFormatted = StreamHistoryPageViewModel.FormatNumber(source.MessageCount);
        ChatterCountFormatted = StreamHistoryPageViewModel.FormatNumber(source.ChatterCount);
        PeakViewersFormatted = StreamHistoryPageViewModel.FormatNumber(source.PeakViewers);
        AverageViewersFormatted = StreamHistoryPageViewModel.FormatNumber(source.AverageViewers);
    }

    private static string BuildGameDisplay(StreamSessionRecord source)
    {
        var game = string.IsNullOrEmpty(source.Game) ? "–" : source.Game;
        var extra = source.Segments.Count - 1;

        return extra > 0 ? string.Create(UiCulture.Russian, $"{game} (+{extra:N0})") : game;
    }
}

public sealed partial class StreamTrendBarViewModel : ObservableObject
{
    public const double MinimumShare = 0.06;

    [ObservableProperty]
    private bool _isSelected;

    public StreamTrendBarViewModel(StreamSessionRowViewModel row, int leaderPeakViewers)
    {
        Row = row;
        Share = leaderPeakViewers > 0
            ? Math.Clamp((double)row.PeakViewers / leaderPeakViewers, MinimumShare, 1)
            : MinimumShare;
        FillTrack = new(Share, GridUnitType.Star);
        RestTrack = new(1 - Share, GridUnitType.Star);
        Label = string.Create(
            UiCulture.Russian,
            $"{RelativeTime.FormatDate(row.StartedAt)}, пик зрителей {row.PeakViewersFormatted}");
    }

    public StreamSessionRowViewModel Row { get; }
    public double Share { get; }
    public GridLength FillTrack { get; }
    public GridLength RestTrack { get; }
    public string Label { get; }
}

public sealed class StreamSessionSegmentRowViewModel
{
    public string Game { get; }
    public string Duration { get; }
    public string Messages { get; }
    public string PeakViewers { get; }
    public string AverageViewers { get; }
    public double Share { get; }
    public bool IsAlternate { get; }
    public string TimelineText { get; }

    public StreamSessionSegmentRowViewModel(StreamSessionSegment segment, double share, bool isAlternate)
    {
        Game = segment.Game is { Length: > 0 } game ? game : "–";
        Duration = StreamHistoryPageViewModel.FormatDuration(segment.Duration);
        Messages = StreamHistoryPageViewModel.FormatNumber(segment.MessageCount);
        PeakViewers = StreamHistoryPageViewModel.FormatNumber(segment.PeakViewers);
        AverageViewers = StreamHistoryPageViewModel.FormatNumber(segment.AverageViewers);
        Share = share;
        IsAlternate = isAlternate;
        TimelineText = $"{Game}, {Duration}";
    }
}

public sealed class StreamSessionChatterRowViewModel
{
    public string DisplayName { get; }
    public long MessageCount { get; }
    public string MessageCountFormatted { get; }
    public int Position { get; }
    public double Share { get; }
    public bool IsTopThree { get; }
    public string Summary { get; }

    public StreamSessionChatterRowViewModel(StreamSessionChatter chatter, int position, long leaderMessageCount)
    {
        DisplayName = chatter.DisplayName;
        MessageCount = chatter.MessageCount;
        MessageCountFormatted = StreamHistoryPageViewModel.FormatNumber(chatter.MessageCount);
        Position = position;
        IsTopThree = position is > 0 and <= 3;
        Share = leaderMessageCount > 0
            ? Math.Clamp((double)chatter.MessageCount / leaderMessageCount, UserStatisticsRanking.MinimumShare, 1)
            : UserStatisticsRanking.MinimumShare;
        Summary = string.Create(
            UiCulture.Russian,
            $"{position:N0}. {DisplayName}, {MessageCountFormatted} {ChatMessageTerm.Instance.ForCount(chatter.MessageCount)}");
    }
}
