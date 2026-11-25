using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PoproshaykaBot.Wpf.Infrastructure.Converters;

public sealed class CornerRadiusMaskConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not CornerRadius radius)
        {
            return default(CornerRadius);
        }

        var (side, inset) = ParseParameter(parameter as string);
        var topLeft = Math.Max(0d, radius.TopLeft - inset);
        var topRight = Math.Max(0d, radius.TopRight - inset);
        var bottomRight = Math.Max(0d, radius.BottomRight - inset);
        var bottomLeft = Math.Max(0d, radius.BottomLeft - inset);

        return side switch
        {
            "left" => new CornerRadius(topLeft, 0d, 0d, bottomLeft),
            "top" => new CornerRadius(topLeft, topRight, 0d, 0d),
            "right" => new CornerRadius(0d, topRight, bottomRight, 0d),
            "bottom" => new CornerRadius(0d, 0d, bottomRight, bottomLeft),
            _ => new CornerRadius(topLeft, topRight, bottomRight, bottomLeft),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static (string Side, double Inset) ParseParameter(string? parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter))
        {
            return (string.Empty, 0d);
        }

        var parts = parameter.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var side = parts.Length > 0 ? parts[0].ToLowerInvariant() : string.Empty;
        var inset = parts.Length > 1 && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0d;

        return (side, inset);
    }
}
