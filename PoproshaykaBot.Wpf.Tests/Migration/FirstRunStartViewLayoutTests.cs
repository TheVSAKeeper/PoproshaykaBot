using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Migration;
using PoproshaykaBot.Wpf.Views.Migration;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests.Migration;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class FirstRunStartViewLayoutTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Dark.xaml",
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

    [TearDown]
    public void TearDown()
    {
        ApplyScale(FontScaleManager.DefaultScale);
    }

    [TestCase(460, 560, 1.0)]
    [TestCase(520, 640, 1.6)]
    public void FirstRunStartView_summary_buttons_and_text_stay_within_narrow_available_width(
        double width, double height, double scale)
    {
        ApplyScale(scale);

        var vm = FirstRunStartViewModel.CreatePreview([], new StubFilePicker(), NullLogger.Instance);
        vm.ShowGallerySummary(new FirstRunImportSummary(
            "Данные перенесены частично",
            [
                new("Профили эфира", "7 профилей"),
                new("Оверлей и локальный порт", "Оформление чата OBS и порт 8080"),
            ],
            [
                new("OBS и аудиоисточники", "Настройки OBS в старой папке не найдены – при необходимости подключите OBS в Настройках."),
                new("Вход в Twitch", "Для работы бота нужно войти в Twitch.", IsWarning: true),
            ],
            IsPartial: true));

        var view = new FirstRunStartView { DataContext = vm };
        var host = new ContentControl { Content = view };
        if (Application.Current?.TryFindResource("Font.Sans") is FontFamily fontFamily)
        {
            host.FontFamily = fontFamily;
        }

        if (Application.Current?.TryFindResource("Font.Size.M") is double fontSize)
        {
            host.FontSize = fontSize;
        }

        var viewport = new Size(width, height);
        host.Measure(viewport);
        host.Arrange(new(viewport));
        host.UpdateLayout();

        var connectBtn = (Button)view.FindName("ConnectYouTubeSummaryButton")!;
        var openBtn = (Button)view.FindName("OpenDashboardSummaryButton")!;
        var connectBounds = connectBtn.TransformToAncestor(host).TransformBounds(new(connectBtn.RenderSize));
        var openBounds = openBtn.TransformToAncestor(host).TransformBounds(new(openBtn.RenderSize));

        Assert.Multiple(() =>
        {
            Assert.That(view.ActualWidth, Is.LessThanOrEqualTo(width + 0.5));
            Assert.That(connectBounds.Right, Is.LessThanOrEqualTo(width + 0.5),
                $"Кнопка «Подключить YouTube» обрезана справа при {width}x{height} @ {scale}: Right={connectBounds.Right:F1}");
            Assert.That(openBounds.Right, Is.LessThanOrEqualTo(width + 0.5),
                $"Кнопка «Открыть панель» обрезана справа при {width}x{height} @ {scale}: Right={openBounds.Right:F1}");
        });
    }

    [TestCase(460, 560, 1.0)]
    [TestCase(520, 640, 1.6)]
    public void FirstRunStartView_choice_storage_selector_and_buttons_stay_within_narrow_available_width(
        double width, double height, double scale)
    {
        ApplyScale(scale);

        var vm = FirstRunStartViewModel.CreatePreview([], new StubFilePicker(), NullLogger.Instance);
        var view = new FirstRunStartView { DataContext = vm };
        var host = new ContentControl { Content = view };
        if (Application.Current?.TryFindResource("Font.Sans") is FontFamily fontFamily)
        {
            host.FontFamily = fontFamily;
        }

        if (Application.Current?.TryFindResource("Font.Size.M") is double fontSize)
        {
            host.FontSize = fontSize;
        }

        var viewport = new Size(width, height);
        host.Measure(viewport);
        host.Arrange(new(viewport));
        host.UpdateLayout();

        var portableRadio = (RadioButton)view.FindName("PortableStorageRadioButton")!;
        var appDataRadio = (RadioButton)view.FindName("AppDataStorageRadioButton")!;
        var importBtn = (Button)view.FindName("ImportChoiceButton")!;
        var startFreshBtn = (Button)view.FindName("StartFreshChoiceButton")!;
        var portableBounds = portableRadio.TransformToAncestor(host).TransformBounds(new(portableRadio.RenderSize));
        var appDataBounds = appDataRadio.TransformToAncestor(host).TransformBounds(new(appDataRadio.RenderSize));
        var importBounds = importBtn.TransformToAncestor(host).TransformBounds(new(importBtn.RenderSize));
        var startFreshBounds = startFreshBtn.TransformToAncestor(host).TransformBounds(new(startFreshBtn.RenderSize));

        Assert.Multiple(() =>
        {
            Assert.That(view.ActualWidth, Is.LessThanOrEqualTo(width + 0.5));
            Assert.That(portableBounds.Right, Is.LessThanOrEqualTo(width + 0.5));
            Assert.That(appDataBounds.Right, Is.LessThanOrEqualTo(width + 0.5));
            Assert.That(importBounds.Right, Is.LessThanOrEqualTo(width + 0.5));
            Assert.That(startFreshBounds.Right, Is.LessThanOrEqualTo(width + 0.5));
        });
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
            resources[key] = Math.Abs(scale - FontScaleManager.DefaultScale) < 0.001
                ? (double)tokens[key]
                : (double)tokens[key] * scale;
        }

        AppScaledSizes.Register(Application.Current);
    }

    private sealed class StubFilePicker : IFilePicker
    {
        public string? PickFolder(string title, string? initialDirectory = null) => null;

        public string? PickFile(FileOpenRequest request) => null;

        public string? SaveFile(FileSaveRequest request) => null;
    }
}
