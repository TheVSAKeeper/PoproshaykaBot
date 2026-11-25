using System.Windows.Media;
using KeepShell.Bootstrap;

namespace PoproshaykaBot.Wpf.Infrastructure;

public static class StreamSegmentPalette
{
    public const int SlotCount = 6;
    public const int NeutralSlot = 5;

    private const string DataScaleStopsKey = "DataScale.Stops";
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    public static int IndexOf(string? game)
    {
        if (game is not { Length: > 0 })
        {
            return NeutralSlot;
        }

        var hash = FnvOffsetBasis;

        foreach (var symbol in game)
        {
            hash = (hash ^ char.ToLowerInvariant(symbol)) * FnvPrime;
        }

        return (int)(hash % (SlotCount - 1));
    }

    public static bool TryGetSlotColor(int paletteIndex, out Color color)
    {
        if (paletteIndex < 0 || paletteIndex >= NeutralSlot)
        {
            color = default;
            return false;
        }

        var position = NeutralSlot > 1 ? paletteIndex / (double)(NeutralSlot - 1) : 0d;
        return TrySampleDataScale(position, out color);
    }

    public static bool TrySampleDataScale(double position, out Color color)
    {
        var palette = ThemeManager.FindPaletteDictionary();

        if (palette?[DataScaleStopsKey] is not GradientStopCollection { Count: > 0 } stops)
        {
            color = default;
            return false;
        }

        color = SampleStops(stops, position);
        return true;
    }

    internal static Color SampleStops(GradientStopCollection stops, double position)
    {
        var ordered = stops.OrderBy(static stop => stop.Offset).ToArray();
        var clamped = Math.Clamp(position, 0d, 1d);

        if (ordered.Length == 1 || clamped <= ordered[0].Offset)
        {
            return ordered[0].Color;
        }

        if (clamped >= ordered[^1].Offset)
        {
            return ordered[^1].Color;
        }

        for (var i = 0; i < ordered.Length - 1; i++)
        {
            var left = ordered[i];
            var right = ordered[i + 1];

            if (clamped > right.Offset)
            {
                continue;
            }

            var span = right.Offset - left.Offset;

            if (span <= 0d)
            {
                return right.Color;
            }

            var t = (clamped - left.Offset) / span;

            return Color.FromArgb(
                LerpChannel(left.Color.A, right.Color.A, t),
                LerpChannel(left.Color.R, right.Color.R, t),
                LerpChannel(left.Color.G, right.Color.G, t),
                LerpChannel(left.Color.B, right.Color.B, t));
        }

        return ordered[^1].Color;
    }

    private static byte LerpChannel(byte from, byte to, double t) =>
        (byte)Math.Clamp(Math.Round(from + (to - from) * t), 0d, 255d);
}
