using KeepShell.Testing;
using PoproshaykaBot.Wpf.Views.Tiles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
public class BroadcastProfileCardMetricsTests
{
    private const double CardChrome = 30;

    [TestCase(2, BroadcastProfilesTileView.TwoColumnsWidth)]
    [TestCase(3, BroadcastProfilesTileView.ThreeColumnsWidth)]
    [Apartment(ApartmentState.STA)]
    public void Панель_кнопок_карточки_влезает_в_колонку_на_пороге(int columns, double threshold)
    {
        var buttons = MeasureButtons("#1000");
        var card = (threshold / columns) - CardChrome;

        Assert.That(card, Is.GreaterThanOrEqualTo(buttons),
            $"На пороге {threshold} DIP карточка получает {card:F1} DIP, а кнопкам «Применить» и счётчику нужно {buttons:F1} – панель перенесётся на вторую строку и поднимет высоту всех карточек ряда");
    }

    private static double MeasureButtons(string badge)
    {
        PackScheme.Ensure();

        var root = new Grid();

        foreach (var source in new[]
                 {
                     "pack://application:,,,/KeepShell;component/Resources/Themes/Dark.xaml",
                     "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
                     "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
                 })
        {
            root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(source) });
        }

        var secondary = (Style)root.FindResource("Button.Secondary");

        var counter = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new(12, 4, 0, 0),
        };

        counter.Children.Add(new Button
        {
            Content = "–",
            Style = secondary,
            Padding = new(8, 2, 8, 2),
        });

        counter.Children.Add(new TextBlock
        {
            Text = badge,
            FontFamily = (FontFamily)root.FindResource("Font.Mono"),
            FontSize = (double)root.FindResource("Font.Size.S"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new(8, 0, 8, 0),
        });

        counter.Children.Add(new Button
        {
            Content = "+",
            Style = secondary,
            Padding = new(8, 2, 8, 2),
        });

        var row = new WrapPanel { Margin = new(0, 4, 0, 0) };

        row.Children.Add(new Button
        {
            Content = "Применить",
            Style = secondary,
            Margin = new(0, 4, 0, 0),
        });

        row.Children.Add(counter);

        root.Children.Add(row);
        root.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        root.UpdateLayout();

        return row.DesiredSize.Width;
    }
}
