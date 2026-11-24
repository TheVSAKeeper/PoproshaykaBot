using PoproshaykaBot.Core.YouTube.Quota;

namespace PoproshaykaBot.Core.Diagnostics;

public sealed record YouTubeQuotaStatus(
    string? ProjectKey,
    DateOnly PacificDate,
    DateTimeOffset NextResetAt,
    int DailyBudget,
    int CommittedUnits,
    int ReservedUnits,
    int BotCommittedUnits,
    int BroadcasterCommittedUnits,
    int Tier1RemainingUnits,
    bool ExhaustedByServer,
    YouTubeQuotaReliability Reliability,
    bool CanWriteFile,
    IReadOnlyDictionary<YouTubeQuotaCategory, int>? CategoryUnits = null);
