using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class UserStatisticsRowViewModel : ObservableObject
{
    public const double VisibleTrackShare = 0.03;

    private static readonly PointTerm MessageTerm = ChatMessageTerm.Instance;

    private readonly PointTerm _pointTerm;
    private UserRankLadder? _rankLadder;

    [ObservableProperty]
    private int _position;

    [ObservableProperty]
    private double _share;

    partial void OnPositionChanged(int oldValue, int newValue)
    {
        if ((oldValue is > 0 and <= 3) != (newValue is > 0 and <= 3))
        {
            OnPropertyChanged(nameof(IsTopThree));
        }
    }

    public UserStatisticsRowViewModel(UserStatistics source, UserRankStanding standing, PointTerm pointTerm)
        : this(
            ToAggregate(source ?? throw new ArgumentNullException(nameof(source))),
            standing,
            pointTerm,
            channelCatalog: null,
            legacySource: source)
    {
    }

    public UserStatisticsRowViewModel(
        ViewerAggregateStatistics aggregate,
        UserRankStanding standing,
        PointTerm pointTerm,
        ChannelCatalogStore? channelCatalog = null)
        : this(aggregate, standing, pointTerm, channelCatalog, legacySource: null)
    {
    }

    private UserStatisticsRowViewModel(
        ViewerAggregateStatistics aggregate,
        UserRankStanding standing,
        PointTerm pointTerm,
        ChannelCatalogStore? channelCatalog,
        UserStatistics? legacySource)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(standing);
        ArgumentNullException.ThrowIfNull(pointTerm);

        Aggregate = aggregate;
        Source = legacySource ?? new UserStatistics
        {
            Platform = aggregate.PrimaryKey.Platform,
            UserId = aggregate.PrimaryKey.UserId,
            Name = aggregate.DisplayName,
            MessageCount = aggregate.MessageCount,
            BonusPoints = aggregate.BonusPoints,
            PenaltyPoints = aggregate.PenaltyPoints,
            FirstSeen = aggregate.FirstSeen,
            LastSeen = aggregate.LastSeen,
        };
        Standing = standing;
        RankDisplay = standing.Display;
        PointsDisplay = aggregate.Points.ToString("N0", UiCulture.Russian);
        MessageCountDisplay = aggregate.MessageCount.ToString("N0", UiCulture.Russian);

        var messageCount = (long)Math.Min(aggregate.MessageCount, (ulong)long.MaxValue);
        var bonus = (long)Math.Min(aggregate.BonusPoints, (ulong)long.MaxValue);
        var penalty = (long)Math.Min(aggregate.PenaltyPoints, (ulong)long.MaxValue);

        Summary = $"{aggregate.DisplayName} · {PointsDisplay} {pointTerm.ForCount(aggregate.Points)} · "
            + $"{MessageCountDisplay} {MessageTerm.ForCount(messageCount)}";

        HasBonus = bonus > 0;
        HasPenalty = penalty > 0;

        BonusDisplay = HasBonus ? string.Create(UiCulture.Russian, $"+{bonus:N0}") : "0";
        PenaltyDisplay = HasPenalty ? string.Create(UiCulture.Russian, $"−{penalty:N0}") : "0";

        MessagesPartText = $"{MessageCountDisplay} {MessageTerm.ForCount(messageCount)}";
        BonusPartText = string.Create(UiCulture.Russian, $"+{bonus:N0} бонус");
        PenaltyPartText = string.Create(UiCulture.Russian, $"−{penalty:N0} вычтено штрафом");

        MessagesTrack = new(messageCount, GridUnitType.Star);
        BonusTrack = new(bonus, GridUnitType.Star);

        var earned = (double)messageCount + bonus;
        IsPenaltyCutWhole = HasPenalty && penalty - bonus >= messageCount;
        EarnedNetTrack = new(IsPenaltyCutWhole ? 0 : (double)(messageCount - penalty) + bonus, GridUnitType.Star);
        PenaltyCutTrack = new(IsPenaltyCutWhole ? 1 : penalty, GridUnitType.Star);

        IsMessagesTrackTiny = IsBelowVisibleShare(messageCount, earned);
        IsBonusTrackTiny = IsBelowVisibleShare(bonus, earned);

        _pointTerm = pointTerm;

        var now = DateTimeOffset.Now;
        var firstSeen = RelativeTime.ToOffset(aggregate.FirstSeen);
        var lastSeen = RelativeTime.ToOffset(aggregate.LastSeen);
        var days = Math.Max(1, (int)(now.Date - firstSeen.ToLocalTime().Date).TotalDays + 1);

        FirstSeenDisplay = RelativeTime.FormatDate(firstSeen);
        FirstSeenMoment = RelativeTime.FormatMoment(firstSeen);
        LastSeenDisplay = RelativeTime.DescribeMoment(lastSeen, now);
        LastSeenMoment = RelativeTime.FormatMoment(lastSeen);
        DaysInChatDisplay = days.ToString("N0", UiCulture.Russian);
        DailyRateDisplay = ((double)messageCount / days).ToString("N1", UiCulture.Russian);

        ChannelBreakdowns = aggregate.ChannelBreakdowns
            .Select(b => CreateBreakdownRow(b, pointTerm, channelCatalog))
            .ToList();

        LinkedAccounts = aggregate.Accounts
            .Select(a => new UserLinkedAccountRowViewModel(
                a.Key,
                PlatformVisuals.GetDisplayName(a.Key.Platform),
                a.Name,
                a.LinkOrigin switch
                {
                    AccountLinkOrigin.ViewerConfirmed => "подтверждено зрителем",
                    AccountLinkOrigin.StreamerManual => "вручную стримером",
                    _ => string.Empty,
                },
                aggregate.IsLinked))
            .ToList();
    }

    public ViewerAggregateStatistics Aggregate { get; }
    public UserStatistics Source { get; }
    public UserRankStanding Standing { get; }
    public UserAvatarViewModel Avatar { get; init; } = UserAvatarViewModel.None;
    public PlatformUserKey Key => Aggregate.PrimaryKey;
    public StreamingPlatform Platform => Aggregate.PrimaryKey.Platform;
    public string UserId => Aggregate.PrimaryKey.UserId;
    public string Name => Aggregate.DisplayName;
    public ulong MessageCount => Aggregate.MessageCount;
    public long Points => Aggregate.Points;
    public ulong BonusPoints => Aggregate.BonusPoints;
    public ulong PenaltyPoints => Aggregate.PenaltyPoints;
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
    public GridLength EarnedNetTrack { get; }
    public GridLength PenaltyCutTrack { get; }
    public bool IsPenaltyCutWhole { get; }
    public bool IsMessagesTrackTiny { get; }
    public bool IsBonusTrackTiny { get; }
    public bool HasTinyTrack => IsMessagesTrackTiny || IsBonusTrackTiny;
    public int Place { get; set; }
    public UserRankLadder? RankLadder => _rankLadder ??= UserRankLadder.Build(Points, Standing, _pointTerm);
    public bool HasRankLadder => RankLadder is not null;
    public string FirstSeenDisplay { get; }
    public string FirstSeenMoment { get; }
    public string LastSeenDisplay { get; }
    public string LastSeenMoment { get; }
    public string DaysInChatDisplay { get; }
    public string DailyRateDisplay { get; }
    public bool IsLinked => Aggregate.IsLinked;
    public IReadOnlyList<UserChannelBreakdownRowViewModel> ChannelBreakdowns { get; }
    public bool HasChannelBreakdowns => ChannelBreakdowns.Count > 1 || IsLinked;
    public IReadOnlyList<UserLinkedAccountRowViewModel> LinkedAccounts { get; }

    public bool IsTopThree => Position is > 0 and <= 3;

    public bool MatchesAccount(PlatformUserKey key) => Aggregate.MatchesAccount(key);

    public bool MatchesQuery(string query)
    {
        return Aggregate.Accounts.Any(account =>
            account.Name.Contains(query, StringComparison.InvariantCultureIgnoreCase)
            || account.Key.UserId.Contains(query, StringComparison.InvariantCultureIgnoreCase));
    }

    private static ViewerAggregateStatistics ToAggregate(UserStatistics source)
    {
        var key = source.Key;
        return new(
            ViewerIdentityKey.FromUser(key),
            key,
            source.Name,
            source.MessageCount,
            source.BonusPoints,
            source.PenaltyPoints,
            source.FirstSeen,
            source.LastSeen,
            [new ViewerAccountInfo(key, source.Name, source.FirstSeen, source.LastSeen)],
            [
                new ViewerChannelBreakdown(
                    StatisticsCounterTarget.ForUnattributed(source.Platform),
                    source.MessageCount,
                    source.BonusPoints,
                    source.PenaltyPoints,
                    source.FirstSeen,
                    source.LastSeen),
            ],
            null);
    }

    private static UserChannelBreakdownRowViewModel CreateBreakdownRow(
        ViewerChannelBreakdown breakdown,
        PointTerm pointTerm,
        ChannelCatalogStore? channelCatalog)
    {
        var platformLabel = PlatformVisuals.GetDisplayName(breakdown.Target.Platform);
        var label = breakdown.Target.AsChannelKey is { } channelKey
            ? $"{platformLabel} · {channelCatalog?.ResolveDisplayName(channelKey) ?? channelKey.ChannelId}"
            : $"{platformLabel} · без канала";

        var messageText = $"{breakdown.MessageCount.ToString("N0", UiCulture.Russian)} {MessageTerm.ForCount((long)breakdown.MessageCount)}";
        var pointsText = $"{breakdown.Points.ToString("N0", UiCulture.Russian)} {pointTerm.ForCount(breakdown.Points)}";

        return new(breakdown.Target, label, messageText, pointsText, breakdown.LastSeen);
    }

    private static bool IsBelowVisibleShare(long part, double earned)
    {
        return part > 0 && part / earned < VisibleTrackShare;
    }
}

public sealed record UserChannelBreakdownRowViewModel(
    StatisticsCounterTarget Target,
    string Label,
    string MessageCountDisplay,
    string PointsDisplay,
    DateTime LastSeen);

public sealed record UserLinkedAccountRowViewModel(
    PlatformUserKey Key,
    string PlatformLabel,
    string Name,
    string OriginLabel,
    bool CanUnlink)
{
    public StreamingPlatform Platform => Key.Platform;
    public string UserId => Key.UserId;
    public bool HasOriginLabel => OriginLabel.Length > 0;
}
