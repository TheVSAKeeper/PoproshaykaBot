using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.Views;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests.Users;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class UserRatingColumnsTests
{
    private const double PageHeight = 640;
    private const double NarrowestPage = 600;
    private const double WidthStep = 2;
    private const double MeasuredSlackCeiling = 8;
    private const double AvatarSize = 24;
    private const double RowHeight = 40;
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
                Assert.That(atThreshold.Slack, Is.LessThan(scale == FontScaleManager.DefaultScale ? MeasuredSlackCeiling : double.MaxValue),
                    "При 1.0 порог мерен впритык: запас шире значит, что раскладка строки поменялась и порог пора перемерить");
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

    [TestCase(1.0, "1")]
    [TestCase(1.6, "1")]
    [TestCase(1.0, "2")]
    [TestCase(1.6, "2")]
    [TestCase(1.6, "3")]
    public void Лестница_рангов_влезает_в_инспектор_на_полу_и_не_режет_названий(double scale, string userId)
    {
        try
        {
            ApplyScale(scale);

            var page = CreatePage();
            var view = new UserStatisticsPageView { DataContext = page };

            Assert.That(page.TrySelect(userId), Is.True);

            page.IsRankLadderExpanded = true;
            Arrange(view, NarrowestPage);

            var ladder = (ItemsControl)view.FindName("RankLadder")!;
            var names = Enumerable.Range(0, ladder.Items.Count)
                .Select(index => (ContentPresenter)ladder.ItemContainerGenerator.ContainerFromIndex(index)!)
                .SelectMany(FindChildren<TextBlock>)
                .Where(text => text.TextTrimming != TextTrimming.None)
                .ToList();

            var clipped = names
                .Select(text => (text.Text, Slack: TextSlack(view, text)))
                .Where(item => item.Slack < 0)
                .Select(item => $"{item.Text} ({item.Slack:F1})")
                .ToList();

            var peers = UIElementAutomationPeer.CreatePeerForElement(ladder);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ladder.ActualWidth, Is.GreaterThan(0), "Лестница видна у выбранного");
                Assert.That(names, Has.Count.EqualTo(ladder.Items.Count), "У каждой ступени своё название");
                Assert.That(clipped, Is.Empty, $"Масштаб {scale}: названия ступеней режутся на полу инспектора");
                Assert.That(peers.GetName(), Is.EqualTo(page.SelectedRow!.RankLadder!.Summary));
                Assert.That(peers.GetChildren().Select(child => child.GetName()),
                    Is.EqualTo(page.RankLadderSteps.Select(step => step.AutomationName)),
                    "Ступень звучит своим названием, порогом и состоянием, а не именем типа");
            }
        }
        finally
        {
            ApplyScale(FontScaleManager.DefaultScale);
        }
    }

    [TestCase(1.0)]
    [TestCase(1.6)]
    public void Кружок_стоит_перед_ником_а_заголовок_Участник_над_ником(double scale)
    {
        try
        {
            ApplyScale(scale);

            var view = new UserStatisticsPageView { DataContext = CreatePage() };
            Arrange(view, 1000);

            var header = (Grid)view.FindName("RatingHeader")!;
            var list = (ListBox)view.FindName("RatingList")!;
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var name = ((UserStatisticsRowViewModel)row.DataContext).Name;

            var headerText = FindChildren<TextBlock>(header).Single(text => text.Text == "Участник");
            var nameText = FindChildren<TextBlock>(row).Single(text => text.Text == name);
            var avatar = FindChildren<ContentPresenter>(row).Single(presenter => presenter.Content is UserAvatarViewModel);

            var headerLeft = headerText.TranslatePoint(new(0, 0), view).X;
            var nameLeft = nameText.TranslatePoint(new(0, 0), view).X;
            var avatarCenter = avatar.TranslatePoint(new(0, avatar.ActualHeight / 2), view).Y;
            var nameCenter = nameText.TranslatePoint(new(0, nameText.ActualHeight / 2), view).Y;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(avatar.ActualWidth, Is.EqualTo(AvatarSize));
                Assert.That(avatar.ActualHeight, Is.EqualTo(AvatarSize));
                Assert.That(row.ActualHeight, Is.EqualTo(RowHeight), "кружок не поднимает строку выше 40");
                Assert.That(avatar.TranslatePoint(new(avatar.ActualWidth, 0), view).X, Is.LessThan(nameLeft),
                    "кружок стоит перед ником");
                Assert.That(headerLeft, Is.EqualTo(nameLeft).Within(0.5), "заголовок «Участник» стоит над ником, а не над кружком");
                Assert.That(avatarCenter, Is.EqualTo(nameCenter).Within(1), "кружок выровнен по середине ника");
            }
        }
        finally
        {
            ApplyScale(FontScaleManager.DefaultScale);
        }
    }

    private static double TextSlack(FrameworkElement view, TextBlock text)
    {
        text.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var natural = text.DesiredSize.Width;
        text.InvalidateMeasure();
        view.UpdateLayout();

        return text.ActualWidth - natural;
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindChildren<T>(child))
            {
                yield return nested;
            }
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

        statistics.SeedPlatform(StreamingPlatform.Twitch,
        [
            new() { UserId = "1", Name = "qwertyqwertyqwerty", MessageCount = 75_123, BonusPoints = 12_345 },
            new() { UserId = "2", Name = "каторжник", MessageCount = 10, PenaltyPoints = 500 },
            new() { UserId = "3", Name = "новичок", MessageCount = 3 },
        ]);

        return new(
            statistics,
            null!,
            new UserRankService(settingsManager),
            new UserPointsManagementService(statistics, settingsManager, eventBus, NullLogger<UserPointsManagementService>.Instance),
            new FakeChannelProvider(),
            settingsManager,
            null!,
            eventBus,
            new MemorySettings(),
            FakeAvatarCache.CreateProvider());
    }

    private sealed class FakeChannelProvider : IChannelProvider
    {
        public string? Channel => "test";
    }
}
