using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Tests.Users;

[TestFixture]
[NonParallelizable]
public class UserAvatarTests
{
    private const int UserCount = 422;
    private const double MinimumTextContrast = 4.5;

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
    private FakeAvatarCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-avatars-page-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _cache = new();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [TestCase("qwerty", "Q")]
    [TestCase("_каторжник", "К")]
    [TestCase("42bot", "4")]
    [TestCase("___", UserAvatarViewModel.UnknownInitial)]
    [TestCase("", UserAvatarViewModel.UnknownInitial)]
    public void Буква_кружка_первая_буква_или_цифра_ника(string name, string initial)
    {
        Assert.That(new UserAvatarViewModel("1", name).Initial, Is.EqualTo(initial));
    }

    [Test]
    public void Цвет_кружка_зависит_только_от_ника()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(new UserAvatarViewModel("1", "qwerty").PaletteIndex,
                Is.EqualTo(new UserAvatarViewModel("2", "qwerty").PaletteIndex));
            Assert.That(new UserAvatarViewModel("1", string.Empty).PaletteIndex, Is.EqualTo(StreamSegmentPalette.NeutralSlot));
        }
    }

    [TestCase("Light")]
    [TestCase("Dark")]
    [Apartment(ApartmentState.STA)]
    public void Буква_читается_на_заливке_кружка_в_каждом_слоте_палитры(string theme)
    {
        TestResources.Ensure(Dictionaries);

        var tokens = new ResourceDictionary
        {
            Source = new($"pack://application:,,,/KeepShell;component/Resources/Themes/{theme}.xaml"),
        };

        (string Fill, string Text)[] slots =
        [
            ("Accent.Soft", "Accent.Text"),
            ("State.InfoSoft", "State.InfoText"),
            ("State.SuccessSoft", "State.SuccessText"),
            ("State.WarningSoft", "State.WarningText"),
            ("State.ErrorSoft", "State.ErrorText"),
            ("Bg.SurfaceMute", "Fg.Secondary"),
        ];

        var surface = ColorOf(tokens, "Bg.Surface");
        var weak = slots
            .Select(slot => (slot.Text, Ratio: Contrast(ColorOf(tokens, slot.Text), Over(ColorOf(tokens, slot.Fill), surface))))
            .Where(slot => slot.Ratio < MinimumTextContrast)
            .Select(slot => $"{slot.Text} {slot.Ratio:F2}:1")
            .ToList();

        Assert.That(weak, Is.Empty, $"Тема {theme}: буква кружка ниже {MinimumTextContrast}:1");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Картинка_ложится_кругом_поверх_буквы()
    {
        TestResources.Ensure(Dictionaries);

        const int Size = 56;
        var red = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr24, null, Enumerable.Repeat(new byte[] { 0, 0, 255 }, 4).SelectMany(pixel => pixel).ToArray(), 2 * 3);
        var circle = new ContentPresenter
        {
            Content = new UserAvatarViewModel("1", "viewer") { Image = red },
            ContentTemplate = (DataTemplate)new UserStatisticsPageView().Resources["UserAvatarCircle"],
            Width = Size,
            Height = Size,
        };

        circle.Measure(new(Size, Size));
        circle.Arrange(new(0, 0, Size, Size));
        circle.UpdateLayout();

        var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(circle);
        var pixels = new byte[Size * Size * 4];
        bitmap.CopyPixels(pixels, Size * 4, 0);

        byte[] PixelAt(int x, int y) => pixels.AsSpan((y * Size + x) * 4, 4).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(PixelAt(Size / 2, Size / 2), Is.EqualTo(new byte[] { 0, 0, 255, 255 }), "в центре – картинка");
            Assert.That(PixelAt(1, 1)[3], Is.Zero, "угол за кругом прозрачен");
        }
    }

    [Test]
    public async Task Адреса_разрешаются_для_всех_а_картинки_берутся_только_для_запрошенных()
    {
        var provider = FakeAvatarCache.CreateProvider(_cache);
        var ids = Enumerable.Range(1, UserCount).Select(index => index.ToString()).ToArray();
        var handles = ids.Select(id => provider.For(id, "u" + id)).ToArray();
        _cache.Cached["1"] = WriteImage("1");
        _cache.Downloadable["2"] = WriteImage("2");

        provider.Track(ids);
        provider.Request(handles[0]);
        provider.Request(handles[1]);
        provider.Request(handles[2]);
        await provider.Loading;

        var requested = _cache.CachedRequests.Concat(_cache.NetworkRequests).SelectMany(batch => batch).Distinct().ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_cache.ResolveRequests, Has.Count.EqualTo(1));
            Assert.That(_cache.ResolveRequests[0], Has.Length.EqualTo(UserCount));
            Assert.That(requested, Is.EquivalentTo(new[] { "1", "2", "3" }));
            Assert.That(handles[0].HasImage && handles[1].HasImage, Is.True);
            Assert.That(handles[2].HasImage, Is.False, "без картинки остаётся буква");
            Assert.That(((BitmapImage)handles[0].Image!).DecodePixelWidth, Is.EqualTo(UserAvatarProvider.DecodePixelSize));
        }
    }

    [Test]
    public async Task Нечитаемый_файл_уходит_из_кеша_а_кружок_остаётся_буквой()
    {
        var provider = FakeAvatarCache.CreateProvider(_cache);
        var handle = provider.For("7", "broken");
        var path = Path.Combine(_directory, "7.img");
        await File.WriteAllTextAsync(path, "не картинка");
        _cache.Cached["7"] = path;

        provider.Track(["7"]);
        provider.Request(handle);
        await provider.Loading;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(handle.HasImage, Is.False);
            Assert.That(_cache.Invalidated, Is.EqualTo(new[] { "7" }));
        }
    }

    [Test]
    public async Task Сменившаяся_на_Twitch_аватарка_перечитывается_в_том_же_проходе()
    {
        var provider = FakeAvatarCache.CreateProvider(_cache);
        var handle = provider.For("5", "viewer");
        _cache.Cached["5"] = WriteImage("old");
        _cache.Downloadable["5"] = WriteImage("new");
        _cache.Changed.Add("5");

        provider.Track(["5"]);
        provider.Request(handle);
        await provider.Loading;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_cache.NetworkRequests.SelectMany(batch => batch), Does.Contain("5"));
            Assert.That(((BitmapImage)handle.Image!).UriSource.LocalPath, Does.EndWith("new.png"));
        }
    }

    [Test]
    public async Task После_отказа_сети_Обновить_дотягивает_картинки_показанных_строк()
    {
        var provider = FakeAvatarCache.CreateProvider(_cache);
        var shown = provider.For("1", "first");
        var scrolled = provider.For("2", "second");
        string[] ids = ["1", "2", "3"];

        provider.Track(ids);
        provider.Request(shown);
        await provider.Loading;
        provider.Request(scrolled);
        await provider.Loading;
        var resolvesBeforeReload = _cache.ResolveRequests.Count;

        _cache.Downloadable["1"] = WriteImage("1");
        _cache.Downloadable["2"] = WriteImage("2");
        provider.Track(ids);
        await provider.Loading;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolvesBeforeReload, Is.EqualTo(1), "прокрутка после отказа в Helix не ходит");
            Assert.That(_cache.ResolveRequests, Has.Count.EqualTo(2), "«Обновить» – ещё один проход");
            Assert.That(shown.HasImage && scrolled.HasImage, Is.True, "«Обновить» без новых запросов строк дотягивает картинки");
            Assert.That(_cache.NetworkRequests.SelectMany(batch => batch), Has.None.EqualTo("3"),
                "картинка не берётся для строки, которую список не показывал");
        }
    }

    [Test]
    public async Task Смена_адреса_узнанная_между_проходами_доезжает_на_Обновить()
    {
        var provider = FakeAvatarCache.CreateProvider(_cache);
        var handle = provider.For("5", "viewer");
        _cache.Cached["5"] = WriteImage("old");

        provider.Track(["5"]);
        provider.Request(handle);
        await provider.Loading;

        _cache.Downloadable["5"] = WriteImage("new");
        _cache.Changed.Add("5");
        provider.Track(["5"]);
        await provider.Loading;

        Assert.That(((BitmapImage)handle.Image!).UriSource.LocalPath, Does.EndWith("new.png"));
    }

    [TestCase("удалён", false)]
    [TestCase("нечитаем", false)]
    [TestCase("нет сети", true)]
    public async Task Устаревшая_картинка_без_новой_уступает_букве_только_когда_новой_не_будет(string outcome, bool keepsOld)
    {
        var provider = FakeAvatarCache.CreateProvider(_cache);
        var handle = provider.For("5", "viewer");
        _cache.Cached["5"] = WriteImage("old");

        provider.Track(["5"]);
        provider.Request(handle);
        await provider.Loading;

        _cache.Changed.Add("5");

        switch (outcome)
        {
            case "удалён":
                _cache.Unavailable.Add("5");
                break;

            case "нечитаем":
                var broken = Path.Combine(_directory, "broken.img");
                await File.WriteAllTextAsync(broken, "не картинка");
                _cache.Downloadable["5"] = broken;
                break;
        }

        provider.Track(["5"]);
        await provider.Loading;

        if (keepsOld)
        {
            var kept = ((BitmapImage)handle.Image!).UriSource.LocalPath;
            _cache.Changed.Clear();
            _cache.Downloadable["5"] = WriteImage("new");
            provider.Track(["5"]);
            await provider.Loading;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(kept, Does.EndWith("old.png"), "при живом адресе временный отказ не отнимает показанную картинку");
                Assert.That(((BitmapImage)handle.Image!).UriSource.LocalPath, Does.EndWith("new.png"),
                    "ручка остаётся устаревшей и дотягивает новую картинку на следующем проходе");
            }
        }
        else
        {
            Assert.That(handle.HasImage, Is.False, "без новой картинки кружок возвращается к букве");
        }
    }

    [Test]
    public void Переименованный_пользователь_получает_новую_букву_и_сохраняет_картинку()
    {
        var provider = FakeAvatarCache.CreateProvider(_cache);
        var before = provider.For("9", "old");
        before.Image = new DrawingImage();

        var after = provider.For("9", "Новый");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(after, Is.Not.SameAs(before));
            Assert.That(after.Initial, Is.EqualTo("Н"));
            Assert.That(after.Image, Is.SameAs(before.Image));
            Assert.That(provider.For("9", "Новый"), Is.SameAs(after));
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public async Task Список_на_четыреста_строк_просит_картинки_только_у_реализованных_строк()
    {
        TestResources.Ensure(Dictionaries);

        var provider = FakeAvatarCache.CreateProvider(_cache);
        var page = CreatePage(provider);
        var view = new UserStatisticsPageView { DataContext = page };

        view.Measure(new(1024, 640));
        view.Arrange(new(0, 0, 1024, 640));
        view.UpdateLayout();

        await provider.Loading;

        var list = (ListBox)view.FindName("RatingList")!;
        var realized = page.Users
            .Where(row => list.ItemContainerGenerator.ContainerFromItem(row) is not null)
            .Select(row => row.UserId)
            .ToHashSet();

        var requested = _cache.CachedRequests.SelectMany(batch => batch).Distinct().ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_cache.ResolveRequests, Has.Count.EqualTo(1), "адреса разрешаются одним проходом");
            Assert.That(_cache.ResolveRequests[0], Has.Length.EqualTo(UserCount));
            Assert.That(requested, Is.Not.Empty);
            Assert.That(requested, Has.Length.LessThan(UserCount / 4), "картинки не просятся для всего списка");
            Assert.That(requested, Is.SubsetOf(realized), "картинки просят только реализованные строки");
        }
    }

    private static Color ColorOf(ResourceDictionary tokens, string key)
    {
        return tokens[key] switch
        {
            SolidColorBrush brush => brush.Color,
            Color color => color,
            var other => throw new InvalidOperationException($"{key}: {other?.GetType().Name ?? "нет ключа"}"),
        };
    }

    private static Color Over(Color top, Color bottom)
    {
        var alpha = top.A / 255.0;

        return Color.FromRgb(
            (byte)Math.Round(top.R * alpha + bottom.R * (1 - alpha)),
            (byte)Math.Round(top.G * alpha + bottom.G * (1 - alpha)),
            (byte)Math.Round(top.B * alpha + bottom.B * (1 - alpha)));
    }

    private static double Contrast(Color first, Color second)
    {
        var lighter = Math.Max(Luminance(first), Luminance(second));
        var darker = Math.Min(Luminance(first), Luminance(second));

        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(Color color)
    {
        return 0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);
    }

    private static double LinearChannel(byte value)
    {
        var normalized = value / 255.0;

        return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
    }

    private string WriteImage(string name)
    {
        var path = Path.Combine(_directory, name + ".png");
        var source = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr24, null, new byte[2 * 2 * 3], 2 * 3);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var stream = File.Create(path);
        encoder.Save(stream);

        return path;
    }

    private UserStatisticsPageViewModel CreatePage(UserAvatarProvider provider)
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings.json"));
        var statistics = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        statistics.SeedPlatform(StreamingPlatform.Twitch, Enumerable.Range(1, UserCount)
            .Select(index => new UserStatistics { UserId = index.ToString(), Name = "зритель" + index, MessageCount = (ulong)index })
            .ToList());

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
            provider);
    }

    private sealed class FakeChannelProvider : IChannelProvider
    {
        public string? Channel => "test";
    }
}
