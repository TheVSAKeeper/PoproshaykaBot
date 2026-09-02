using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Shapes;

namespace PoproshaykaBot.Wpf.Infrastructure.Behaviors;

public static class StreamStatusBrushBehavior
{
    private const string NeutralKey = "Fg.Muted";

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.RegisterAttached(
        "Foreground",
        typeof(object),
        typeof(StreamStatusBrushBehavior),
        new PropertyMetadata(null, OnForegroundChanged));

    public static readonly DependencyProperty FillProperty = DependencyProperty.RegisterAttached(
        "Fill",
        typeof(object),
        typeof(StreamStatusBrushBehavior),
        new PropertyMetadata(null, OnFillChanged));

    public static object? GetForeground(DependencyObject element)
    {
        return element.GetValue(ForegroundProperty);
    }

    public static void SetForeground(DependencyObject element, object? value)
    {
        element.SetValue(ForegroundProperty, value);
    }

    public static object? GetFill(DependencyObject element)
    {
        return element.GetValue(FillProperty);
    }

    public static void SetFill(DependencyObject element, object? value)
    {
        element.SetValue(FillProperty, value);
    }

    private static void OnForegroundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var target = d switch
        {
            Control => Control.ForegroundProperty,
            TextBlock => TextBlock.ForegroundProperty,
            _ => TextElement.ForegroundProperty,
        };

        var key = ResolveKey(e.NewValue);

        Apply(d, target, key is null ? NeutralKey : key + "Text");
    }

    private static void OnFillChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Apply(d, Shape.FillProperty, ResolveKey(e.NewValue) ?? NeutralKey);
    }

    private static void Apply(DependencyObject element, DependencyProperty property, string key)
    {
        if (element is FrameworkElement frameworkElement)
        {
            frameworkElement.SetResourceReference(property, key);
        }
    }

    private static string? ResolveKey(object? value)
    {
        if (value is bool flag)
        {
            return flag ? "State.Success" : null;
        }

        var name = (value as Enum)?.ToString() ?? value as string;

        return name?.ToLowerInvariant() switch
        {
            "online" or "live" or "connected" or "active" or "running" or "started" or "success" or "ok" => "State.Success",
            "error" or "failed" or "fault" or "faulted" or "stuck" => "State.Error",
            _ => null,
        };
    }
}
