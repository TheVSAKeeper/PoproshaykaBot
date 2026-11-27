using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views.Tiles;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
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
        TestResources.Ensure(Dictionaries);
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
        Assert.That(tile.ContentFillsTile, Is.True, "Кадр идёт от края до края тела плитки, без полей.");

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

        Assert.Multiple(() =>
        {
            Assert.That(tile.Thumbnail, Is.Null, "Без стрима плитка показывает состояние «Нет превью», а не прошлый кадр.");
            Assert.That(tile.ContentFillsTile, Is.False, "Карточке «Нет превью» поля тела нужны, кадру – нет.");
        });
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
    public void Раскладка_по_умолчанию_использует_четырёхколоночный_мультистрим_и_скрывает_превью_стрима()
    {
        var layout = DashboardLayoutPresets.CreateWide();

        DashboardLayoutReconciler.SyncRoot(layout);

        var preview = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "stream-preview", StringComparison.Ordinal));
        var polls = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "polls-control", StringComparison.Ordinal));
        var obs = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "obs-info", StringComparison.Ordinal));
        var overlay = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "chat-overlay-preview", StringComparison.Ordinal));
        var webChat = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "twitch-chat", StringComparison.Ordinal));
        var multiChat = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "multiplatform-chat", StringComparison.Ordinal));
        var multistream = layout.Tiles.Single(tile => string.Equals(tile.TypeId, "multistream", StringComparison.Ordinal));
        var firstColumnVisible = layout.Tiles.Where(tile => tile.IsVisible && tile.Column == 0).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(layout.ColumnCount, Is.EqualTo(4));
            Assert.That(layout.RowCount, Is.EqualTo(5));
            Assert.That(layout.Root, Is.Not.Null, "Четырёхколоночная раскладка должна разбираться в дерево разрезов.");
            Assert.That(preview.IsVisible, Is.False);
            Assert.That(polls.IsVisible, Is.False);
            Assert.That(firstColumnVisible.Select(tile => tile.TypeId), Is.EquivalentTo(new[] { "stream-info", "broadcast-status", "broadcast-profiles" }));
            Assert.That((obs.Column, obs.Row, obs.RowSpan, obs.IsVisible), Is.EqualTo((1, 0, 3, true)));
            Assert.That((overlay.Column, overlay.Row, overlay.RowSpan, overlay.IsVisible), Is.EqualTo((1, 3, 2, true)));
            Assert.That((webChat.Column, webChat.Row, webChat.RowSpan, webChat.IsVisible), Is.EqualTo((2, 0, 5, true)));
            Assert.That((multiChat.Column, multiChat.Row, multiChat.RowSpan, multiChat.IsVisible), Is.EqualTo((3, 0, 3, true)));
            Assert.That((multistream.Column, multistream.Row, multistream.RowSpan, multistream.IsVisible), Is.EqualTo((3, 3, 2, true)));
        });

        var reset = DashboardLayoutReconciler.ResetToDefaults(DashboardLayoutDefaults.Create(), new()
        {
            ColumnCount = 3,
            RowCount = 5,
            Tiles =
            [
                new() { Id = "stream-preview", TypeId = "stream-preview", IsVisible = true },
                new() { Id = "polls-control", TypeId = "polls-control", IsVisible = true },
                new() { Id = "logs", TypeId = "logs", IsVisible = true },
            ],
        });

        Assert.Multiple(() =>
        {
            Assert.That(reset.Tiles.Single(tile => tile.TypeId == "stream-preview").IsVisible, Is.False);
            Assert.That(reset.Tiles.Single(tile => tile.TypeId == "polls-control").IsVisible, Is.False);
            Assert.That(reset.Tiles.Single(tile => tile.TypeId == "logs").IsVisible, Is.True);
        });
    }

    [TestCase(true, VerticalAlignment.Top, 200d)]
    [TestCase(true, VerticalAlignment.Top, 120d)]
    [TestCase(false, VerticalAlignment.Top, 200d)]
    [TestCase(false, VerticalAlignment.Stretch, 150d)]
    public void Кадр_ужимается_по_высоте_слота_а_не_режется_краем_плитки(bool contentTrack, VerticalAlignment body, double slotHeight)
    {
        using var tile = CreateOnlineTile(WriteFixedThumbnail());
        var chrome = (DataTemplate)XamlReader.Parse(
            $"""
             <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                           xmlns:t="clr-namespace:PoproshaykaBot.Wpf.Views.Tiles;assembly=PoproshaykaBot.Wpf">
                 <ScrollViewer HorizontalScrollBarVisibility="Disabled"
                               VerticalScrollBarVisibility="Auto"
                               VerticalAlignment="{body}">
                     <t:StreamPreviewTileView />
                 </ScrollViewer>
             </DataTemplate>
             """);
        var host = new Grid { Width = 700, Height = contentTrack ? 600 : slotHeight };
        host.RowDefinitions.Add(contentTrack
            ? new() { Height = GridLength.Auto, MaxHeight = slotHeight }
            : new() { Height = new(1, GridUnitType.Star) });
        host.RowDefinitions.Add(new() { Height = contentTrack ? new(1, GridUnitType.Star) : new(0) });
        host.Children.Add(new ContentControl
        {
            Content = tile,
            ContentTemplate = chrome,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        });

        Arrange(host);

        var image = FindImage(host);
        var scroller = FindAncestor<ScrollViewer>(image);

        Assert.Multiple(() =>
        {
            Assert.That(scroller.ExtentHeight, Is.LessThanOrEqualTo(scroller.ViewportHeight + 0.5),
                "Кадр выше слота уходил под край плитки с полосой прокрутки – он обязан уменьшиться.");
            Assert.That(image.ActualHeight, Is.EqualTo(slotHeight).Within(1), "Кадр выше слота обрезался краем плитки.");
            Assert.That(image.ActualWidth, Is.EqualTo(slotHeight * 16 / 9).Within(1), "Уменьшение идёт с сохранением 16:9.");
        });

        if (contentTrack)
        {
            return;
        }

        host.Height = 600;
        Arrange(host);

        Assert.That(image.ActualWidth, Is.EqualTo(700).Within(1),
            "Когда слот снова вырос, кадр возвращается во всю ширину, а не остаётся на прежнем потолке.");
    }

    [Test]
    public void Обновление_грузит_новый_кадр_мимо_кеша_и_не_копит_запросы()
    {
        using var server = new FrameServer();
        using var tile = CreateOnlineTile(server.Template);

        PumpUntil(() => tile.Thumbnail is not null && !tile.IsLoading);

        var first = tile.Thumbnail;

        tile.RefreshFrameCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(tile.IsLoading, Is.True);
            Assert.That(tile.RefreshFrameCommand.CanExecute(null), Is.False, "Во время загрузки кнопка неактивна.");
            Assert.That(tile.Thumbnail, Is.SameAs(first), "Пока новый кадр не доехал, на экране остаётся прежний, а не пустота.");
        });

        tile.RefreshFrameCommand.Execute(null);
        PumpUntil(() => !tile.IsLoading);

        Assert.Multiple(() =>
        {
            Assert.That(server.Queries, Has.Count.EqualTo(2), "Повторное нажатие во время загрузки второго запроса не даёт.");
            Assert.That(server.Queries[0], Does.StartWith("?t="), "Первый кадр эфира тоже мимо кеша: адрес шаблона между эфирами не меняется.");
            Assert.That(server.Queries[1], Does.StartWith("?t="), "Новый кадр запрашивается новым адресом, иначе его отдаст кеш.");
            Assert.That(server.Queries[1], Is.Not.EqualTo(server.Queries[0]));
            Assert.That(tile.Thumbnail, Is.Not.SameAs(first));
            Assert.That(tile.RefreshFrameCommand.CanExecute(null), Is.True);
        });
    }

    [Test]
    public void Автообновление_идёт_только_в_эфире_и_только_пока_плитка_на_экране()
    {
        using var server = new FrameServer();
        var dispatcher = new ManualUiDispatcher();
        var stream = new FakeStreamStatus(StreamStatus.Online, OnlineInfo(server.Template));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        using var tile = new StreamPreviewTileViewModel(new FakePlatformRegistry(stream), bus, NullLogger<StreamPreviewTileViewModel>.Instance, dispatcher);
        var timer = dispatcher.Timers.Single();

        PumpUntil(() => tile.Thumbnail is not null && !tile.IsLoading);

        Assert.Multiple(() =>
        {
            Assert.That(timer.IsRunning, Is.True);
            Assert.That(timer.Interval, Is.EqualTo(TimeSpan.FromMinutes(5)));
        });

        timer.Tick();

        Assert.That(server.Queries, Has.Count.EqualTo(1), "Скрытая плитка (другая страница, свёрнута) кадр не грузит.");

        tile.SetOnScreen(true);
        PumpUntil(() => !tile.IsLoading);

        Assert.That(server.Queries, Has.Count.EqualTo(2), "Пропущенное обновление догоняется, когда плитка снова видна.");

        timer.Tick();
        PumpUntil(() => !tile.IsLoading);

        Assert.That(server.Queries, Has.Count.EqualTo(3));

        stream.Status = StreamStatus.Offline;
        bus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "channel")).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(timer.IsRunning, Is.False, "Офлайн таймер не работает.");
            Assert.That(tile.Thumbnail, Is.Null);
            Assert.That(tile.RefreshFrameCommand.CanExecute(null), Is.False, "Офлайн обновлять нечего.");
        });
    }

    [Test]
    public void Сорвавшаяся_загрузка_говорит_об_этом_и_не_стирает_прежний_кадр()
    {
        using var server = new FrameServer { Fail = true };
        using var tile = CreateOnlineTile(server.Template);
        tile.SetOnScreen(true);

        PumpUntil(() => !tile.IsLoading);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Thumbnail, Is.Null);
            Assert.That(tile.LoadFailed, Is.True);
            Assert.That(tile.EmptyHeading, Is.EqualTo("Кадр не загрузился"));
            Assert.That(tile.RefreshFrameCommand.CanExecute(null), Is.True, "После отказа кадр можно запросить заново.");
        });

        server.Fail = false;
        tile.RefreshFrameCommand.Execute(null);
        PumpUntil(() => !tile.IsLoading);

        var shown = tile.Thumbnail;

        Assert.That(shown, Is.Not.Null);

        server.Fail = true;
        tile.RefreshFrameCommand.Execute(null);
        PumpUntil(() => !tile.IsLoading);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Thumbnail, Is.SameAs(shown), "Неудачное обновление оставляет на экране прежний кадр.");
            Assert.That(tile.HeaderStatus?.Severity, Is.EqualTo(StatusSeverity.Warning), "…и говорит об этом в шапке плитки.");
        });
    }

    [Test]
    public void Мультистрим_вкладки_переключают_площадку_в_один_клик_и_подгружают_её_кадр()
    {
        var twitchThumb = WriteNamedThumbnail("twitch.png");
        var youtubeThumb = WriteNamedThumbnail("youtube.png");
        var twitchStream = new FakeStreamStatus(StreamStatus.Online, OnlineInfo(twitchThumb));
        var youtubeStream = new FakeStreamStatus(StreamStatus.Online, OnlineInfo(youtubeThumb));
        var registry = new MultiPlatformFakeRegistry(
            new MultiFakeChatPlatform(StreamingPlatform.Twitch, "Twitch", twitchStream),
            new MultiFakeChatPlatform(StreamingPlatform.YouTube, "YouTube", youtubeStream));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);

        using var tile = new StreamPreviewTileViewModel(
            registry,
            bus,
            NullLogger<StreamPreviewTileViewModel>.Instance,
            new ManualUiDispatcher());

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasMultiplePlatforms, Is.True);
            Assert.That(tile.PlatformTabs.Select(static t => t.Platform),
                Is.EqualTo(new[] { StreamingPlatform.Twitch, StreamingPlatform.YouTube }));
            Assert.That(tile.PlatformTabs[0].IsSelected, Is.True);
            Assert.That(tile.PlatformTabs[0].IsOnline, Is.True);
            Assert.That(tile.PlatformTabs[1].IsSelected, Is.False);
            Assert.That(tile.PlatformTabs[1].IsOnline, Is.True);
            Assert.That(tile.Thumbnail?.UriSource.LocalPath, Does.EndWith("twitch.png"));
        });

        tile.SelectPlatformCommand.Execute(StreamingPlatform.YouTube);

        Assert.Multiple(() =>
        {
            Assert.That(tile.SelectedPlatform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(tile.PlatformTabs[0].IsSelected, Is.False);
            Assert.That(tile.PlatformTabs[1].IsSelected, Is.True);
            Assert.That(tile.Thumbnail?.UriSource.LocalPath, Does.EndWith("youtube.png"));
        });

        youtubeStream.Status = StreamStatus.Offline;
        bus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "yt")).GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(tile.SelectedPlatform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(tile.PlatformTabs[1].IsOnline, Is.False);
            Assert.That(tile.PlatformTabs[0].IsOnline, Is.True);
            Assert.That(tile.Thumbnail, Is.Null);
        });
    }

    private static void Arrange(FrameworkElement host)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            host.Measure(new(host.Width, host.Height));
            host.Arrange(new(0, 0, host.Width, host.Height));
            host.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        }
    }

    private static T FindAncestor<T>(DependencyObject node)
        where T : DependencyObject
    {
        for (var parent = VisualTreeHelper.GetParent(node); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is T found)
            {
                return found;
            }
        }

        throw new AssertionException($"Над картинкой кадра нет {typeof(T).Name}.");
    }

    private static Image FindImage(DependencyObject root)
    {
        return FindImageOrNull(root) ?? throw new AssertionException("В плитке нет картинки кадра.");
    }

    private static Image? FindImageOrNull(DependencyObject root)
    {
        if (root is Image image)
        {
            return image;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindImageOrNull(VisualTreeHelper.GetChild(root, index)) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);

        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Кадр не загрузился за 10 секунд.");
            }

            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(10);
        }
    }

    private static StreamInfo OnlineInfo(string raw)
    {
        return new()
        {
            Title = "Стрим",
            ViewerCount = 12,
            ThumbnailUrl = raw,
        };
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
        return WriteNamedThumbnail("preview.png");
    }

    private string WriteNamedThumbnail(string name)
    {
        WriteImage(name);

        return new Uri(_directory).AbsoluteUri.TrimEnd('/') + "/" + name;
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
        return CreateTile(StreamStatus.Online, OnlineInfo(raw));
    }

    private static StreamPreviewTileViewModel CreateTile(StreamStatus status, StreamInfo? info)
    {
        var stream = new FakeStreamStatus(status, info);

        return new(
            new FakePlatformRegistry(stream),
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
            NullLogger<StreamPreviewTileViewModel>.Instance,
            new ManualUiDispatcher());
    }

    private sealed class FakePlatformRegistry(IStreamStatus twitchStream) : IChatPlatformRegistry
    {
        private readonly FakeChatPlatform _twitch = new(twitchStream);

        public IReadOnlyList<IChatPlatform> All => [_twitch];

        public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            if (platform == StreamingPlatform.Twitch)
            {
                chatPlatform = _twitch;
                return true;
            }

            chatPlatform = null;
            return false;
        }

        public IChatPlatform? Find(StreamingPlatform platform) => platform == StreamingPlatform.Twitch ? _twitch : null;

        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform) ?? throw new KeyNotFoundException();

        public IReadOnlyList<IChatPlatform> GetEnabled() => [_twitch];

        public IReadOnlyList<IChatPlatform> GetConnected() => [];

        public IStreamStatus GetStreamStatus(StreamingPlatform platform) => _twitch.StreamStatus;

        public string GetDisplayName(StreamingPlatform platform) => platform.ToString();

        public string GetConfiguredChannel(StreamingPlatform platform) => "streamer";

        public string BuildStreamUrl(StreamingPlatform platform, StreamInfo? stream = null) => "https://twitch.tv/streamer";

        private sealed class FakeChatPlatform(IStreamStatus streamStatus) : IChatPlatform
        {
            public StreamingPlatform Id => StreamingPlatform.Twitch;

            public string DisplayName => "Twitch";

            public PlatformCapabilities Capabilities => PlatformCapabilities.ChannelMetadataEdit;

            public bool IsEnabled => true;

            public bool IsConnected => false;

            public IStreamStatus StreamStatus => streamStatus;

            public IPlatformMessenger Messenger => null!;

            public string ConfiguredChannel => "streamer";

            public string BuildStreamUrl(StreamInfo? stream = null) => "https://twitch.tv/streamer";
        }
    }

    private sealed class MultiPlatformFakeRegistry(params IChatPlatform[] platforms) : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All => platforms;

        public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            chatPlatform = platforms.FirstOrDefault(p => p.Id == platform);
            return chatPlatform is not null;
        }

        public IChatPlatform? Find(StreamingPlatform platform) => platforms.FirstOrDefault(p => p.Id == platform);

        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform) ?? throw new KeyNotFoundException();

        public IReadOnlyList<IChatPlatform> GetEnabled() => platforms.Where(static p => p.IsEnabled).ToArray();

        public IReadOnlyList<IChatPlatform> GetConnected() => platforms.Where(static p => p.IsConnected).ToArray();
    }

    private sealed class MultiFakeChatPlatform(StreamingPlatform id, string displayName, IStreamStatus streamStatus) : IChatPlatform
    {
        public StreamingPlatform Id => id;

        public string DisplayName => displayName;

        public PlatformCapabilities Capabilities => PlatformCapabilities.None;

        public bool IsEnabled => true;

        public bool IsConnected => false;

        public IStreamStatus StreamStatus => streamStatus;

        public IPlatformMessenger Messenger => null!;

        public string ConfiguredChannel => "channel";
    }

    private sealed class FakeStreamStatus(StreamStatus status, StreamInfo? info) : IStreamStatus
    {
        public StreamStatus Status { get; set; } = status;

        public StreamStatus CurrentStatus => Status;

        public StreamInfo? CurrentStream => info;

        public StreamStatusSnapshot GetSnapshot() => new(Status, info, null);

        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class FrameServer : IDisposable
    {
        private const int MaxRequestHeaderBytes = 16 * 1024;
        private static readonly byte[] HeaderTerminator = "\r\n\r\n"u8.ToArray();
        private static readonly byte[] NotFoundResponse =
            "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray();

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly ManualResetEventSlim _stopped = new(false);
        private readonly byte[] _frame;
        private readonly byte[] _okResponseHeader;
        private readonly List<string> _queries = [];
        private volatile bool _fail;

        public FrameServer()
        {
            var source = BitmapSource.Create(16, 9, 96, 96, PixelFormats.Bgra32, null, new byte[16 * 9 * 4], 16 * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using (var buffer = new MemoryStream())
            {
                encoder.Save(buffer);
                _frame = buffer.ToArray();
            }

            _okResponseHeader = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 200 OK\r\nContent-Type: image/png\r\nCache-Control: max-age=300\r\nContent-Length: {_frame.Length}\r\nConnection: close\r\n\r\n");

            _listener = new(IPAddress.Loopback, 0);
            _listener.Start();
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            Template = $"http://127.0.0.1:{port}/preview-{{width}}x{{height}}.png";
            _ = Task.Run(() => ServeAsync(_cts.Token));
        }

        public string Template { get; }

        public bool Fail
        {
            get => _fail;
            set => _fail = value;
        }

        public IReadOnlyList<string> Queries
        {
            get
            {
                lock (_queries)
                {
                    return _queries.ToList();
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _ = _stopped.Wait(TimeSpan.FromSeconds(5));
            _listener.Dispose();
            _stopped.Dispose();
            _cts.Dispose();
        }

        private async Task ServeAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TcpClient client;

                    try
                    {
                        client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (SocketException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    try
                    {
                        await HandleClientAsync(client, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (IOException)
                    {
                        // client disconnected early
                    }
                    catch (SocketException)
                    {
                        // client disconnected early
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                }
            }
            finally
            {
                _stopped.Set();
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            {
                await using var stream = client.GetStream();
                var query = await ReadRequestQueryAsync(stream, cancellationToken).ConfigureAwait(false);

                if (query is null)
                {
                    return;
                }

                lock (_queries)
                {
                    _queries.Add(query);
                }

                if (Fail)
                {
                    await stream.WriteAsync(NotFoundResponse.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await stream.WriteAsync(_okResponseHeader.AsMemory(), cancellationToken).ConfigureAwait(false);
                    await stream.WriteAsync(_frame.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    client.Client.Shutdown(SocketShutdown.Send);
                }
                catch (SocketException)
                {
                    // best-effort graceful socket half-close
                }
            }
        }

        private static async Task<string?> ReadRequestQueryAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[MaxRequestHeaderBytes];
            var totalRead = 0;

            while (totalRead < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                totalRead += read;

                if (buffer.AsSpan(0, totalRead).IndexOf(HeaderTerminator) >= 0)
                {
                    break;
                }
            }

            if (totalRead == 0)
            {
                return null;
            }

            var text = Encoding.ASCII.GetString(buffer, 0, totalRead);
            var lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
            var requestLine = lineEnd >= 0 ? text[..lineEnd] : text;
            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2)
            {
                return string.Empty;
            }

            var target = parts[1];
            var queryIndex = target.IndexOf('?', StringComparison.Ordinal);
            return queryIndex >= 0 ? target[queryIndex..] : string.Empty;
        }
    }
}
