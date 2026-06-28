using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Statistics;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class StreamHistoryPageViewModel : ObservableObject, IPageHeader, IDisposable
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const string Placeholder = "–";

    private readonly StreamSessionHistoryStore _historyStore;
    private readonly List<IDisposable> _subs = [];
    private readonly ObservableCollection<StreamSessionRowViewModel> _sessions = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailTitle))]
    [NotifyPropertyChangedFor(nameof(DetailGame))]
    [NotifyPropertyChangedFor(nameof(DetailStarted))]
    [NotifyPropertyChangedFor(nameof(DetailEnded))]
    [NotifyPropertyChangedFor(nameof(DetailDuration))]
    [NotifyPropertyChangedFor(nameof(DetailMessages))]
    [NotifyPropertyChangedFor(nameof(DetailChatters))]
    [NotifyPropertyChangedFor(nameof(DetailViewers))]
    private StreamSessionRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _summary = string.Empty;

    public string PageTitle => "История стримов";
    public string? PageDescription => null;

    public ICollectionView SessionsView { get; }
    public ObservableCollection<StreamSessionSegmentRowViewModel> Segments { get; } = [];
    public ObservableCollection<StreamSessionChatterRowViewModel> Chatters { get; } = [];

    public StreamHistoryPageViewModel(StreamSessionHistoryStore historyStore, IEventBus eventBus)
    {
        _historyStore = historyStore;

        var cvs = new CollectionViewSource { Source = _sessions };
        cvs.SortDescriptions.Add(new SortDescription(nameof(StreamSessionRowViewModel.StartedAt), ListSortDirection.Descending));
        SessionsView = cvs.View;

        _subs.Add(eventBus.SubscribeOnUi<StreamSessionCompleted>(_ => RefreshCommand.Execute(null)));

        Refresh();
    }

    partial void OnSelectedRowChanged(StreamSessionRowViewModel? value)
    {
        Segments.Clear();
        Chatters.Clear();

        if (value?.Source is not { } session)
        {
            return;
        }

        foreach (var seg in session.Segments)
        {
            Segments.Add(new StreamSessionSegmentRowViewModel(seg));
        }

        foreach (var ch in session.Chatters)
        {
            Chatters.Add(new StreamSessionChatterRowViewModel(ch));
        }
    }

    [RelayCommand]
    private void Refresh()
    {
        var history = _historyStore.Load();
        var selectedId = SelectedRow?.Source.Id;

        _sessions.Clear();

        foreach (var record in history.Sessions)
        {
            _sessions.Add(new StreamSessionRowViewModel(record));
        }

        UpdateSummary();

        SelectedRow = selectedId.HasValue
            ? _sessions.FirstOrDefault(r => r.Source.Id == selectedId)
            : null;
    }

    public string DetailTitle => SelectedRow?.Source.Title is { Length: > 0 } t ? t : Placeholder;
    public string DetailGame => SelectedRow?.Source.Game is { Length: > 0 } g ? g : Placeholder;
    public string DetailStarted => SelectedRow != null ? FormatDateTime(SelectedRow.StartedAt) : Placeholder;
    public string DetailEnded => SelectedRow != null ? FormatDateTime(SelectedRow.Source.EndedAt) : Placeholder;
    public string DetailDuration => SelectedRow?.DurationFormatted ?? Placeholder;
    public string DetailMessages => SelectedRow?.MessageCountFormatted ?? Placeholder;
    public string DetailChatters => SelectedRow?.ChatterCountFormatted ?? Placeholder;

    public string DetailViewers => SelectedRow != null
        ? $"пик {SelectedRow.PeakViewersFormatted} / средн. {SelectedRow.AverageViewersFormatted}"
        : $"пик {Placeholder} / средн. {Placeholder}";

    private void UpdateSummary()
    {
        var totalDuration = _sessions.Aggregate(TimeSpan.Zero, (sum, r) => sum + r.Duration);
        Summary = $"Стримов: {_sessions.Count:N0}  ·  Общее время: {FormatDuration(totalDuration)}";
    }

    public static string FormatDateTime(DateTimeOffset value)
        => value.ToLocalTime().ToString("dd.MM.yyyy HH:mm", RuCulture);

    public static string FormatDuration(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}д {span.Hours}ч {span.Minutes}м");
        }

        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{span.Hours}ч {span.Minutes}м");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}м {span.Seconds}с");
    }

    public static string FormatNumber(long value) => value.ToString("N0", RuCulture);

    public void Dispose()
    {
        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        _subs.Clear();
    }
}

public sealed class StreamSessionRowViewModel
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public StreamSessionRecord Source { get; }

    public DateTimeOffset StartedAt => Source.StartedAt;
    public TimeSpan Duration => Source.Duration;
    public long MessageCount => Source.MessageCount;
    public int ChatterCount => Source.ChatterCount;
    public int PeakViewers => Source.PeakViewers;
    public int AverageViewers => Source.AverageViewers;

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
        StartedAtFormatted = source.StartedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", RuCulture);
        DurationFormatted = StreamHistoryPageViewModel.FormatDuration(source.Duration);
        GameFormatted = BuildGameDisplay(source);
        MessageCountFormatted = source.MessageCount.ToString("N0", RuCulture);
        ChatterCountFormatted = source.ChatterCount.ToString("N0", RuCulture);
        PeakViewersFormatted = source.PeakViewers.ToString("N0", RuCulture);
        AverageViewersFormatted = source.AverageViewers.ToString("N0", RuCulture);
    }

    private static string BuildGameDisplay(StreamSessionRecord s)
    {
        var game = string.IsNullOrEmpty(s.Game) ? "–" : s.Game;
        var extra = s.Segments.Count - 1;
        return extra > 0 ? $"{game} (+{extra})" : game;
    }
}

public sealed class StreamSessionSegmentRowViewModel
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public string Game { get; }
    public string Duration { get; }
    public string Messages { get; }
    public string PeakViewers { get; }
    public string AverageViewers { get; }

    public StreamSessionSegmentRowViewModel(StreamSessionSegment segment)
    {
        Game = segment.Game is { Length: > 0 } g ? g : "–";
        Duration = StreamHistoryPageViewModel.FormatDuration(segment.Duration);
        Messages = segment.MessageCount.ToString("N0", RuCulture);
        PeakViewers = segment.PeakViewers.ToString("N0", RuCulture);
        AverageViewers = segment.AverageViewers.ToString("N0", RuCulture);
    }
}

public sealed class StreamSessionChatterRowViewModel
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public string DisplayName { get; }
    public long MessageCount { get; }
    public string MessageCountFormatted { get; }

    public StreamSessionChatterRowViewModel(StreamSessionChatter chatter)
    {
        DisplayName = chatter.DisplayName;
        MessageCount = chatter.MessageCount;
        MessageCountFormatted = chatter.MessageCount.ToString("N0", RuCulture);
    }
}
