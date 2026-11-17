using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Controls;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using PoproshaykaBot.Wpf.Views;
using PoproshaykaBot.Wpf.Views.Dialogs;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class CompactLayoutTests
{
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
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        ApplyScale(FontScaleManager.DefaultScale);

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestCase(1.0)]
    [TestCase(1.6)]
    public void Диалоги_при_любом_масштабе_не_режут_подписи_и_кнопки(double scale)
    {
        ApplyScale(scale);

        using var game = new GameAutocompleteViewModel(new StubGameResolver(), new ManualUiDispatcher());
        using var profileVm = new BroadcastProfileEditDialogViewModel(
            game,
            null!,
            null!,
            NullLogger<BroadcastProfileEditDialogViewModel>.Instance);
        var profileView = new BroadcastProfileEditDialogView { DataContext = profileVm };
        Arrange(profileView, new(600, 620));

        var profileLabels = Descendants<TextBlock>(profileView)
            .Where(static t => t.Text is "Заголовок:" or "Серия №:" or "Игра:" or "Теги:" or "Язык:" or "Площадки:")
            .ToArray();

        Assert.That(profileLabels, Has.Length.EqualTo(6));
        foreach (var label in profileLabels)
        {
            var requiredWidth = label.DesiredSize.Width - label.Margin.Left - label.Margin.Right;
            Assert.That(label.ActualWidth, Is.GreaterThanOrEqualTo(requiredWidth - 0.5),
                $"Подпись «{label.Text}» обрезана при scale={scale}");
        }

        var termVm = new PointTermDialogViewModel();
        var termView = new PointTermDialogView { DataContext = termVm };
        Arrange(termView, new(480, 420));

        var termLabels = Descendants<TextBlock>(termView)
            .Where(static t => t.Text is "Единственное:" or "От 2 до 4:" or "Множественное:")
            .ToArray();

        Assert.That(termLabels, Has.Length.EqualTo(3));
        foreach (var label in termLabels)
        {
            var requiredWidth = label.DesiredSize.Width - label.Margin.Left - label.Margin.Right;
            Assert.That(label.ActualWidth, Is.GreaterThanOrEqualTo(requiredWidth - 0.5),
                $"Подпись «{label.Text}» обрезана при scale={scale}");
        }
    }

    [Test]
    public void Баннер_онбординга_переносит_текст_и_не_подрезает_кнопку()
    {
        ApplyScale(1.6);

        var data = new
        {
            IsVisible = true,
            Text = "Для работы бота подключите канал Twitch, настройте веб-сервер и авторизуйте аккаунты.",
            OpenWizardCommand = (System.Windows.Input.ICommand?)null,
        };
        var banner = new OnboardingBannerView { DataContext = data };
        Arrange(banner, new(720, 220));

        var text = Descendants<TextBlock>(banner).First(t => t.Text == data.Text);
        var singleLine = new TextBlock { Text = "А", FontSize = text.FontSize, FontFamily = text.FontFamily };
        singleLine.Measure(new(double.PositiveInfinity, double.PositiveInfinity));
        var button = Descendants<Button>(banner).First(b => Equals(b.Content, "Открыть мастер"));

        Assert.Multiple(() =>
        {
            Assert.That(text.ActualHeight, Is.GreaterThan(singleLine.DesiredSize.Height * 1.5));
            Assert.That(button.ActualWidth, Is.GreaterThanOrEqualTo(button.DesiredSize.Width - button.Margin.Left - button.Margin.Right - 0.5));
        });
    }

    [Test]
    public void Пользователи_при_1024x640_оставляют_не_менее_четырёх_строк_и_не_режут_инспектор()
    {
        ApplyScale(1.0);
        var pageVm = CreateUsersPage(20);
        Assert.That(pageVm.TrySelectAt(0), Is.True);
        var view = new UserStatisticsPageView { DataContext = pageVm };
        Arrange(view, new(920, 430));

        var inspectorScroll = (ScrollViewer)view.FindName("OuterInspectorScroll")!;
        var dailyLabel = Descendants<TextBlock>(inspectorScroll).First(static t => t.Text == "сообщений в день");
        var bottomEdge = dailyLabel.TranslatePoint(new(0, dailyLabel.ActualHeight), inspectorScroll).Y;

        Assert.That(view.IsInspectorScrollCombined, Is.True);
        Assert.That(bottomEdge, Is.LessThanOrEqualTo(inspectorScroll.ViewportHeight + 0.5),
            "При 1024x640 @ 1.0 подпись «сообщений в день» не должна обрезаться краем скролла инспектора");

        ApplyScale(1.6);
        Arrange(view, new(920, 350));

        var ratingList = (ListBox)view.FindName("RatingList")!;
        Assert.Multiple(() =>
        {
            Assert.That(pageVm.ShowExpandedSummary, Is.False);
            Assert.That(ratingList.ActualHeight, Is.GreaterThanOrEqualTo(150),
                "При 1024x640 @ 1.6 с баннером списку пользователей должно оставаться минимум ~4 строки");
        });
    }

    [Test]
    public void История_стримов_переходит_в_две_колонки_при_1216_и_держит_строки_при_1024x640_1_6()
    {
        var pageVm = CreateStreamsPage(20);
        var view = new StreamHistoryPageView { DataContext = pageVm };

        ApplyScale(1.0);
        Arrange(view, new(1216, 680));

        var pageGrid = (Grid)view.FindName("PageGrid")!;
        Assert.That(pageGrid.ColumnDefinitions.Count, Is.EqualTo(3),
            "При 1280x800 со свёрнутым рейлом (1216 DIP) страница должна идти в две колонки");

        var baseFontM = (double)Application.Current.Resources["Font.Size.M"];
        ApplyScale(1.6);
        var scaledFontM = (double)Application.Current.Resources["Font.Size.M"];
        Assert.That(scaledFontM, Is.EqualTo(baseFontM * 1.6).Within(0.01), "Масштаб шрифта 1.6 должен масштабировать Font.Size.* ресурсы");

        Arrange(view, new(920, 400));

        var sessionsList = (ListBox)view.FindName("SessionsList")!;
        var hiddenToggle = Descendants<System.Windows.Controls.Primitives.ToggleButton>(view)
            .First(b => b.Visibility == Visibility.Visible && Descendants<TextBlock>(b).Any(t => t.Text.StartsWith("Убранные:", StringComparison.Ordinal)));

        Assert.Multiple(() =>
        {
            Assert.That(pageVm.IsSummaryOpen, Is.False);
            Assert.That(pageVm.HasTrend, Is.False);
            Assert.That(sessionsList.ActualHeight, Is.GreaterThanOrEqualTo(120));
            Assert.That(hiddenToggle.ActualWidth, Is.GreaterThanOrEqualTo(hiddenToggle.DesiredSize.Width - hiddenToggle.Margin.Left - hiddenToggle.Margin.Right - 0.5));
        });

        foreach (var height in new[] { 430.0, 350.0 })
        {
            pageVm.SelectedRow = null;
            Arrange(view, new(744, height));
            var rowHeightNoSelection = GetFirstRowHeight(sessionsList);
            Assert.That(sessionsList.ActualHeight, Is.GreaterThanOrEqualTo((rowHeightNoSelection * 4) - 0.5),
                $"Без выбора при 744x{height} @ 1.6 список должен вмещать минимум 4 строки");

            Assert.That(pageVm.TrySelectAt(0), Is.True);
            Arrange(view, new(744, height));
            var rowHeightWithSelection = GetFirstRowHeight(sessionsList);
            Assert.That(sessionsList.ActualHeight, Is.GreaterThanOrEqualTo((rowHeightWithSelection * 4) - 0.5),
                $"С выбранной строкой при 744x{height} @ 1.6 список должен вмещать минимум 4 строки");
        }
    }

    private UserStatisticsPageViewModel CreateUsersPage(int count)
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        var statistics = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        var users = Enumerable.Range(1, count)
            .Select(i => new UserStatistics
            {
                Platform = StreamingPlatform.Twitch,
                UserId = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = $"VeryLongViewerNameForCompactLayoutTest_{i}",
                MessageCount = (ulong)(100 - i),
                BonusPoints = (ulong)((count - i + 1) * 100),
                PenaltyPoints = 5,
            })
            .ToList();
        statistics.SeedPlatform(StreamingPlatform.Twitch, users);

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

    private StreamHistoryPageViewModel CreateStreamsPage(int count)
    {
        var store = new BroadcastHistoryStore(
            null,
            Path.Combine(_directory, "broadcasts.json"),
            Path.Combine(_directory, "sessions.json"));

        var start = new DateTimeOffset(new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Local));
        for (var i = 0; i < count; i++)
        {
            store.TryAppend(BroadcastRecord.FromTwitchSession(new StreamSessionRecord
            {
                Channel = "channel",
                StartedAt = start.AddDays(i),
                EndedAt = start.AddDays(i).AddHours(3),
                Title = $"Стрим {i}",
                Game = i % 2 == 0 ? "Just Chatting" : "Software and Game Development",
                MessageCount = 100 + i,
                ChatterCount = 10,
                PeakViewers = 50 + i,
                AverageViewers = 25,
                IsHidden = i == 0,
            }));
        }

        var users = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var boxArt = new GameBoxArtProvider(new FakeBoxArtCache(), NullLogger<GameBoxArtProvider>.Instance);
        return new(store, users, new MemorySettings(), boxArt, new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance));
    }

    private static double GetFirstRowHeight(ListBox listBox)
    {
        return listBox.ItemContainerGenerator.ContainerFromIndex(0) is FrameworkElement item && item.ActualHeight > 0
            ? item.ActualHeight
            : 34;
    }

    private static void ApplyScale(double scale)
    {
        if (Application.Current is null)
        {
            return;
        }

        FontScaleManager.Apply(scale);

        var tokens = new ResourceDictionary { Source = new(Dictionaries[1]) };
        var resources = Application.Current.Resources;

        foreach (var key in tokens.Keys.OfType<string>().Where(static k => k.StartsWith("Font.Size.", StringComparison.Ordinal)))
        {
            if (Math.Abs(scale - FontScaleManager.DefaultScale) < 0.001)
            {
                resources[key] = (double)tokens[key];
            }
            else
            {
                resources[key] = (double)tokens[key] * scale;
            }
        }

        AppScaledSizes.Register(Application.Current);
    }

    private static void Arrange(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new(size));
        element.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
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

    private sealed class FakeChannelProvider : IChannelProvider
    {
        public string? Channel => "test";
    }

    private sealed class StubGameResolver : IGameCategoryResolver
    {
        public Task<IReadOnlyList<GameSuggestion>> SearchAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GameSuggestion>>([]);

        public Task<GameCategoryResolution> ResolveAsync(string query, CancellationToken cancellationToken)
            => Task.FromResult(new GameCategoryResolution(null, []));

        public Task RememberAsync(GameSuggestion suggestion) => Task.CompletedTask;
    }
}
