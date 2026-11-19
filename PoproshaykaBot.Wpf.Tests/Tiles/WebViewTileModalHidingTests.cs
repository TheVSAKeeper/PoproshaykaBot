using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.Views.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Wpf;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Display;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views;
using PoproshaykaBot.Wpf.Views.Tiles;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class WebViewTileModalHidingTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
    ];

    private DirectoryInfo _directory = null!;
    private IDisposable? _disposable;

    public enum Tile
    {
        None = 0,
        Chat = 1,
        OverlayPreview = 2,
    }

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("webview-tile-modal");
    }

    [TearDown]
    public void TearDown()
    {
        _disposable?.Dispose();
        _disposable = null;
        _directory.Delete(recursive: true);
    }

    [TestCase(Tile.Chat, "Чат скрыт")]
    [TestCase(Tile.OverlayPreview, "Превью скрыто")]
    public void Браузер_прячется_под_диалогом_и_возвращается_после_него(Tile tile, string heading)
    {
        var viewModel = CreateViewModel(tile);
        var view = CreateView(tile, viewModel);
        var browser = (WebView2)view.FindName("WebView");
        var placeholder = Placeholder(view, heading);

        viewModel.IsModalDialogOpen = true;
        Layout(view);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(browser.Visibility, Is.EqualTo(Visibility.Collapsed),
                "HWND браузера рисуется поверх оверлея диалога – под открытым диалогом его быть не должно");
            Assert.That(placeholder.Visibility, Is.EqualTo(Visibility.Visible),
                "На месте спрятанного браузера нужна заглушка, иначе плитка выглядит сломанной");
            Assert.That(placeholder.Description, Does.Contain("диалог"),
                "Заглушка под диалогом обязана назвать причину – диалог, а не настройку дашборда");
        }

        viewModel.IsModalDialogOpen = false;
        Layout(view);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(browser.Visibility, Is.EqualTo(Visibility.Visible),
                "Закрытый диалог обязан вернуть браузер");
            Assert.That(placeholder.Visibility, Is.EqualTo(Visibility.Collapsed),
                "Заглушка после закрытия диалога легла бы поверх вернувшегося браузера");
        }
    }

    [TestCase(Tile.Chat, true)]
    [TestCase(Tile.Chat, false)]
    [TestCase(Tile.OverlayPreview, true)]
    [TestCase(Tile.OverlayPreview, false)]
    public void Диалог_и_режим_правки_вместе_держат_браузер_скрытым(Tile tile, bool dialogClosesFirst)
    {
        var viewModel = CreateViewModel(tile);
        var view = CreateView(tile, viewModel);
        var browser = (WebView2)view.FindName("WebView");

        viewModel.IsLayoutEditing = true;
        viewModel.IsModalDialogOpen = true;
        Layout(view);

        Assert.That(browser.Visibility, Is.EqualTo(Visibility.Collapsed),
            "Диалог поверх режима правки – браузер скрыт");

        if (dialogClosesFirst)
        {
            viewModel.IsModalDialogOpen = false;
        }
        else
        {
            viewModel.IsLayoutEditing = false;
        }

        Layout(view);

        Assert.That(browser.Visibility, Is.EqualTo(Visibility.Collapsed),
            dialogClosesFirst
                ? "Закрытие диалога не должно возвращать браузер, пока идёт правка раскладки"
                : "Выход из правки не должен возвращать браузер, пока открыт диалог");
    }

    [Test]
    public void Переключатель_чата_использует_выбранный_эфир_и_прячет_его_под_диалогом()
    {
        var status = new FakeStreamStatus();
        var registry = new ChatPlatformRegistry([
            new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", new FakeStreamStatus()),
            new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", status),
        ]);

        var viewModel = (ChatDisplayTileViewModel)CreateViewModel(Tile.Chat, registry);
        var view = CreateView(Tile.Chat, viewModel);
        var twitchBrowser = (WebView2)view.FindName("WebView");
        var youtubeBrowser = (WebView2)view.FindName("PlatformWebView");

        SelectMode(viewModel, StreamingPlatform.YouTube);
        Layout(view);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.PlatformChat.ChannelUri?.Query, Is.EqualTo("?v=selected-video"));
            Assert.That(viewModel.PlatformChat.ExternalUri?.AbsolutePath, Is.EqualTo("/watch"));
            Assert.That(twitchBrowser.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(youtubeBrowser.Visibility, Is.EqualTo(Visibility.Visible));
        }

        viewModel.IsModalDialogOpen = true;
        Layout(view);
        Assert.That(youtubeBrowser.Visibility, Is.EqualTo(Visibility.Collapsed));

        viewModel.IsModalDialogOpen = false;
        SelectMode(viewModel, StreamingPlatform.Twitch);
        status.CurrentStatus = StreamStatus.Offline;
        SelectMode(viewModel, StreamingPlatform.YouTube);
        Layout(view);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.PlatformChat.ChannelUri, Is.Null);
            Assert.That(viewModel.PlatformChat.ExternalUri?.AbsolutePath, Is.EqualTo("/@channel"));
            Assert.That(viewModel.PlatformChat.HasFallback, Is.True);
            Assert.That(youtubeBrowser.Visibility, Is.EqualTo(Visibility.Collapsed));
        }
    }

    [Test]
    public void Ссылка_оффлайн_чата_обновляется_после_настройки_канала()
    {
        var settings = new YouTubeSettingsStore(filePath: Path.Combine(_directory.FullName, "youtube.json"));
        var status = new FakeStreamStatus { CurrentStatus = StreamStatus.Offline };
        var registry = new ChatPlatformRegistry([
            new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", status, () => settings.Load().Channel is { Length: > 0 } channel
                ? $"https://www.youtube.com/{channel}" : null),
        ]);
        var viewModel = (ChatDisplayTileViewModel)CreateViewModel(Tile.Chat, registry, settings);
        SelectMode(viewModel, StreamingPlatform.YouTube);

        Assert.That(viewModel.PlatformChat.ExternalUri, Is.Null);
        settings.Mutate(current => current.Channel = "@new-channel");

        Assert.That(viewModel.PlatformChat.ExternalUri?.AbsolutePath, Is.EqualTo("/@new-channel"));
        Assert.That(viewModel.PlatformChat.FallbackActionCommand, Is.Not.Null);
    }

    [TestCase(ChatDisplayLayout.Columns)]
    [TestCase(ChatDisplayLayout.Rows)]
    public void Обе_площадки_размещены_в_одной_плитке_и_восстанавливают_режим(ChatDisplayLayout layout)
    {
        var registry = new ChatPlatformRegistry([
            new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", new FakeStreamStatus()),
            new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", new FakeStreamStatus()),
        ]);
        var settings = new MemorySettings();
        var model = (ChatDisplayTileViewModel)CreateViewModel(Tile.Chat, registry, uiSettings: settings);
        var view = CreateView(Tile.Chat, model);
        var twitch = (WebView2)view.FindName("WebView");
        var youtube = (WebView2)view.FindName("PlatformWebView");

        SelectMode(model, StreamingPlatform.YouTube, layout);
        Layout(view);
        var first = twitch.TranslatePoint(new Point(), view);
        var second = youtube.TranslatePoint(new Point(), view);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(twitch.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(youtube.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(twitch.ActualWidth, Is.GreaterThan(0));
            Assert.That(twitch.ActualHeight, Is.GreaterThan(0));
            Assert.That(youtube.ActualWidth, Is.EqualTo(twitch.ActualWidth).Within(1));
            Assert.That(youtube.ActualHeight, Is.EqualTo(twitch.ActualHeight).Within(1));
            Assert.That(second.X - first.X, layout == ChatDisplayLayout.Columns
                ? Is.GreaterThanOrEqualTo(twitch.ActualWidth) : Is.EqualTo(0));
            Assert.That(second.Y - first.Y, layout == ChatDisplayLayout.Rows
                ? Is.GreaterThanOrEqualTo(twitch.ActualHeight) : Is.EqualTo(0));
        }

        model.IsModalDialogOpen = true;
        Layout(view);
        Assert.That(new[] { twitch.Visibility, youtube.Visibility }, Is.All.EqualTo(Visibility.Collapsed));
        model.IsModalDialogOpen = false;
        model.PlatformChat.ShowGenericFallback();
        Layout(view);
        Assert.That(twitch.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(youtube.Visibility, Is.EqualTo(Visibility.Collapsed));

        model.Dispose();
        var restored = (ChatDisplayTileViewModel)CreateViewModel(Tile.Chat, registry, uiSettings: settings);
        Assert.That(restored.SelectedMode, Is.EqualTo(model.SelectedMode));
        SelectMode(restored, StreamingPlatform.Twitch);
        Assert.That(restored.PlatformChat.IsShown, Is.False);
    }

    [Test]
    public void Пункт_меню_в_заголовке_переключает_плитку_на_два_чата()
    {
        var registry = new ChatPlatformRegistry([
            new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", new FakeStreamStatus()),
            new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", new FakeStreamStatus()),
        ]);
        var model = (ChatDisplayTileViewModel)CreateViewModel(Tile.Chat, registry);
        var dashboard = new DashboardView();
        var template = (DataTemplate)dashboard.Resources["ChatTilePlatformSelector"];
        var header = (Grid)template.LoadContent();
        header.DataContext = model;
        var button = header.Children.OfType<Button>().Single();
        var window = new Window
        {
            Content = header, Width = 160, Height = 80,
            Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false,
        };

        try
        {
            window.Show();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var menu = button.ContextMenu!;
            menu.UpdateLayout();
            Assert.That(menu.IsOpen, Is.True);
            var columns = model.Modes.Single(mode => mode.Layout == ChatDisplayLayout.Columns);
            var item = (MenuItem)menu.ItemContainerGenerator.ContainerFromItem(columns);
            Assert.That(item.Header, Is.EqualTo(columns.Label));
            Assert.That(item.Command, Is.SameAs(model.SelectModeCommand));
            item.Command.Execute(item.CommandParameter);
            Assert.That(model.SelectedMode, Is.SameAs(columns));
        }
        finally
        {
            button.ContextMenu!.IsOpen = false;
            window.Close();
        }
    }

    private static void SelectMode(ChatDisplayTileViewModel model, StreamingPlatform platform,
        ChatDisplayLayout layout = ChatDisplayLayout.Single) =>
        model.SelectModeCommand.Execute(model.Modes.Single(mode => mode.Platform == platform && mode.Layout == layout));

    private static UserControl CreateView(Tile tile, DashboardTileViewModel viewModel)
    {
        UserControl view = tile switch
        {
            Tile.Chat => new ChatDisplayTileView(),
            Tile.OverlayPreview => new ChatOverlayPreviewTileView(),
            _ => throw new ArgumentOutOfRangeException(nameof(tile)),
        };

        view.DataContext = viewModel;
        Layout(view);

        return view;
    }

    private static EmptyState Placeholder(UserControl view, string heading)
    {
        if (view.FindName("HiddenChat") is EmptyState chat)
        {
            return chat;
        }

        var grid = (Grid)((FrameworkElement)view.FindName("WebView")).Parent;

        return grid.Children.OfType<EmptyState>().Single(state => state.Heading == heading);
    }

    private static void Layout(UserControl view)
    {
        view.Measure(new(380, 460));
        view.Arrange(new(0, 0, 380, 460));
        view.UpdateLayout();
    }

    private DashboardTileViewModel CreateViewModel(Tile tile, IChatPlatformRegistry? platformRegistry = null,
        YouTubeSettingsStore? youtubeSettings = null, ISettingsStore? uiSettings = null)
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory.FullName, "settings.json"));

        DashboardTileViewModel viewModel = tile switch
        {
            Tile.Chat => new ChatDisplayTileViewModel(settings,
                new FakeTargetChannelProvider(),
                NullLogger<ChatDisplayTileViewModel>.Instance,
                new FakeShellLauncher(),
                new FakeDialogService(),
                new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
                new ChatDisplayStore(NullLogger<ChatDisplayStore>.Instance, _directory.FullName), platformRegistry, youtubeSettings, uiSettings: uiSettings),
            Tile.OverlayPreview => new ChatOverlayPreviewTileViewModel(settings, NullLogger<ChatOverlayPreviewTileViewModel>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(tile)),
        };

        _disposable = viewModel as IDisposable;

        return viewModel;
    }

    private sealed class FakeDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public bool Confirm(string title, string message, bool defaultYes = false) => false;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => false;

        public void Info(string title, string message)
        {
        }

        public void Warning(string title, string message)
        {
        }

        public void Error(string title, string message)
        {
        }
    }

    private sealed class FakeTargetChannelProvider : ITargetChannelProvider
    {
        public TargetChannelState Current { get; } = new("poproshayka", "poproshayka", false, true);

        public void BeginSession()
        {
        }

        public void EndSession()
        {
        }
    }

    private sealed class FakeStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus { get; set; } = StreamStatus.Online;
        public StreamInfo CurrentStream { get; } = new() { Id = "selected-video" };
        public StreamStatusSnapshot GetSnapshot() => throw new NotSupportedException();
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class FakeChatPlatform(StreamingPlatform id, string name, IStreamStatus status, Func<string?>? offlineUrl = null) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => name;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => true;
        public bool IsConnected => false;
        public IStreamStatus StreamStatus => status;
        public IPlatformMessenger Messenger => throw new NotSupportedException();
        public string BuildNativeChatUrl(StreamInfo stream) => $"https://www.youtube.com/live_chat?v={stream.Id}";
        public string BuildStreamUrl(StreamInfo? stream = null) => stream is { } info
            ? $"https://www.youtube.com/watch?v={info.Id}" : offlineUrl is not null ? offlineUrl() : "https://www.youtube.com/@channel";
    }

    private sealed class FakeShellLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;

        public bool Reveal(string path) => true;

        public bool Start(string executable, params string[] arguments) => true;
    }
}
