namespace PoproshaykaBot.WinForms.Settings.Obs;

// TODO: Костыль из-за того, что сериализатор не умеет работать с системным Color
public class AppColor(byte a, byte r, byte g, byte b)
{
    private AppColor(byte r, byte g, byte b) : this(255, r, g, b)
    {
    }

    public byte A { get; set; } = a;
    public byte R { get; set; } = r;
    public byte G { get; set; } = g;
    public byte B { get; set; } = b;

    public static implicit operator Color(AppColor appColor)
    {
        return Color.FromArgb(appColor.A, appColor.R, appColor.G, appColor.B);
    }

    public static implicit operator AppColor(Color color)
    {
        return new(color.A, color.R, color.G, color.B);
    }

    public static AppColor FromArgb(byte a, byte r, byte g, byte b)
    {
        return new(a, r, g, b);
    }

    public static AppColor FromArgb(byte r, byte g, byte b)
    {
        return new(r, g, b);
    }
}
