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

    public PlatformIconView()
    {
        InitializeComponent();
        ApplyVisual();
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
        }
    }

    private void ApplyVisual()
    {
        var platform = Platform;
        var descriptor = PlatformVisuals.Get(platform);
        IconPath.Data = PlatformVisuals.ResolveGeometry(platform, this);
        IconPath.Fill = PlatformVisuals.ResolveBrandBrush(platform, this);
        ToolTip = descriptor.DisplayName;
    }
}
