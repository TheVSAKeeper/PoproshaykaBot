using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class StreamHistoryTrendGeometryTests
{
    private static readonly DateTimeOffset Start = new(new DateTime(2026, 9, 1, 18, 0, 0, DateTimeKind.Local));
    private static readonly Size Area = new(1024, 640);
    private static readonly Size Wide = new(1360, 800);

    private const double CardBorder = 1;
    private const double CompactCoverHeight = 32;
    private const double WideCoverHeight = 72;

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
        PackScheme.Ensure();

        if (Application.Current is not null)
        {
            return;
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        foreach (var source in Dictionaries)
        {
            application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new(source) });
        }
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-trend-" + Guid.NewGuid().ToString("N"));
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

    [Test]
    public void Скрытая_полоса_отдаёт_таблице_всю_свою_высоту()
    {
        var page = CreatePage();
        var view = new StreamHistoryPageView { DataContext = page };

        Arrange(view);

        var withStrip = TableTop(view);

        page.ToggleTrendCommand.Execute(null);
        Arrange(view);

        var withoutStrip = TableTop(view);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withStrip - withoutStrip, Is.GreaterThanOrEqualTo(StreamHistoryPageView.TrendStripCompactHeight));
            Assert.That(withoutStrip, Is.LessThanOrEqualTo(CardBorder),
                "Ни столбиков, ни подписей, ни черты, ни полей обёртки – таблица начинается у рамки карточки");
        }
    }

    [Test]
    public void Компактная_карточка_идёт_малым_слотом_без_полосы_долей()
    {
        var page = CreatePage();
        page.IsCardsView = true;

        var view = new StreamHistoryPageView { DataContext = page };

        Arrange(view);

        var compact = Card(view);

        Arrange(view, Wide);

        var wide = Card(view);

        Arrange(view);

        var again = Card(view);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.IsCompactLayout, Is.True, "На 1024 DIP страница идёт стопкой – без этого мерить нечего");
            Assert.That(compact.Height, Is.LessThanOrEqualTo(StreamHistoryPageView.CardCompactHeight),
                $"Компактная карточка выросла до {compact.Height:F1} DIP при потолке {StreamHistoryPageView.CardCompactHeight}");
            Assert.That(compact.CoverHeight, Is.EqualTo(CompactCoverHeight), "В компактной карточке обложка идёт малым слотом");
            Assert.That(compact.Stripes, Is.EqualTo(Visibility.Collapsed), "Полоса долей категорий набирает высоту, которой в 72 DIP нет");
            Assert.That(wide.Height, Is.GreaterThanOrEqualTo(StreamHistoryPageView.CardHeight), "Широкая раскладка оставляет карточку прежней");
            Assert.That(wide.CoverHeight, Is.EqualTo(WideCoverHeight));
            Assert.That(wide.Stripes, Is.EqualTo(Visibility.Visible));
            Assert.That(again.Height, Is.EqualTo(compact.Height), "Возврат в узкую раскладку возвращает компактную карточку – признак не залипает");
            Assert.That(again.Stripes, Is.EqualTo(Visibility.Collapsed));
        }
    }

    private static (double Height, double CoverHeight, Visibility Stripes) Card(StreamHistoryPageView view)
    {
        var list = (ListBox)view.FindName("SessionCards")!;
        var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0)!;
        var presenter = FindChild<ContentPresenter>(container)!;
        var template = container.ContentTemplate;

        var cover = (ContentPresenter)template.FindName("CardCover", presenter)!;
        var stripes = (UIElement)template.FindName("CardStripes", presenter)!;
        var slot = FindChild<Border>(cover)!;

        return (container.ActualHeight, slot.Height, stripes.Visibility);
    }

    [Test]
    public void Меню_фильтра_висит_на_строке_сегмента_и_достижимо_с_клавиатуры()
    {
        var page = CreatePage();
        var view = new StreamHistoryPageView { DataContext = page };

        Arrange(view);

        page.TrySelectAt(0);
        Arrange(view);

        var grid = (DataGrid)view.FindName("SegmentsGrid")!;
        var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0)!;
        var cell = FindChild<DataGridCell>(row)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Segments[0].CanFilter, Is.True);
            Assert.That(ContextMenuService.GetContextMenu(row), Is.Not.Null);
            Assert.That(cell.Focusable, Is.True, "Фокус клавиатуры стоит на ячейке – строка у DataGrid не фокусируема");
            Assert.That(OwnerOfMenu(cell), Is.SameAs(row),
                "Клавиша контекстного меню поднимает ContextMenuOpening от ячейки вверх и находит меню строки");
            Assert.That(row.Cursor, Is.Null, "Строка больше не притворяется кнопкой");
        }
    }

    [Test]
    public void Меню_строки_видит_вьюмодель_той_строки_над_которой_открыто()
    {
        var page = CreatePage();
        var view = new StreamHistoryPageView { DataContext = page };

        Arrange(view);

        page.TrySelectAt(0);
        Arrange(view);

        var sessions = (DataGrid)view.FindName("SessionsGrid")!;
        var segments = (DataGrid)view.FindName("SegmentsGrid")!;

        var firstRow = (DataGridRow)sessions.ItemContainerGenerator.ContainerFromIndex(0)!;
        var secondRow = (DataGridRow)sessions.ItemContainerGenerator.ContainerFromIndex(1)!;
        var segmentRow = (DataGridRow)segments.ItemContainerGenerator.ContainerFromIndex(0)!;

        var first = MenuItemState(firstRow);
        var second = MenuItemState(secondRow);
        var segment = MenuItemState(segmentRow);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.DataContext, Is.SameAs(firstRow.DataContext), "Обработчик берёт строку из DataContext пункта меню");
            Assert.That(first.Header, Is.EqualTo(((StreamSessionRowViewModel)firstRow.DataContext).HiddenMenuCaption));
            Assert.That(second.DataContext, Is.SameAs(secondRow.DataContext), "Меню общее на все строки – оно обязано смотреть на ту, над которой открыто");
            Assert.That(segment.DataContext, Is.SameAs(segmentRow.DataContext));
            Assert.That(segment.Header, Is.EqualTo(((StreamSessionSegmentRowViewModel)segmentRow.DataContext).FilterCaption));
        }
    }

    private static (object? DataContext, object? Header) MenuItemState(FrameworkElement owner)
    {
        var menu = ContextMenuService.GetContextMenu(owner)
            ?? throw new InvalidOperationException("У строки нет контекстного меню");

        menu.PlacementTarget = owner;
        menu.IsOpen = true;

        try
        {
            var item = (MenuItem)menu.Items[0];
            item.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

            return (item.DataContext, item.Header);
        }
        finally
        {
            menu.IsOpen = false;
        }
    }

    private static DependencyObject? OwnerOfMenu(DependencyObject element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement && ContextMenuService.GetContextMenu(current) is not null)
            {
                return current;
            }
        }

        return null;
    }

    private static TChild? FindChild<TChild>(DependencyObject parent)
        where TChild : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);

            if (child is TChild found)
            {
                return found;
            }

            if (FindChild<TChild>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static void Arrange(FrameworkElement view)
    {
        Arrange(view, Area);
    }

    private static void Arrange(FrameworkElement view, Size area)
    {
        view.Measure(area);
        view.Arrange(new(area));
        view.UpdateLayout();
    }

    private static double TableTop(StreamHistoryPageView view)
    {
        var card = (FrameworkElement)view.FindName("TableCard")!;
        var grid = (FrameworkElement)view.FindName("SessionsGrid")!;

        return grid.TransformToAncestor(card).Transform(default).Y;
    }

    private StreamHistoryPageViewModel CreatePage()
    {
        var store = new StreamSessionHistoryStore(filePath: Path.Combine(_directory, "sessions.json"));

        for (var index = 0; index < 4; index++)
        {
            store.Append(Session(index));
        }

        var users = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var boxArt = new GameBoxArtProvider(new FakeBoxArtCache(), NullLogger<GameBoxArtProvider>.Instance);

        return new(
            store,
            users,
            new MemorySettings(),
            boxArt,
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance));
    }

    private static StreamSessionRecord Session(int index)
    {
        var started = Start.AddDays(index);

        return new()
        {
            Channel = "bobito217",
            StartedAt = started,
            EndedAt = started.AddHours(3),
            Title = "эфир",
            Game = index % 2 == 0 ? "Just Chatting" : "Minecraft",
            MessageCount = 100 + index,
            ChatterCount = 1,
            PeakViewers = 10 + index,
            AverageViewers = 5,
        };
    }
}
