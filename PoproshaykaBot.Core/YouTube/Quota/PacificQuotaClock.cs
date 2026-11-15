namespace PoproshaykaBot.Core.YouTube.Quota;

internal readonly record struct PacificQuotaWindow(
    DateOnly PacificDate,
    DateTimeOffset NextResetAtUtc);

internal static class PacificQuotaClock
{
    private static readonly TimeZoneInfo PacificTimeZone = ResolvePacificTimeZone();

    public static PacificQuotaWindow GetWindow(DateTimeOffset utcNow)
    {
        var utc = utcNow.ToUniversalTime();
        var local = TimeZoneInfo.ConvertTime(utc, PacificTimeZone);
        var pacificDate = DateOnly.FromDateTime(local.DateTime);
        var nextMidnight = pacificDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var nextResetUtc = ConvertLocalToUtc(nextMidnight);

        return new(pacificDate, nextResetUtc);
    }

    private static DateTimeOffset ConvertLocalToUtc(DateTime localTime)
    {
        var adjusted = PacificTimeZone.IsInvalidTime(localTime)
            ? localTime.AddHours(1)
            : localTime;

        var offset = PacificTimeZone.IsAmbiguousTime(adjusted)
            ? PacificTimeZone.GetAmbiguousTimeOffsets(adjusted).Min()
            : PacificTimeZone.GetUtcOffset(adjusted);

        return new DateTimeOffset(adjusted, offset).ToUniversalTime();
    }

    private static TimeZoneInfo ResolvePacificTimeZone()
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById("America/Los_Angeles", out var iana))
        {
            return iana;
        }

        return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
    }
}
