using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Infrastructure.Converters;

public sealed class StreamStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var state = ResolveState(value);

        if (Application.Current is null)
        {
            return DependencyProperty.UnsetValue;
        }

        var variant = parameter as string;

        var suffix = string.Equals(variant, "Soft", StringComparison.OrdinalIgnoreCase) ? "Soft"
            : string.Equals(variant, "Text", StringComparison.OrdinalIgnoreCase) ? "Text"
            : string.Empty;

        var key = state switch
        {
            TriState.Positive => "State.Success" + suffix,
            TriState.Negative => "State.Error" + suffix,
            _ => "Fg.Muted",
        };

        return Application.Current.TryFindResource(key) as Brush ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static TriState ResolveState(object? value)
    {
        if (value is bool flag)
        {
            return flag ? TriState.Positive : TriState.Neutral;
        }

        var name = (value as Enum)?.ToString() ?? value as string;

        return name?.ToLowerInvariant() switch
        {
            "online" or "live" or "connected" or "active" or "running" or "started" or "success" or "ok" => TriState.Positive,
            "error" or "failed" or "fault" or "faulted" or "stuck" => TriState.Negative,
            _ => TriState.Neutral,
        };
    }

    private enum TriState
    {
        None = 0,
        Neutral = 1,
        Positive = 2,
        Negative = 3,
    }
}
