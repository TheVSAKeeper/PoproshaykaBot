using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class UserStatisticsRowViewModel : ObservableObject
{
    private static readonly PointTerm MessageTerm = ChatMessageTerm.Instance;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTopThree))]
    private int _position;

    [ObservableProperty]
    private double _share;

    public UserStatisticsRowViewModel(UserStatistics source, string rankDisplay, PointTerm pointTerm)
    {
        Source = source;
        RankDisplay = rankDisplay;
        PointsDisplay = source.Points.ToString("N0", UiCulture.Russian);
        MessageCountDisplay = source.MessageCount.ToString("N0", UiCulture.Russian);

        var messageCount = (long)Math.Min(source.MessageCount, (ulong)long.MaxValue);
        var bonus = (long)Math.Min(source.BonusPoints, (ulong)long.MaxValue);
        var penalty = (long)Math.Min(source.PenaltyPoints, (ulong)long.MaxValue);

        Summary = $"{source.Name} · {PointsDisplay} {pointTerm.ForCount(source.Points)} · "
            + $"{MessageCountDisplay} {MessageTerm.ForCount(messageCount)}";

        MessagesPartText = $"{MessageCountDisplay} {MessageTerm.ForCount(messageCount)}";
        BonusPartText = string.Create(UiCulture.Russian, $"+{bonus:N0} бонус");
        PenaltyPartText = string.Create(UiCulture.Russian, $"−{penalty:N0} штраф");

        MessagesTrack = new(messageCount, GridUnitType.Star);
        BonusTrack = new(bonus, GridUnitType.Star);
        PenaltyTrack = new(penalty, GridUnitType.Star);

        FirstSeenText = $"В чате с {RelativeTime.FormatDate(source.FirstSeen)}";
        LastSeenText = $"Писал {RelativeTime.Describe(source.LastSeen, DateTimeOffset.Now)}";
    }

    public UserStatistics Source { get; }
    public string UserId => Source.UserId;
    public string Name => Source.Name;
    public ulong MessageCount => Source.MessageCount;
    public long Points => Source.Points;
    public ulong BonusPoints => Source.BonusPoints;
    public ulong PenaltyPoints => Source.PenaltyPoints;
    public string RankDisplay { get; }
    public string PointsDisplay { get; }
    public string MessageCountDisplay { get; }
    public string Summary { get; }
    public string MessagesPartText { get; }
    public string BonusPartText { get; }
    public string PenaltyPartText { get; }
    public GridLength MessagesTrack { get; }
    public GridLength BonusTrack { get; }
    public GridLength PenaltyTrack { get; }
    public string FirstSeenText { get; }
    public string LastSeenText { get; }

    public bool IsTopThree => Position is > 0 and <= 3;
}
