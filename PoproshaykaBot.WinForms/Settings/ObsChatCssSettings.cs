namespace PoproshaykaBot.WinForms.Settings;

public class ObsChatCssSettings
{
    public string BackgroundColor { get; set; } = "rgba(0, 0, 0, 0.7)";
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

    public static ObsChatCssSettings FromObsChatSettings(ObsChatSettings? settings)
    {
        var safeSettings = settings ?? new ObsChatSettings();

        return new()
        {
            BackgroundColor = safeSettings.BackgroundColor ?? "rgba(0, 0, 0, 0.7)",
            TextColor = safeSettings.TextColor ?? "#ffffff",
            UsernameColor = safeSettings.UsernameColor ?? "#9146ff",
            SystemMessageColor = safeSettings.SystemMessageColor ?? "#ffcc00",
            TimestampColor = safeSettings.TimestampColor ?? "#999999",
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
        };
    }

    private static int ValidateRange(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(max, value));
    }
}
