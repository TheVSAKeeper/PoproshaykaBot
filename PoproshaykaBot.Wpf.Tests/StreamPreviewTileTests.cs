using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class StreamPreviewTileTests
{
    private string _directory = string.Empty;

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

    private string WriteThumbnail()
    {
        var source = BitmapSource.Create(16, 9, 96, 96, PixelFormats.Bgra32, null, new byte[16 * 9 * 4], 16 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));

        using (var file = File.Create(Path.Combine(_directory, "preview-320x180.png")))
        {
            encoder.Save(file);
        }

        return new Uri(_directory).AbsoluteUri.TrimEnd('/') + "/preview-{width}x{height}.png";
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
