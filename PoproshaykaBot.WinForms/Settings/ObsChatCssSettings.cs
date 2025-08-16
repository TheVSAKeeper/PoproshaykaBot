namespace PoproshaykaBot.WinForms.Settings;

public class ObsChatCssSettings
{
    public string BackgroundColor { get; set; } = "#000000b5";
    public string TextColor { get; set; } = "#ffffff";
    public string UsernameColor { get; set; } = "#9146ff";
    public string SystemMessageColor { get; set; } = "#ffcc00";
    public string TimestampColor { get; set; } = "#999999";
    public string FontFamily { get; set; } = "Arial, sans-serif";
    public string FontSize { get; set; } = "14px";
    public string FontWeight { get; set; } = "normal";
    public string Padding { get; set; } = "5px";
    public string Margin { get; set; } = "5px 0";
    public string BorderRadius { get; set; } = "5px";
    public string AnimationDuration { get; set; } = "300ms";
    public bool EnableAnimations { get; set; } = true;
    public int MaxMessages { get; set; } = 50;
    public bool ShowTimestamp { get; set; } = true;

    public string EmoteSize { get; set; } = "28px";
    public string BadgeSize { get; set; } = "18px";

    public static ObsChatCssSettings FromObsChatSettings(ObsChatSettings? settings)
    {
        var safeSettings = settings ?? new ObsChatSettings();

        return new()
        {
            BackgroundColor = ColorToCssString(safeSettings.BackgroundColor),
            TextColor = ColorToCssString(safeSettings.TextColor),
            UsernameColor = ColorToCssString(safeSettings.UsernameColor),
            SystemMessageColor = ColorToCssString(safeSettings.SystemMessageColor),
            TimestampColor = ColorToCssString(safeSettings.TimestampColor),
            FontFamily = safeSettings.FontFamily ?? "Arial, sans-serif",
            FontSize = $"{ValidateRange(safeSettings.FontSize, 8, 72)}px",
            FontWeight = safeSettings.FontBold ? "bold" : "normal",
            Padding = $"{ValidateRange(safeSettings.Padding, 0, 50)}px",
            Margin = $"{ValidateRange(safeSettings.Margin, 0, 50)}px 0",
            BorderRadius = $"{ValidateRange(safeSettings.BorderRadius, 0, 50)}px",
            AnimationDuration = $"{ValidateRange(safeSettings.AnimationDuration, 100, 2000)}ms",
            EnableAnimations = safeSettings.EnableAnimations,
            MaxMessages = ValidateRange(safeSettings.MaxMessages, 10, 200),
            ShowTimestamp = safeSettings.ShowTimestamp,

            EmoteSize = $"{ValidateRange(safeSettings.EmoteSizePixels, 16, 128)}px",
            BadgeSize = $"{ValidateRange(safeSettings.BadgeSizePixels, 12, 72)}px",
        };
    }

    private static int ValidateRange(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }

    private static string ColorToCssString(Color color)
    {
        if (color.A == 255)
        {
            return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        return $"#{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";
    }
}
