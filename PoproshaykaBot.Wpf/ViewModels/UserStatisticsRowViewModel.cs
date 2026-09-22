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

    public UserStatisticsRowViewModel(UserStatistics source, UserRankStanding standing, PointTerm pointTerm)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(pointTerm);

        Source = source;
        Standing = standing;
        RankDisplay = standing.Display;
        PointsDisplay = source.Points.ToString("N0", UiCulture.Russian);
        MessageCountDisplay = source.MessageCount.ToString("N0", UiCulture.Russian);

        var messageCount = (long)Math.Min(source.MessageCount, (ulong)long.MaxValue);
        var bonus = (long)Math.Min(source.BonusPoints, (ulong)long.MaxValue);
        var penalty = (long)Math.Min(source.PenaltyPoints, (ulong)long.MaxValue);

        Summary = $"{source.Name} · {PointsDisplay} {pointTerm.ForCount(source.Points)} · "
            + $"{MessageCountDisplay} {MessageTerm.ForCount(messageCount)}";

        HasBonus = bonus > 0;
        HasPenalty = penalty > 0;

        BonusDisplay = HasBonus ? string.Create(UiCulture.Russian, $"+{bonus:N0}") : "0";
        PenaltyDisplay = HasPenalty ? string.Create(UiCulture.Russian, $"−{penalty:N0}") : "0";

        MessagesPartText = $"{MessageCountDisplay} {MessageTerm.ForCount(messageCount)}";
        BonusPartText = string.Create(UiCulture.Russian, $"+{bonus:N0} бонус");
        PenaltyPartText = string.Create(UiCulture.Russian, $"−{penalty:N0} штраф");

        MessagesTrack = new(messageCount, GridUnitType.Star);
        BonusTrack = new(bonus, GridUnitType.Star);
        PenaltyTrack = new(penalty, GridUnitType.Star);

        HasNextRank = standing.NextDisplay is { Length: > 0 };
        HasRankPath = HasNextRank || standing.IsTopRank;

        NextRankText = HasNextRank
            ? $"До ранга {standing.NextDisplay}"
            : standing.IsTopRank ? "Максимальный ранг" : string.Empty;

        PointsToNextText = HasNextRank
            ? string.Create(UiCulture.Russian, $"{standing.PointsToNext:N0} {pointTerm.ForCount(standing.PointsToNext)}")
            : string.Empty;

        RankProgressTrack = new(standing.Progress, GridUnitType.Star);
        RankRemainderTrack = new(1 - standing.Progress, GridUnitType.Star);

        var now = DateTimeOffset.Now;
        var days = Math.Max(1, (int)(now.Date - source.FirstSeen.ToLocalTime().Date).TotalDays + 1);

        FirstSeenText = $"В чате с {RelativeTime.FormatDate(source.FirstSeen)}";
        LastSeenText = $"Писал {RelativeTime.Describe(source.LastSeen, now)}";
        DaysInChatDisplay = days.ToString("N0", UiCulture.Russian);
        DailyRateDisplay = ((double)messageCount / days).ToString("N1", UiCulture.Russian);
    }

    public UserStatistics Source { get; }
    public UserRankStanding Standing { get; }
    public string UserId => Source.UserId;
    public string Name => Source.Name;
    public ulong MessageCount => Source.MessageCount;
    public long Points => Source.Points;
    public ulong BonusPoints => Source.BonusPoints;
    public ulong PenaltyPoints => Source.PenaltyPoints;
    public ulong RankOrder => Standing.Order;
    public string RankDisplay { get; }
    public string PointsDisplay { get; }
    public string MessageCountDisplay { get; }
    public string BonusDisplay { get; }
    public string PenaltyDisplay { get; }
    public bool HasBonus { get; }
    public bool HasPenalty { get; }
    public string Summary { get; }
    public string MessagesPartText { get; }
    public string BonusPartText { get; }
    public string PenaltyPartText { get; }
    public GridLength MessagesTrack { get; }
    public GridLength BonusTrack { get; }
    public GridLength PenaltyTrack { get; }
    public bool HasNextRank { get; }
    public bool HasRankPath { get; }
    public string NextRankText { get; }
    public string PointsToNextText { get; }
    public GridLength RankProgressTrack { get; }
    public GridLength RankRemainderTrack { get; }
    public string FirstSeenText { get; }
    public string LastSeenText { get; }
    public string DaysInChatDisplay { get; }
    public string DailyRateDisplay { get; }

    public bool IsTopThree => Position is > 0 and <= 3;
}
