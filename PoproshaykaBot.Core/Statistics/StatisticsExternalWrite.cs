namespace PoproshaykaBot.Core.Statistics;

public readonly record struct StatisticsExternalWrite(bool Statistics, bool StreamHistory)
{
    public static StatisticsExternalWrite None => new(false, false);
}
