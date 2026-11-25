using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using KeepShell.Bootstrap;

namespace PoproshaykaBot.Wpf.Infrastructure;

public enum DataScaleSlotRole
{
    None = 0,
    Stripe = 1,
    SoftFill = 2,
    Foreground = 3,
}

public static class DataScaleBrushBehavior
{
    private const byte SoftAlpha = 46;

    private static readonly DependencyProperty ThemeListenerProperty = DependencyProperty.RegisterAttached(
        "ThemeListener",
        typeof(ThemeChangeRelay),
        typeof(DataScaleBrushBehavior));

    public static readonly DependencyProperty MeterLevelProperty = DependencyProperty.RegisterAttached(
        "MeterLevel",
        typeof(double),
        typeof(DataScaleBrushBehavior),
        new PropertyMetadata(double.NaN, OnMeterPropertyChanged));

    public static readonly DependencyProperty MeterSeverityProperty = DependencyProperty.RegisterAttached(
        "MeterSeverity",
        typeof(string),
        typeof(DataScaleBrushBehavior),
        new PropertyMetadata(null, OnMeterPropertyChanged));

    public static readonly DependencyProperty SegmentSlotProperty = DependencyProperty.RegisterAttached(
        "SegmentSlot",
        typeof(int),
        typeof(DataScaleBrushBehavior),
        new PropertyMetadata(-1, OnSegmentPropertyChanged));

    public static readonly DependencyProperty SlotRoleProperty = DependencyProperty.RegisterAttached(
        "SlotRole",
        typeof(DataScaleSlotRole),
        typeof(DataScaleBrushBehavior),
        new PropertyMetadata(DataScaleSlotRole.None, OnSegmentPropertyChanged));

    public static double GetMeterLevel(DependencyObject element) => (double)element.GetValue(MeterLevelProperty);

    public static void SetMeterLevel(DependencyObject element, double value) => element.SetValue(MeterLevelProperty, value);

    public static string? GetMeterSeverity(DependencyObject element) => (string?)element.GetValue(MeterSeverityProperty);

    public static void SetMeterSeverity(DependencyObject element, string? value) => element.SetValue(MeterSeverityProperty, value);

    public static int GetSegmentSlot(DependencyObject element) => (int)element.GetValue(SegmentSlotProperty);

    public static void SetSegmentSlot(DependencyObject element, int value) => element.SetValue(SegmentSlotProperty, value);

    public static DataScaleSlotRole GetSlotRole(DependencyObject element) => (DataScaleSlotRole)element.GetValue(SlotRoleProperty);

    public static void SetSlotRole(DependencyObject element, DataScaleSlotRole value) => element.SetValue(SlotRoleProperty, value);

    private static void OnMeterPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            EnsureThemeRelay(element, ApplyMeter);
            ApplyMeter(element);
        }
    }

    private static void OnSegmentPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            EnsureThemeRelay(element, ApplySegment);
            ApplySegment(element);
        }
    }

    private static void ApplyMeter(FrameworkElement element)
    {
        var level = GetMeterLevel(element);

        if (double.IsFinite(level) && StreamSegmentPalette.TrySampleDataScale(level, out var color))
        {
            element.SetValue(Control.ForegroundProperty, CreateFrozenBrush(color));
            return;
        }

        element.SetResourceReference(Control.ForegroundProperty, ResolveMeterFallbackKey(GetMeterSeverity(element)));
    }

    private static void ApplySegment(FrameworkElement element)
    {
        var role = GetSlotRole(element);

        if (role == DataScaleSlotRole.None)
        {
            return;
        }

        var slot = GetSegmentSlot(element);

        if (StreamSegmentPalette.TryGetSlotColor(slot, out var color))
        {
            switch (role)
            {
                case DataScaleSlotRole.Stripe:
                    element.SetValue(Border.BackgroundProperty, CreateFrozenBrush(color));
                    break;
                case DataScaleSlotRole.SoftFill:
                    element.SetValue(Border.BackgroundProperty, CreateFrozenBrush(Color.FromArgb(SoftAlpha, color.R, color.G, color.B)));
                    break;
                case DataScaleSlotRole.Foreground:
                    element.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Primary");
                    break;
            }

            return;
        }

        switch (role)
        {
            case DataScaleSlotRole.Stripe:
            case DataScaleSlotRole.SoftFill:
                element.ClearValue(Border.BackgroundProperty);
                break;
            case DataScaleSlotRole.Foreground:
                element.ClearValue(TextBlock.ForegroundProperty);
                break;
        }
    }

    private static string ResolveMeterFallbackKey(string? severity) => severity?.ToLowerInvariant() switch
    {
        "error" or "fatal" or "critical" => "State.ErrorText",
        "warning" or "warn" => "State.WarningText",
        "success" or "ok" => "State.SuccessText",
        "info" or "information" => "State.InfoText",
        _ => "Meter.Default",
    };

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void EnsureThemeRelay(FrameworkElement element, Action<FrameworkElement> apply)
    {
        if (element.GetValue(ThemeListenerProperty) is ThemeChangeRelay existing)
        {
            existing.AttachIfLoaded(element);
            return;
        }

        var relay = new ThemeChangeRelay(element, apply);
        element.SetValue(ThemeListenerProperty, relay);
        relay.AttachIfLoaded(element);
    }

    private sealed class ThemeChangeRelay
    {
        private readonly WeakReference<FrameworkElement> _element;
        private readonly Action<FrameworkElement> _apply;
        private bool _subscribed;

        public ThemeChangeRelay(FrameworkElement element, Action<FrameworkElement> apply)
        {
            _element = new WeakReference<FrameworkElement>(element);
            _apply = apply;
            element.Loaded += OnLoaded;
            element.Unloaded += OnUnloaded;
        }

        public void AttachIfLoaded(FrameworkElement element)
        {
            if (element.IsLoaded)
            {
                Subscribe();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Subscribe();

            if (sender is FrameworkElement element)
            {
                _apply(element);
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Unsubscribe();
        }

        private void OnThemeChanged(object? sender, ThemeDefinition theme)
        {
            if (!_element.TryGetTarget(out var element))
            {
                Unsubscribe();
                return;
            }

            _apply(element);
        }

        private void Subscribe()
        {
            if (_subscribed)
            {
                return;
            }

            ThemeManager.Changed += OnThemeChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            ThemeManager.Changed -= OnThemeChanged;
            _subscribed = false;
        }
    }
}
