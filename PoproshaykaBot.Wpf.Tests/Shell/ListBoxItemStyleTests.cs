using System.Windows;
using System.Windows.Controls;
using KeepShell.Bootstrap;
using PoproshaykaBot.Wpf.Tests.Support;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class ListBoxItemStyleTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/TableStyles.xaml",
    ];

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
    }

    [Test]
    public void Неявный_стиль_ListBoxItem_задаёт_VerticalContentAlignment_без_ошибок_привязки()
    {
        var style = (Style)Application.Current.FindResource(typeof(ListBoxItem));
        var verticalSetter = style.Setters
            .OfType<Setter>()
            .FirstOrDefault(s => s.Property == Control.VerticalContentAlignmentProperty);

        Assert.That(verticalSetter, Is.Not.Null);
        Assert.That(verticalSetter!.Value, Is.EqualTo(VerticalAlignment.Center));

        using var bindingErrors = BindingErrorSink.Attach();
        bindingErrors.Clear();

        var listBox = new ListBox
        {
            ItemsSource = new[] { "Первый", "Второй", "Третий" },
            Width = 260,
            Height = 160,
        };
        listBox.SetValue(VirtualizingPanel.IsVirtualizingProperty, true);
        listBox.SetValue(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling);

        var host = new ContentControl { Content = listBox };
        host.Measure(new(260, 160));
        host.Arrange(new(0, 0, 260, 160));
        host.UpdateLayout();

        var detachedItem = new ListBoxItem { Content = "Вне списка", Style = style };
        detachedItem.Measure(new(200, 40));
        detachedItem.Arrange(new(0, 0, 200, 40));
        detachedItem.UpdateLayout();

        Assert.That(bindingErrors.Count, Is.Zero,
            () => string.Join(Environment.NewLine, bindingErrors.Records.Select(static r => r.Message)));
    }
}
