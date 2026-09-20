using KeepShell.Testing;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Ui;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views;
using PoproshaykaBot.Wpf.Views.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class DashboardPreviewGeometryTests
{
    private static readonly Size Area = new(880, 653);

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

    [Test]
    public void Preview_lays_the_tiles_out_where_the_dashboard_arranges_them()
    {
        var typeIds = new[] { "stream-info", "broadcast-status", "twitch-chat" };

        var dashboard = ArrangeDashboard(WeightedLayout());
        var preview = ArrangePreview(WeightedLayout());

        Assert.Multiple(() =>
        {
            foreach (var typeId in typeIds)
            {
                Assert.That(dashboard.TryGetValue(typeId, out var real), Is.True, $"Плитка {typeId} обязана лечь на дашборд.");
                Assert.That(preview.TryGetValue(typeId, out var shown), Is.True, $"Плитка {typeId} обязана лечь в превью.");

                Assert.That(shown.X, Is.EqualTo(real.X).Within(0.02),
                    $"Левый край {typeId} в превью расходится с дашбордом больше чем на 2 % ширины.");
                Assert.That(shown.Y, Is.EqualTo(real.Y).Within(0.02), $"Верхний край {typeId} расходится больше чем на 2 %.");
                Assert.That(shown.Width, Is.EqualTo(real.Width).Within(0.02), $"Ширина {typeId} расходится больше чем на 2 %.");
                Assert.That(shown.Height, Is.EqualTo(real.Height).Within(0.02), $"Высота {typeId} расходится больше чем на 2 %.");
            }
        });
    }

    [Test]
    public void The_preview_fits_the_width_it_is_given_and_the_caption_names_the_real_scale()
    {
        var section = new DashboardLayoutSectionViewModel(Tiles());

        section.LoadSettings(WeightedLayout());
        section.Reference = DashboardPreviewReference.Window1920;

        var view = new DashboardLayoutSectionView { DataContext = section };
        var host = new Grid { Width = 900, Height = 700 };

        host.Children.Add(view);
        host.Measure(new(900, 700));
        host.Arrange(new(0, 0, 900, 700));
        host.UpdateLayout();
        host.UpdateLayout();

        var box = Descendants(view).OfType<Viewbox>().First();
        var area = DescendantByName(view, "PreviewArea");

        Assert.Multiple(() =>
        {
            Assert.That(box.ActualWidth, Is.LessThanOrEqualTo(area.ActualWidth + 0.5),
                "Превью не должно вылезать за отведённую ему ширину – иначе правый край раскладки обрезается.");
            Assert.That(section.Scale, Is.EqualTo(box.ActualWidth / section.ContentArea.Width).Within(0.02),
                "Подпись обязана называть тот масштаб, в котором превью действительно нарисовано.");
        });
    }

    private static FrameworkElement DescendantByName(DependencyObject root, string name)
    {
        var found = Descendants(root).OfType<FrameworkElement>().FirstOrDefault(element => element.Name == name);

        Assert.That(found, Is.Not.Null, $"Элемент {name} обязан быть в дереве превью.");

        return found!;
    }

    private static DashboardLayoutSettings WeightedLayout()
    {
        var layout = new DashboardLayoutSettings
        {
            ColumnCount = 2,
            RowCount = 2,
            Root = new SplitPane(SplitOrientation.Columns, [
                new(new SplitPane(SplitOrientation.Rows, [
                    new(new TilePane("stream-info"), 0.4),
                    new(new TilePane("broadcast-status"), 0.6),
                ]), 0.65),
                new(new TilePane("twitch-chat"), 0.35),
            ]),
        };

        Add(layout, "stream-info", 0, 0, 1, 1);
        Add(layout, "broadcast-status", 1, 0, 1, 1);
        Add(layout, "twitch-chat", 0, 1, 2, 1);

        return layout;
    }

    private static void Add(DashboardLayoutSettings layout, string typeId, int row, int column, int rowSpan, int columnSpan)
    {
        layout.Tiles.Add(new()
        {
            Id = typeId,
            TypeId = typeId,
            Order = layout.Tiles.Count,
            Row = row,
            Column = column,
            RowSpan = rowSpan,
            ColumnSpan = columnSpan,
            IsVisible = true,
        });
    }

    private static DashboardTileViewModel[] Tiles()
    {
        return
        [
            new FakeTile("stream-info", fills: true),
            new FakeTile("broadcast-status", fills: true),
            new FakeTile("twitch-chat", fills: true),
        ];
    }

    private static Dictionary<string, Rect> ArrangeDashboard(DashboardLayoutSettings layout)
    {
        using var viewModel = new DashboardViewModel(Tiles(), new(new FakeLayoutStore(layout)), TimeProvider.System);

        var view = new DashboardView { DataContext = viewModel };
        var host = new Grid();

        host.Children.Add(view);

        var outer = Area;
        FrameworkElement? bands = null;

        for (var pass = 0; pass < 4; pass++)
        {
            host.Width = outer.Width;
            host.Height = outer.Height;

            host.Measure(outer);
            host.Arrange(new(default, outer));
            host.UpdateLayout();

            bands = view.FindName("BandsGrid") as FrameworkElement;

            Assert.That(bands, Is.Not.Null, "Сетка плиток дашборда обязана найтись – по ней и меряется область панели.");

            var delta = new Size(Area.Width - bands!.ActualWidth, Area.Height - bands.ActualHeight);

            if (Math.Abs(delta.Width) < 0.5 && Math.Abs(delta.Height) < 0.5)
            {
                break;
            }

            outer = new(outer.Width + delta.Width, outer.Height + delta.Height);
        }

        var frame = DashboardPaneSurface.Bounds(bands!, host);
        var rects = new Dictionary<string, Rect>(StringComparer.Ordinal);

        foreach (var content in Descendants(view).OfType<ContentControl>())
        {
            if (content.Content is DashboardTileViewModel tile && content.ActualWidth > 0)
            {
                rects.TryAdd(tile.TypeId, Normalize(DashboardPaneSurface.Bounds(content, host), frame));
            }
        }

        return rects;
    }

    private static Dictionary<string, Rect> ArrangePreview(DashboardLayoutSettings layout)
    {
        var section = new DashboardLayoutSectionViewModel(Tiles());

        section.LoadSettings(layout);
        section.Reference = new("Проверка", new(Area.Width + DashboardPreviewReference.NavWidthExpanded,
            Area.Height + DashboardPreviewReference.TitleBarHeight + DashboardPreviewReference.StatusBarHeight));

        Assert.That(section.ContentArea.Width, Is.EqualTo(Area.Width).Within(0.001));
        Assert.That(section.Pane, Is.Not.Null);

        var rects = new Dictionary<string, Rect>(StringComparer.Ordinal);
        var elements = new Dictionary<string, FrameworkElement>(StringComparer.Ordinal);

        var surface = new DashboardPaneSurface
        {
            Tile = _ => new Border(),
            Registered = (pane, element) =>
            {
                if (pane is TilePaneLayout leaf)
                {
                    elements[leaf.Tile.TypeId] = element;
                }
            },
        };

        var canvas = new Grid { Width = Area.Width, Height = Area.Height };

        canvas.Children.Add(surface.BuildRoot(section.Pane!));
        canvas.Measure(Area);
        canvas.Arrange(new(default, Area));
        canvas.UpdateLayout();

        foreach (var (typeId, element) in elements)
        {
            rects[typeId] = Normalize(DashboardPaneSurface.Bounds(element, canvas), new(default, Area));
        }

        return rects;
    }

    [Test]
    public void The_stacked_preview_registers_its_tiles_as_drop_targets()
    {
        var section = new DashboardLayoutSectionViewModel(Tiles());

        section.LoadSettings(WeightedLayout());

        var registered = new List<PaneLayout>();

        var surface = new DashboardPaneSurface
        {
            Tile = _ => new Border(),
            Registered = (pane, _) => registered.Add(pane),
            Stacked = true,
        };

        surface.Stack(new Grid(), section.Pane!);

        Assert.That(
            registered.OfType<TilePaneLayout>().Select(leaf => leaf.Tile.TypeId),
            Is.EquivalentTo(new[] { "stream-info", "broadcast-status", "twitch-chat" }),
            "Без регистрации путей бросок плитки и «Убрать плитку» в стопке молча ничего не делают.");
    }

    private static Rect Normalize(Rect rect, Rect frame)
    {
        return new(
            (rect.X - frame.X) / frame.Width,
            (rect.Y - frame.Y) / frame.Height,
            rect.Width / frame.Width,
            rect.Height / frame.Height);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            yield return child;

            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}
