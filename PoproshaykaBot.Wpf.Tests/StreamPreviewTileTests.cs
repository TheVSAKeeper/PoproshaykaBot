using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views.Tiles;
using System.IO;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class StreamPreviewTileTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
    ];

    private string _directory = string.Empty;

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestApplication.EnsureResources(Dictionaries);
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"poproshayka-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void Онлайн_подставляет_размеры_в_адрес_кадра()
    {
        var raw = WriteThumbnail();

        using var tile = CreateTile(StreamStatus.Online, new()
        {
            Title = "Стрим",
            GameName = "Just Chatting",
            ViewerCount = 12,
            ThumbnailUrl = raw,
        });

        Assert.That(tile.Thumbnail, Is.Not.Null, "Плитка превью обязана показать кадр стрима, который сейчас в эфире.");

        Assert.Multiple(() =>
        {
            Assert.That(tile.Thumbnail!.UriSource.LocalPath, Does.Contain("320x180"),
                "В адрес Helix подставляются ширина и высота, иначе Twitch отдаёт шаблон вместо картинки.");

            Assert.That(tile.Thumbnail.DecodePixelWidth, Is.EqualTo(320));
        });
    }

    [TestCase(300d, 320, false)]
    [TestCase(330d, 512, true)]
    [TestCase(700d, 704, true)]
    [TestCase(5000d, 1920, true)]
    public void Кадр_перекодируется_по_ширине_слота_только_когда_она_выросла(double pixels, int expected, bool reloaded)
    {
        using var tile = CreateOnlineTile(WriteThumbnail(320, 512, 704, 1920));
        var before = tile.Thumbnail;

        tile.FitTo(pixels);

        Assert.Multiple(() =>
        {
            Assert.That(tile.DecodeWidth, Is.EqualTo(expected));
            Assert.That(tile.Thumbnail!.DecodePixelWidth, Is.EqualTo(expected));
            Assert.That(tile.Thumbnail.UriSource.LocalPath, Does.Contain($"{expected}x{expected * 9 / 16}"),
                "Twitch отдаёт кадр того размера, который назван в адресе, – декодировать 704 px из кадра в 320 бессмысленно.");
            Assert.That(ReferenceEquals(before, tile.Thumbnail), Is.Not.EqualTo(reloaded),
                "Слот, который не шире уже декодированного кадра, картинку не пересоздаёт.");
        });
    }

    [Test]
    public void Растягивание_слота_по_пикселю_пересоздаёт_кадр_считаные_разы()
    {
        using var tile = CreateOnlineTile(WriteThumbnail(320, 512, 768, 1152));
        var reloads = 0;

        tile.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(StreamPreviewTileViewModel.Thumbnail))
            {
                reloads++;
            }
        };

        for (var width = 321; width <= 1000; width++)
        {
            tile.FitTo(width);
        }

        Assert.Multiple(() =>
        {
            Assert.That(reloads, Is.EqualTo(3), "Каждый рост берёт запас в полтора раза: 512, 768, 1152 – а не новый кадр на каждый пиксель.");
            Assert.That(tile.DecodeWidth, Is.EqualTo(1152));
        });
    }

    [Test]
    public void Ширина_декодирования_берётся_из_слота_плитки_с_учётом_dpi()
    {
        using var tile = CreateOnlineTile(WriteFixedThumbnail());
        var view = new StreamPreviewTileView { DataContext = tile };
        var host = new Grid { Width = 700, Height = 400, Children = { view } };

        host.Measure(new(700, 400));
        host.Arrange(new(0, 0, 700, 400));
        host.UpdateLayout();

        var expected = 700 * VisualTreeHelper.GetDpi(view).DpiScaleX;

        Assert.That(tile.DecodeWidth, Is.GreaterThanOrEqualTo(expected).And.LessThan(expected * 1.5 + 64),
            "Кадр декодируется под физические пиксели слота, а не в прежние 320.");
    }

    [Test]
    public void Плитка_по_содержимому_кадр_не_растит_и_шире_прежнего_не_становится()
    {
        using var tile = CreateOnlineTile(WriteFixedThumbnail());
        tile.FitTo(1000);

        var view = new StreamPreviewTileView { DataContext = tile };
        var host = new StackPanel { Orientation = Orientation.Horizontal, Children = { view } };

        host.Measure(new(double.PositiveInfinity, 600));
        host.Arrange(new(0, 0, host.DesiredSize.Width, 600));
        host.UpdateLayout();

        Assert.Multiple(() =>
        {
            Assert.That(tile.DecodeWidth, Is.EqualTo(1024), "Без ширины слота мерить нечего – кадр не перекодируется.");
            Assert.That(view.ActualWidth, Is.LessThanOrEqualTo(320.5),
                "В колонке по содержимому ширину даёт сам кадр, и перекодированный кадр не должен раздвигать колонку.");
        });
    }

    [Test]
    public void Офлайн_оставляет_плитку_без_кадра()
    {
        using var tile = CreateTile(StreamStatus.Offline, null);

        Assert.That(tile.Thumbnail, Is.Null, "Без стрима плитка показывает состояние «Нет превью», а не прошлый кадр.");
    }

    [Test]
    public void Голый_снимок_стрима_кадра_не_даёт()
    {
        using var tile = CreateTile(StreamStatus.Online, new() { ThumbnailUrl = WriteThumbnail() });

        Assert.That(tile.Thumbnail, Is.Null,
            "Снимок из stream.online до ответа Helix ещё не несёт данных стрима – кадр по нему не грузится, как и в плитке «Информация о стриме».");
    }

    [Test]
    public void Стрим_без_адреса_кадра_плитку_не_ломает()
    {
        using var tile = CreateTile(StreamStatus.Online, new()
        {
            Title = "Стрим",
            ViewerCount = 3,
        });

        Assert.That(tile.Thumbnail, Is.Null);
    }

    [Test]
    public void Раскладка_по_умолчанию_ставит_превью_над_превью_оверлея()
    {
        var layout = DashboardLayoutDefaults.Create();

        var preview = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "stream-preview", StringComparison.Ordinal));
        var overlay = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "chat-overlay-preview", StringComparison.Ordinal));
        var firstColumn = layout.Tiles.Where(tile => tile.Column == 0).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(preview.Column, Is.EqualTo(1));
            Assert.That(preview.Row, Is.Zero);
            Assert.That(preview.RowSpan, Is.EqualTo(2));
            Assert.That(overlay.Column, Is.EqualTo(1));
            Assert.That(overlay.Row, Is.EqualTo(2));
            Assert.That(overlay.RowSpan, Is.EqualTo(3));
            Assert.That(preview.IsVisible, Is.True);

            Assert.That(firstColumn, Has.Count.EqualTo(5),
                "Полы плиток первой колонки уже дают 796 DIP при бюджете 733 у окна 800 – шестая плитка увела бы её в прокрутку глубже.");
        });
    }

    private string WriteThumbnail(params int[] widths)
    {
        foreach (var width in widths.DefaultIfEmpty(320))
        {
            WriteImage($"preview-{width}x{width * 9 / 16}.png");
        }

        return new Uri(_directory).AbsoluteUri.TrimEnd('/') + "/preview-{width}x{height}.png";
    }

    private string WriteFixedThumbnail()
    {
        WriteImage("preview.png");

        return new Uri(_directory).AbsoluteUri.TrimEnd('/') + "/preview.png";
    }

    private void WriteImage(string name)
    {
        var source = BitmapSource.Create(16, 9, 96, 96, PixelFormats.Bgra32, null, new byte[16 * 9 * 4], 16 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using var file = File.Create(Path.Combine(_directory, name));

        encoder.Save(file);
    }

    private static StreamPreviewTileViewModel CreateOnlineTile(string raw)
    {
        return CreateTile(StreamStatus.Online, new()
        {
            Title = "Стрим",
            ViewerCount = 12,
            ThumbnailUrl = raw,
        });
    }

    private static StreamPreviewTileViewModel CreateTile(StreamStatus status, StreamInfo? info)
    {
        var stream = new FakeStreamStatus(status, info);

        return new(stream, new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance));
    }

    private sealed class FakeStreamStatus(StreamStatus status, StreamInfo? info) : IStreamStatus
    {
        public StreamStatus CurrentStatus => status;

        public StreamInfo? CurrentStream => info;

        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }
}
