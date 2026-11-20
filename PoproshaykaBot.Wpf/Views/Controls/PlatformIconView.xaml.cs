using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Controls;

public partial class PlatformIconView : UserControl
{
    public static readonly DependencyProperty PlatformProperty = DependencyProperty.Register(
        nameof(Platform),
        typeof(StreamingPlatform),
        typeof(PlatformIconView),
        new PropertyMetadata(StreamingPlatform.Twitch, OnPlatformChanged));

    static PlatformIconView()
    {
        ToolTipProperty.OverrideMetadata(typeof(PlatformIconView), new FrameworkPropertyMetadata(null, null, CoerceToolTip));
    }

    public PlatformIconView()
    {
        InitializeComponent();
        ApplyVisual();
        CoerceValue(ToolTipProperty);
    }

    public StreamingPlatform Platform
    {
        get => (StreamingPlatform)GetValue(PlatformProperty);
        set => SetValue(PlatformProperty, value);
    }

    private static void OnPlatformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlatformIconView view)
        {
            view.ApplyVisual();
            view.CoerceValue(ToolTipProperty);
        }
    }

    private static object CoerceToolTip(DependencyObject d, object? value) =>
        value ?? PlatformVisuals.GetDisplayName(((PlatformIconView)d).Platform);

    private void ApplyVisual()
    {
        var platform = Platform;
        IconPath.Data = PlatformVisuals.ResolveGeometry(platform, this);
        IconPath.Fill = PlatformVisuals.ResolveBrandBrush(platform, this);
    }
}
