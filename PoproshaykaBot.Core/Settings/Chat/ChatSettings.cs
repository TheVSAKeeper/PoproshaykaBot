namespace PoproshaykaBot.Core.Settings.Chat;

public sealed class ChatSettings
{
    public const int DefaultHistoryMaxItems = 1000;
    public const int MinHistoryMaxItems = 1;
    public const int MaxHistoryMaxItems = 10_000;

    public bool ShowHistoryInApp { get; set; }

    public bool ShowHistoryInObs { get; set; }

    public bool FetchTwitchRecentMessages { get; set; }

    public int HistoryMaxItems { get; set; } = DefaultHistoryMaxItems;

    public static int ClampHistoryMaxItems(int value)
    {
        return Math.Clamp(value, MinHistoryMaxItems, MaxHistoryMaxItems);
    }
}
