namespace PoproshaykaBot.Core.YouTube.Quota;

internal static class YouTubeQuotaPolicy
{
    public const string UnconfiguredProjectKey = "__unconfigured";
    public const int MinimumDailyBudget = 100;

    public static string NormalizeProjectKey(YouTubeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!string.IsNullOrWhiteSpace(settings.ProjectId))
        {
            return settings.ProjectId.Trim().ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(settings.ClientId))
        {
            return settings.ClientId.Trim().ToLowerInvariant();
        }

        return UnconfiguredProjectKey;
    }

    public static int NormalizeBudget(int configuredBudget)
    {
        return Math.Max(MinimumDailyBudget, configuredBudget);
    }

    public static int GetCeiling(
        int dailyBudget,
        YouTubeQuotaPriority priority,
        YouTubeQuotaReliability reliability)
    {
        var budget = NormalizeBudget(dailyBudget);

        if (reliability == YouTubeQuotaReliability.CorruptFile
            && priority is YouTubeQuotaPriority.StandaloneGreeting or YouTubeQuotaPriority.ScheduledBroadcast)
        {
            return 0;
        }

        return priority switch
        {
            YouTubeQuotaPriority.CriticalReadAndMonitor => budget,
            YouTubeQuotaPriority.CommandReply => Math.Max(0, budget - Math.Clamp((int)(budget * 0.20), 100, 2_000)),
            YouTubeQuotaPriority.StandaloneGreeting => Math.Max(0, budget - Math.Clamp((int)(budget * 0.30), 200, 3_000)),
            YouTubeQuotaPriority.ScheduledBroadcast => Math.Max(0, budget - Math.Clamp((int)(budget * 0.45), 300, 4_500)),
            _ => 0,
        };
    }
}
