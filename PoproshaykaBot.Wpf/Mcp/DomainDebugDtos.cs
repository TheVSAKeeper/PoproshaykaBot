using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming.Switching;

namespace PoproshaykaBot.Wpf.Mcp;

public sealed record AccountsReadinessDto(
    AccountsReadinessDto.OnboardingStatusDto? Onboarding,
    IReadOnlyList<AccountsReadinessDto.PlatformAccountsDto> Platforms)
{
    public sealed record OnboardingStatusDto(
        bool IsComplete,
        bool RequiresWizard,
        IReadOnlyList<string> MissingItems);

    public sealed record RoleContextReadinessDto(
        PlatformAuthRole Role,
        string ContextId,
        string Label,
        string? DisplayLabel,
        string? SubjectId,
        bool IsDefault,
        bool HasAccessToken,
        bool HasRefreshToken,
        DateTimeOffset? AccessTokenExpiresAt,
        bool IsExpired,
        IReadOnlyList<string> GrantedScopes,
        IReadOnlyList<string> RequiredScopes,
        IReadOnlyList<string> MissingScopes,
        bool HasChatBrowserProfile,
        bool? PersistedVerified);

    public sealed record PlatformAccountReadinessDto(
        StreamingPlatform Platform,
        string AccountId,
        string DisplayLabel,
        IReadOnlyList<RoleContextReadinessDto> Roles);

    public sealed record PlatformAccountsDto(
        StreamingPlatform Platform,
        IReadOnlyList<PlatformAccountReadinessDto> Accounts);
}

public sealed record ChatHistoryResponseDto(
    bool ActiveOnly,
    StreamingPlatform? FilterPlatform,
    string? FilterChannel,
    int Limit,
    int TotalMatched,
    IReadOnlyList<ChatHistoryResponseDto.ScopeDto> ActiveScopes,
    IReadOnlyList<ChatHistoryResponseDto.MessageDto> Messages)
{
    public sealed record ScopeDto(
        StreamingPlatform Platform,
        string ChannelId,
        string ChannelName,
        string? ChatSessionId);

    public sealed record MessageDto(
        DateTime Timestamp,
        StreamingPlatform Platform,
        string Channel,
        string ChannelId,
        string UserId,
        string Username,
        string DisplayName,
        string Message,
        ChatMessageType MessageType,
        bool IsBot,
        CommandResponseMark? CommandResponseMark,
        string MessageId);
}

public sealed record CommandsStateDto(
    string Prefix,
    CommandResponseTarget DefaultResponseTarget,
    StreamingPlatform? FilterPlatform,
    int PendingResponseCount,
    IReadOnlyList<CommandsStateDto.CommandEntryDto> Commands)
{
    public sealed record CommandEntryDto(
        string Canonical,
        IReadOnlyList<string> Aliases,
        PlatformCapabilities RequiredCapabilities,
        bool Enabled,
        CommandAccessLevel AccessLevel,
        CommandResponseTarget ResponseTarget,
        bool? AvailableOnPlatform,
        long UsageTotal,
        long UsageStream,
        DateTimeOffset? LastUsedAt,
        string? LastUsedBy);
}

public sealed record BroadcastsStateDto(
    ActiveBroadcastDiagnosticsSummary? ActiveDraft,
    BroadcastHistoryDiagnosticsSummary? History,
    IReadOnlyList<BroadcastsStateDto.ProfileSummaryDto> Profiles,
    BroadcastsStateDto.SchedulerStatusDto? Scheduler)
{
    public sealed record ProfileSummaryDto(
        Guid Id,
        string Name,
        string Title,
        string GameName,
        int CurrentNumber,
        string ObsSceneName,
        IReadOnlyList<StreamingPlatform> PlatformTargets,
        DateTimeOffset? LastApplyAt,
        DateTimeOffset? LastAutoAdvanceAt);

    public sealed record SchedulerStatusDto(
        bool IsActive,
        TimeSpan Interval,
        DateTime? NextRunAt,
        DateTimeOffset? LastBroadcastAt,
        int SentMessagesCount,
        string? LastError);
}

