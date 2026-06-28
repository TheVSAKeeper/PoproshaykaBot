using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed class UserStatisticsRowViewModel
{
    public UserStatisticsRowViewModel(UserStatistics source, string rankDisplay)
    {
        Source = source;
        RankDisplay = rankDisplay;
    }

    public UserStatistics Source { get; }
    public string UserId => Source.UserId;
    public string Name => Source.Name;
    public ulong MessageCount => Source.MessageCount;
    public long Points => Source.Points;
    public ulong BonusPoints => Source.BonusPoints;
    public ulong PenaltyPoints => Source.PenaltyPoints;
    public string RankDisplay { get; }
}
