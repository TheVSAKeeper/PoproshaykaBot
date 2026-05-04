using System.Globalization;

namespace PoproshaykaBot.Core.Chat.Commands;

internal static class FormattingUtils
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly TimeZoneInfo MoscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");

    public static string FormatNumber(long number)
    {
        return number.ToString("N0", RussianCulture);
    }

    public static string FormatNumber(ulong number)
    {
        return number.ToString("N0", RussianCulture);
    }

    public static string FormatNumber(decimal number)
    {
        return number.ToString("N0", RussianCulture);
    }

    public static string FormatDateTime(DateTime utcDateTime)
    {
        var moscowTime = TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, MoscowTimeZone);
        return moscowTime.ToString("dd.MM.yyyy HH:mm", RussianCulture) + " МСК";
    }

    public static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
        {
            return $"{(int)timeSpan.TotalDays}д {timeSpan.Hours}ч {timeSpan.Minutes}м";
        }

        if (timeSpan.TotalHours >= 1)
        {
            return $"{timeSpan.Hours}ч {timeSpan.Minutes}м";
        }

        return $"{timeSpan.Minutes}м {timeSpan.Seconds}с";
    }
}
