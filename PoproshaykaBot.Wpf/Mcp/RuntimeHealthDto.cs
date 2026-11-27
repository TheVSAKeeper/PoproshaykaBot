using KeepShell.Updates;
using PoproshaykaBot.Core.Chat.Display;
using PoproshaykaBot.Core.Infrastructure.Runtime;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Diagnostics;

namespace PoproshaykaBot.Wpf.Mcp;

public sealed record RuntimeHealthDto(
    RuntimeHealthDto.BuildInfoDto Build,
    RuntimeHealthDto.StorageInfoDto Storage,
    PreviousRunState? PreviousRun,
    UnobservedTaskExceptionSnapshot? UnobservedTaskExceptions,
    RuntimeHealthDto.BindingErrorsDto? BindingErrors,
    SettingsWriteGateStatus? SettingsWriteGate,
    UiSettingsWriteGuardStatus? UiSettingsWriteGuard,
    ChatDisplayStoreStatus? ChatDisplayStore,
    RuntimeHealthDto.UserStatisticsHealthDto? UserStatistics,
    BroadcastHistoryDiagnosticsSummary? BroadcastHistory,
    ActiveBroadcastDiagnosticsSummary? ActiveBroadcast,
    PollHistoryDiagnosticsSummary? PollHistory,
    RuntimeHealthDto.UpdateHealthDto? Update,
    RuntimeHealthDto.DashboardLayoutHealthDto? DashboardLayout,
    string? WebView2Version,
    IReadOnlyList<DisplayMonitorInfo> Monitors)
{
    public sealed record BuildInfoDto(
        string Version,
        string? StartupLine,
        string BuildLabel);

    public sealed record StorageInfoDto(
        string Mode,
        string BaseDirectory,
        UpdateKind? UpdateKind);

    public sealed record BindingErrorItemDto(
        string? Case,
        string? Theme,
        string Message);

    public sealed record BindingErrorsDto(
        int TotalCount,
        int BufferedCount,
        IReadOnlyList<BindingErrorItemDto> RecentErrors);

    public sealed record UserStatisticsHealthDto(
        StatisticsLoadState LoadState,
        bool IsLoaded,
        int? AccountsCount,
        int? ChannelCountersCount,
        int? UnattributedCountersCount,
        int? LinkGroupsCount);

    public sealed record UpdateHealthDto(
        UpdateKind Kind,
        bool IsUpdatable,
        string CurrentVersion,
        string? LatestCandidateVersion,
        bool HasPreparedUpdate,
        string? PreparedVersion);

    public sealed record DashboardLayoutHealthDto(
        int Revision,
        int TotalTiles,
        int VisibleTiles,
        int HiddenTiles,
        int CollapsedTiles);
}

public sealed record SettingsSummaryDto(
    IReadOnlyList<SettingsSummaryDto.EntryDto> Entries,
    SettingsWriteGateStatus? SettingsWriteGate,
    UiSettingsWriteGuardStatus? UiSettingsWriteGuard)
{
    public sealed record EntryDto(string Key, string Description);
}
