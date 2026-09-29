using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Tests.Users;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class UserRatingColumnsTests
{
    private const double PageHeight = 640;
    private const double NarrowestPage = 600;
    private const double WidthStep = 2;
    private const int NameColumn = 1;
    private const int RankColumn = 2;
    private const string FontSizePrefix = "Font.Size.";

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

    private string _directory = null!;

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-rating-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestCase(1.0)]
    [TestCase(1.6)]
    public void Ранг_виден_с_порога_ширины_списка_и_ник_при_нём_не_режется(double scale)
    {
        try
        {
            ApplyScale(scale);

            var view = new UserStatisticsPageView { DataContext = CreatePage() };
            var threshold = UserStatisticsPageView.RankColumnListWidth * scale;
            var width = NarrowestPage;

            Arrange(view, width);

            while (ListWidth(view) < threshold)
            {
                width += WidthStep;
                Arrange(view, width);
            }

            var atThreshold = (view.ShowsRankColumn, Slack: NameSlack(view), Rank: RankWidth(view));

            Arrange(view, width - WidthStep);

            var below = (view.ShowsRankColumn, Slack: NameSlack(view), Rank: RankWidth(view));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(atThreshold.ShowsRankColumn, Is.True, "На пороге колонка «Ранг» на месте");
                Assert.That(atThreshold.Rank, Is.GreaterThan(0));
                Assert.That(atThreshold.Slack, Is.GreaterThanOrEqualTo(0),
                    $"Масштаб {scale}: на пороге заголовку «Участник» не хватает {-atThreshold.Slack:F1} DIP – порог занижен");
                Assert.That(below.ShowsRankColumn, Is.False, "Уже порога колонка «Ранг» скрыта");
                Assert.That(below.Rank, Is.Zero, "Скрытая колонка не держит ширину ни в шапке, ни в строках");
                Assert.That(below.Slack, Is.GreaterThanOrEqualTo(0));
            }
        }
        finally
        {
            ApplyScale(FontScaleManager.DefaultScale);
        }
    }

    private static void ApplyScale(double scale)
    {
        FontScaleManager.Apply(scale);

        var tokens = new ResourceDictionary { Source = new(Dictionaries[1]) };
        var resources = Application.Current.Resources;

        foreach (var key in tokens.Keys.OfType<string>().Where(key => key.StartsWith(FontSizePrefix, StringComparison.Ordinal)))
        {
            if (scale == FontScaleManager.DefaultScale)
            {
                resources.Remove(key);
            }
            else
            {
                resources[key] = (double)tokens[key] * scale;
            }
        }
    }

    private static void Arrange(FrameworkElement view, double width)
    {
        var area = new Size(width, PageHeight);

        view.Measure(area);
        view.Arrange(new(area));
        view.UpdateLayout();
    }

    private static double ListWidth(FrameworkElement view)
    {
        return ((FrameworkElement)view.FindName("RatingList")!).ActualWidth;
    }

    private static double RankWidth(FrameworkElement view)
    {
        return ((Grid)view.FindName("RatingHeader")!).ColumnDefinitions[RankColumn].ActualWidth;
    }

    private static double NameSlack(FrameworkElement view)
    {
        var header = (Grid)view.FindName("RatingHeader")!;
        var button = header.Children.OfType<Button>().Single(child => Grid.GetColumn(child) == NameColumn);

        button.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var natural = button.DesiredSize.Width;
        button.InvalidateMeasure();
        view.UpdateLayout();

        return header.ColumnDefinitions[NameColumn].ActualWidth - natural;
    }

    private UserStatisticsPageViewModel CreatePage()
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        var statistics = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        statistics.ReplaceAll(
        [
            new() { UserId = "1", Name = "qwertyqwertyqwerty", MessageCount = 75_123, BonusPoints = 12_345 },
            new() { UserId = "2", Name = "каторжник", MessageCount = 10, PenaltyPoints = 500 },
            new() { UserId = "3", Name = "новичок", MessageCount = 3 },
        ]);

        return new(
            statistics,
            null!,
            new UserRankService(settingsManager),
            new UserPointsManagementService(statistics, settingsManager, eventBus),
            new FakeChannelProvider(),
            settingsManager,
            null!,
            eventBus);
    }

    private sealed class FakeChannelProvider : IChannelProvider
    {
        public string? Channel => "test";
    }
}
