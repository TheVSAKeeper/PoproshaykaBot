using KeepShell.Testing;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views.Tiles;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class BroadcastProfileCardMetricsTests
{
    private const double CardMargin = 8;
    private const double CardInnerChrome = 10 + 10 + 1 + 1;
    private const double CardChrome = CardMargin + CardInnerChrome;
    private const double CardHeightBudget = 124;

    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/TableStyles.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/RatingStyles.xaml",
    ];

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
    }

    [TestCase(2, BroadcastProfilesTileView.TwoColumnsWidth)]
    [TestCase(3, BroadcastProfilesTileView.ThreeColumnsWidth)]
    public void Панель_действий_влезает_в_колонку_на_пороге(int columns, double threshold)
    {
        var card = Card(out var actions);
        var available = (threshold / columns) - CardChrome;

        Measure(card, available);

        Assert.That(available, Is.GreaterThanOrEqualTo(actions.DesiredSize.Width),
            $"На пороге {threshold} DIP карточка получает {available:F1} DIP, а её панели действий нужно {actions.DesiredSize.Width:F1} – кнопки перенесутся на вторую строку и панель закроет сведения карточки");
    }

    [Test]
    public void Карточка_в_покое_укладывается_в_бюджет_высоты()
    {
        var card = Card(out _);

        Measure(card, (BroadcastProfilesTileView.ThreeColumnsWidth / 3) - CardChrome);


        Assert.That(card.ActualHeight, Is.LessThanOrEqualTo(CardHeightBudget),
            $"Карточка профиля со всеми сведениями занимает {card.ActualHeight:F1} DIP при бюджете {CardHeightBudget} – при десятке профилей плитка живёт прокруткой");
    }

    [Test]
    public void Панель_действий_видна_сразу_и_не_перекрывает_сведения()
    {
        var card = Card(out var actions);
        var details = (FrameworkElement)card.FindName("CardDetails");

        Measure(card, (BroadcastProfilesTileView.ThreeColumnsWidth / 3) - CardChrome);

        var detailsBottom = details.TranslatePoint(new(0, details.ActualHeight), card).Y;
        var actionsTop = actions.TranslatePoint(new(0, 0), card).Y;

        Assert.Multiple(() =>
        {
            Assert.That(actions.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(actions.Opacity, Is.EqualTo(1),
                "Кнопка «Применить» должна быть сразу видна на карточке без наведения мыши");
            Assert.That(actions.IsHitTestVisible, Is.True);
            Assert.That(actionsTop, Is.GreaterThanOrEqualTo(detailsBottom - 0.5),
                $"Панель действий ({actionsTop:F1}) не должна перекрывать сведения карточки ({detailsBottom:F1})");
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Панель_действий_не_заходит_на_строку_имени(bool withDetails)
    {
        var card = Card(out var actions, withDetails, isActive: true);
        var header = (FrameworkElement)card.FindName("CardHeader");

        Measure(card, (BroadcastProfilesTileView.ThreeColumnsWidth / 3) - CardChrome);

        var headerBottom = header.TranslatePoint(new(0, header.ActualHeight), card).Y;
        var actionsTop = actions.TranslatePoint(new(0, 0), card).Y;

        Assert.That(actionsTop, Is.GreaterThanOrEqualTo(headerBottom),
            $"Панель действий начинается на {actionsTop:F1} DIP, а строка имени кончается на {headerBottom:F1} – панель закрывает бейдж «#N»");
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Применённая_карточка_отмечена_кромкой_без_смены_геометрии(bool isActive)
    {
        var card = Card(out _, isActive: isActive);
        var edge = (FrameworkElement)card.FindName("ActiveEdge");

        Measure(card, (BroadcastProfilesTileView.ThreeColumnsWidth / 3) - CardChrome);

        var plain = Card(out _);
        Measure(plain, (BroadcastProfilesTileView.ThreeColumnsWidth / 3) - CardChrome);

        Assert.Multiple(() =>
        {
            Assert.That(edge.Visibility == Visibility.Visible, Is.EqualTo(isActive),
                "Применённый профиль отмечает только кромка слева – бейджа «Активен» и заливки у карточки больше нет");
            Assert.That(card.ActualHeight, Is.EqualTo(plain.ActualHeight),
                "Отметка применённого профиля не меняет высоту карточки");
        });
    }

    private static DataTemplate Template()
    {
        return (DataTemplate)new BroadcastProfilesTileView().Resources["ProfileCardTemplate"];
    }

    private static Border Card(out FrameworkElement actions, bool withDetails = true, bool isActive = false)
    {
        var template = Template();
        var card = (Border)template.LoadContent();

        card.DataContext = Item(withDetails, isActive);
        actions = (FrameworkElement)card.FindName("CardActions");

        return card;
    }

    private static void Measure(Border card, double content)
    {
        var width = content + CardInnerChrome + CardMargin;
        var host = new Grid { Width = width };

        host.Children.Add(card);
        host.Measure(new(width, double.PositiveInfinity));
        host.Arrange(new(0, 0, width, host.DesiredSize.Height));
        host.UpdateLayout();
    }

    private static BroadcastProfileItemViewModel Item(bool withDetails, bool isActive)
    {
        var profile = withDetails
            ? new BroadcastProfile
            {
                Name = "Утренний стрим",
                Title = "Проходим сюжет, серия {n}",
                GameName = "Software and Game Development",
                BroadcasterLanguage = "ru",
                Tags = ["РусскийЯзык"],
                CurrentNumber = 1000,
            }
            : new BroadcastProfile { Name = "Утренний стрим" };

        return new(
            profile,
            isActive,
            hasDrift: withDetails,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => { },
            _ => { },
            [new(StreamingPlatform.Twitch, "Twitch")]);
    }
}
