using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class UserStatisticsRowViewModel : ObservableObject
{
    private static readonly PointTerm MessageTerm = new()
    {
        Singular = "сообщение",
        Few = "сообщения",
        Many = "сообщений",
    };

    [ObservableProperty]
    private int _position;

    [ObservableProperty]
    private double _share;

    public UserStatisticsRowViewModel(UserStatistics source, string rankDisplay, PointTerm pointTerm)
    {
        Source = source;
        RankDisplay = rankDisplay;
        PointsDisplay = source.Points.ToString("N0", UiCulture.Russian);
        MessageCountDisplay = source.MessageCount.ToString("N0", UiCulture.Russian);
        PointsUnit = BuildPointsUnit(pointTerm);

        var messageCount = (long)Math.Min(source.MessageCount, (ulong)long.MaxValue);

        Summary = $"{source.Name} · {PointsDisplay} {pointTerm.ForCount(source.Points)} · "
            + $"{MessageCountDisplay} {MessageTerm.ForCount(messageCount)}";
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
    public string PointsUnit { get; }
    public string Summary { get; }

    private static string BuildPointsUnit(PointTerm pointTerm)
    {
        var singular = pointTerm.Singular.Trim();

        return singular.Length > 0 ? singular[..1].ToLowerInvariant() : "б";
    }
}