public sealed record PollsStateDto(
    PollSnapshot? CurrentPoll,
    PollsAvailability? Availability,
    string? AvailabilityError,
    int ConfiguredProfilesCount,
    PollHistoryDiagnosticsSummary? HistoryHealth,
    IReadOnlyList<PollsStateDto.PollHistoryItemDto> RecentHistory)
{
    public sealed record PollHistoryChoiceDto(
        string ChoiceId,
        string Title,
        int Votes,
        int ChannelPointsVotes,
        int BitsVotes);

    public sealed record PollHistoryItemDto(
        string PollId,
        Guid? SourceProfileId,
        string Title,
        DateTime StartedAtUtc,
        DateTime EndedAtUtc,
        PollSnapshotStatus FinalStatus,
        string? WinnerChoiceId,
        bool WinnerIsTie,
        int TotalVotes,
        IReadOnlyList<PollHistoryChoiceDto> Choices);
}

public sealed record StatisticsSummaryDto(
    StatisticsLoadState? LoadState,
    StatisticsSummaryDto.BotStatisticsDto? BotStatistics,
    StatisticsSummaryDto.CatalogCountsDto? CatalogCounts,
    StatisticsSummaryDto.SliceInfoDto Slice,
    int SliceUsersCount,
    IReadOnlyList<StatisticsSummaryDto.ViewerSummaryDto> TopViewers,
    StatisticsSummaryDto.ViewerSummaryDto? QueriedViewer)
{
    public sealed record BotStatisticsDto(
        ulong TotalMessagesProcessed,
        DateTime BotStartTime,
        DateTime LastUpdated,
        TimeSpan TotalUptime);

    public sealed record CatalogCountsDto(
        int TotalAccounts,
        int ChannelCounters,
        int UnattributedCounters,
        int LinkGroups);

    public sealed record SliceInfoDto(
        StatisticsSliceKind Kind,
        StreamingPlatform? Platform,
        string? ChannelId);

    public sealed record LinkedAccountDto(
        StreamingPlatform Platform,
        string UserId,
        string Name,
        DateTime FirstSeen,
        DateTime LastSeen);

    public sealed record ChannelBreakdownDto(
        StreamingPlatform Platform,
        string? ChannelId,
        ulong MessageCount,
        ulong BonusPoints,
        ulong PenaltyPoints,
        long Points,
        DateTime FirstSeen,
        DateTime LastSeen);

    public sealed record ViewerSummaryDto(
        string DisplayName,
        StreamingPlatform PrimaryPlatform,
        string PrimaryUserId,
        ulong MessageCount,
        ulong BonusPoints,
        ulong PenaltyPoints,
        long Points,
        DateTime FirstSeen,
        DateTime LastSeen,
        bool IsLinked,
        Guid? LinkGroupId,
        IReadOnlyList<LinkedAccountDto> LinkedAccounts,
        IReadOnlyList<ChannelBreakdownDto> ChannelBreakdowns);
}

public sealed record ObsStateResponseDto(
    bool Enabled,
    string Host,
    int Port,
    bool AutoConnect,
    string SceneName,
    string SourceName,
    IReadOnlyList<string> ChatRefreshSources,
    ObsConnectionSnapshot Connection,
    ObsDashboardSnapshot? Dashboard,
    IReadOnlyList<string>? Scenes,
    IReadOnlyList<string>? BrowserSources,
    string? QueryError);

public sealed record RefreshMonitoringResultDto(
    bool Accepted,
    IReadOnlyList<StreamingPlatform> RefreshedPlatforms,
    string? ChannelId);

public sealed record SwitchActiveChannelResultDto(
    bool Succeeded,
    ChannelSwitchOutcome Outcome,
    StreamingPlatform? ActivePlatform,
    string? ActiveChannelId,
    string? ErrorMessage);

public sealed record RefreshObsSourcesResultDto(
    bool Accepted,
    IReadOnlyList<string> ConfiguredSources,
    int RefreshedCount);

public sealed record FlushStatisticsResultDto(
    bool Accepted,
    DateTimeOffset? LastRunAt,
    string? LastError);

public sealed record ExportDiagnosticsBundleResultDto(
    string Path,
    int EntryCount,
    bool RedactionComplete);
