using KeepShell.Testing;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.Views.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class RanksSettingsSectionViewTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
    ];

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
    }

    [TestCase("", "КОРОЛЬ", "5000", 0)]
    [TestCase("♔", " ", "5000", 1)]
    [TestCase("♔", "КОРОЛЬ", "-1", 2)]
    public void Рамка_поля_с_ошибкой_красится_цветом_ошибки_а_соседние_нет(string emoji, string name, string threshold, int invalidField)
    {
        var section = new RanksSettingsSectionViewModel();
        section.Load([new("♔", "КОРОЛЬ", 5000), new("♟", "ПЕШКА", 0)]);

        var entry = section.Entries[0];
        entry.Emoji = emoji;
        entry.Name = name;
        entry.ThresholdText = threshold;

        var view = new RanksSettingsSectionView { DataContext = section };
        view.Measure(new(900, 600));
        view.Arrange(new(0, 0, 900, 600));
        view.UpdateLayout();
        view.UpdateLayout();

        var fields = Descendants<TextBox>(view).Take(3).ToArray();
        var error = (SolidColorBrush)view.FindResource("State.Error");

        Assert.That(fields, Has.Length.EqualTo(3));

        Assert.Multiple(() =>
        {
            for (var index = 0; index < fields.Length; index++)
            {
                var color = (fields[index].BorderBrush as SolidColorBrush)?.Color;

                if (index == invalidField)
                {
                    Assert.That(color, Is.EqualTo(error.Color), $"Поле {index} с ошибкой");
                }
                else
                {
                    Assert.That(color, Is.Not.EqualTo(error.Color), $"Поле {index} без ошибки");
                }
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
