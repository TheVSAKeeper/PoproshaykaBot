using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.Views.Controls;
using System.Windows;
using System.Windows.Data;

namespace PoproshaykaBot.Wpf.Tests.Multiplatform;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class PlatformIconViewTests
{
    [Test]
    public void Icon_uses_neutral_tooltip_for_unbranded_platform()
    {
        var icon = new PlatformIconView();
        Assert.That(icon.ToolTip, Is.EqualTo("Twitch"));

        icon.Platform = StreamingPlatform.VkLive;
        Assert.That(icon.ToolTip, Is.EqualTo("Площадка"));
    }

    [Test]
    public void Icon_keeps_tooltip_binding_when_platform_changes()
    {
        var icon = new PlatformIconView();
        icon.SetBinding(FrameworkElement.ToolTipProperty, new Binding { Source = "VK Видео Live" });

        icon.Platform = StreamingPlatform.VkLive;

        Assert.Multiple(() =>
        {
            Assert.That(icon.ToolTip, Is.EqualTo("VK Видео Live"));
            Assert.That(BindingOperations.IsDataBound(icon, FrameworkElement.ToolTipProperty), Is.True);
        });
    }
}
