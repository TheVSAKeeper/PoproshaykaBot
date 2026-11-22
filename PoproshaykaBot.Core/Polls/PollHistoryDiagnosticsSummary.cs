using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Polls;

public sealed record PollHistoryDiagnosticsSummary(
    StatisticsLoadState ReadState,
    bool CanPersist,
    bool RewrittenExternally,
    bool HasUnsavedChanges,
    int EntryCount);
