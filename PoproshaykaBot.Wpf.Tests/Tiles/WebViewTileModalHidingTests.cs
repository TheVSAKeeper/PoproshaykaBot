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
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views;
using PoproshaykaBot.Wpf.Views.Tiles;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

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
            Assert.That(viewModel.SecondaryChat.ChannelUri?.Query, Is.EqualTo("?v=selected-video"));
            Assert.That(viewModel.SecondaryChat.ExternalUri?.AbsolutePath, Is.EqualTo("/watch"));
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
            Assert.That(viewModel.SecondaryChat.ChannelUri, Is.Null);
            Assert.That(viewModel.SecondaryChat.ExternalUri?.AbsolutePath, Is.EqualTo("/@channel"));
            Assert.That(viewModel.SecondaryChat.HasFallback, Is.True);
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

        Assert.That(viewModel.PrimaryChat.ExternalUri, Is.Null);
        settings.Mutate(current => current.Channel = "@new-channel");

        Assert.That(viewModel.PrimaryChat.ExternalUri?.AbsolutePath, Is.EqualTo("/@new-channel"));
        Assert.That(viewModel.PrimaryChat.FallbackActionCommand, Is.Not.Null);
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
        model.SecondaryChat.ShowGenericFallback();
        Layout(view);
        Assert.That(twitch.Visibility, Is.EqualTo(Visibility.Visible));
        Assert.That(youtube.Visibility, Is.EqualTo(Visibility.Collapsed));

        model.Dispose();
        var restored = (ChatDisplayTileViewModel)CreateViewModel(Tile.Chat, registry, uiSettings: settings);
        Assert.That(restored.SelectedMode, Is.EqualTo(model.SelectedMode));
        SelectMode(restored, StreamingPlatform.Twitch);
        Assert.That(restored.SecondaryChat.IsShown, Is.False);
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

    [Test]
    public void Масштаб_и_сброс_масштаба_работают_для_YouTube_чата()
    {
        var status = new FakeStreamStatus();
        var registry = new ChatPlatformRegistry([
            new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", new FakeStreamStatus()),
            new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", status),
        ]);

        var viewModel = (ChatDisplayTileViewModel)CreateViewModel(Tile.Chat, registry);
        var view = CreateView(Tile.Chat, viewModel);
        var youtubeBrowser = (WebView2)view.FindName("PlatformWebView");
        var resetZoomAction = viewModel.HeaderActions.Single(action => ReferenceEquals(action.Command, viewModel.ResetZoomCommand));

        SelectMode(viewModel, StreamingPlatform.YouTube);
        Layout(view);

        var zoomBinding = BindingOperations.GetBinding(youtubeBrowser, WebView2.ZoomFactorProperty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.PrimaryChat.IsShown, Is.False);
            Assert.That(viewModel.SecondaryChat.IsShown, Is.True);
            Assert.That(resetZoomAction.IsVisible, Is.True);
            Assert.That(resetZoomAction.IsEnabled, Is.True);
            Assert.That(zoomBinding, Is.Not.Null);
            Assert.That(zoomBinding!.Path.Path, Is.EqualTo("DataContext.ZoomFactor"));
            Assert.That(zoomBinding.Mode, Is.EqualTo(BindingMode.TwoWay));
        }

        viewModel.ZoomFactor = 1.5;
        Assert.That(youtubeBrowser.ZoomFactor, Is.EqualTo(1.5).Within(0.001));

        viewModel.ResetZoomCommand.Execute(null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.ZoomFactor, Is.EqualTo(ChatDisplayStore.DefaultZoom).Within(0.001));
            Assert.That(youtubeBrowser.ZoomFactor, Is.EqualTo(ChatDisplayStore.DefaultZoom).Within(0.001));
        }

        SelectMode(viewModel, StreamingPlatform.Twitch);
        status.CurrentStatus = StreamStatus.Offline;
        SelectMode(viewModel, StreamingPlatform.YouTube);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resetZoomAction.IsVisible, Is.True);
            Assert.That(resetZoomAction.IsEnabled, Is.False);
        }
    }

    private static void SelectMode(ChatDisplayTileViewModel model, StreamingPlatform platform,
        ChatDisplayLayout layout = ChatDisplayLayout.Single) =>
        model.SelectModeCommand.Execute(model.Modes.Single(mode => mode.Platform == platform && mode.Layout == layout));

    [TestCase("Light")]
    [TestCase("Dark")]
    public void Сплит_вмещает_площадку_и_аккаунт_в_одну_строку_и_без_панели_API(string theme)
    {
        var twitch = new AccountsStore(filePath: Path.Combine(_directory.FullName, "accounts.json"));
        twitch.UpsertContext(PlatformAuthRole.Bot, "sample", "alice", new TwitchAccountSettings
        {
            Login = "alice", UserId = "tw-user", AccessToken = "fixture", BrowserProfileName = Guid.NewGuid().ToString("N"),
        });
        var youtube = new YouTubeAccountsStore(filePath: Path.Combine(_directory.FullName, "youtube-accounts.json"));
        youtube.UpsertContext(PlatformAuthRole.Bot, "sample", "Bob on YouTube", new YouTubeAccountSettings
        {
            ChannelId = "yt-user", ChannelTitle = "Bob on YouTube", AccessToken = "fixture",
        });
        var registry = new ChatPlatformRegistry([
            new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", new FakeStreamStatus()),
            new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", new FakeStreamStatus()),
        ]);
        var model = (ChatDisplayTileViewModel)CreateViewModel(Tile.Chat, registry, accountDirectories: [twitch, youtube],
            webChatLogin: new StubWebChatLogin(PoproshaykaBot.Wpf.Infrastructure.PlatformWebChatLoginResult.Canceled()));
        SelectMode(model, StreamingPlatform.YouTube, ChatDisplayLayout.Rows);
        model.PrimaryChat.SelectedAccount = model.PrimaryChat.AccountChoices.Single(option => option.Id == "tw-user");
        model.SecondaryChat.SelectedAccount = model.SecondaryChat.AccountChoices.Single(option => option.Id == "yt-user");
        var view = CreateView(Tile.Chat, model);
        view.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new($"pack://application:,,,/KeepShell;component/Resources/Themes/{theme}.xaml"),
        });
        view.SetResourceReference(Control.BackgroundProperty, "Bg.Surface");
        Layout(view);

        var pickers = FindChildren<ComboBox>(view).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(pickers, Has.Length.EqualTo(2));
            Assert.That(pickers.Select(picker => picker.ActualHeight), Is.All.InRange(12.0, 26.0));
            Assert.That(typeof(ChatDisplayPaneViewModel).GetProperty("ShowMessageInput"), Is.Null);
            Assert.That(typeof(ChatDisplayPaneViewModel).GetProperty("SendMessageCommand"), Is.Null);
            Assert.That(model.SecondaryChat.SelectedAccount!.Label, Is.EqualTo("Bob on YouTube (бот)"));
            Assert.That(model.SecondaryChat.CanSignInBrowser, Is.True);
        });
        TestResources.CaptureAccountLayout(view, $"chat-{theme}", 380, 460);
    }

    [Test]
    public async Task Плитка_показывает_Twitch_аккаунт_без_профиля_и_единообразно_сбрасывает_веб_сессию()
    {
        var twitchPath = Path.Combine(_directory.FullName, "twitch-accounts.json");
        var twitch = new AccountsStore(filePath: twitchPath);
        twitch.UpsertContext(PlatformAuthRole.Bot, "sample", "alice", new TwitchAccountSettings
        {
            Login = "alice", UserId = "tw-user", AccessToken = "fixture",
        });
        var youtubePath = Path.Combine(_directory.FullName, "youtube-accounts.json");
        var youtube = new YouTubeAccountsStore(filePath: youtubePath);
        youtube.UpsertContext(PlatformAuthRole.Bot, "sample", "Bob on YouTube", new YouTubeAccountSettings
        {
            ChannelId = "yt-user", ChannelTitle = "Bob on YouTube", AccessToken = "fixture",
        });

        var registry = new ChatPlatformRegistry([
            new FakeChatPlatform(StreamingPlatform.Twitch, "Twitch", new FakeStreamStatus()),
            new FakeChatPlatform(StreamingPlatform.YouTube, "YouTube", new FakeStreamStatus()),
        ]);
        var profileId = Guid.NewGuid().ToString("N");
        var model = (ChatDisplayTileViewModel)CreateViewModel(
            Tile.Chat,
            registry,
            accountDirectories: [twitch, youtube],
            webChatLogin: new StubWebChatLogin(PoproshaykaBot.Wpf.Infrastructure.PlatformWebChatLoginResult.Completed(profileId)));

        var twOption = model.PrimaryChat.AccountChoices.SingleOrDefault(o => o.Id == "tw-user");
        Assert.That(twOption, Is.Not.Null);
        model.PrimaryChat.SelectedAccount = twOption;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.PrimaryChat.CanSignInBrowser, Is.True);
            Assert.That(model.PrimaryChat.CanResetBrowserSession, Is.False);
        }

        await model.PrimaryChat.SignInBrowserCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(model.PrimaryChat.CanSignInBrowser, Is.False);
            Assert.That(model.PrimaryChat.CanResetBrowserSession, Is.True);
            Assert.That(
                new AccountsStore(filePath: twitchPath).ListAccounts().Single().Authorizations.Single().ChatBrowserProfile?.ProfileName,
                Is.EqualTo(profileId));
        });

        var resetRequestedAccountId = string.Empty;
        model.ResetPaneSessionRequested += (pane, accountId, _) =>
        {
            resetRequestedAccountId = accountId;
            model.CompletePaneSessionReset(pane, pane.Platform, accountId);
        };

        model.PrimaryChat.ResetBrowserSessionCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(resetRequestedAccountId, Is.EqualTo("tw-user"));
            Assert.That(model.PrimaryChat.CanSignInBrowser, Is.True);
            Assert.That(model.PrimaryChat.CanResetBrowserSession, Is.False);
            Assert.That(
                new AccountsStore(filePath: twitchPath).ListAccounts().Single().Authorizations.Single().ChatBrowserProfile,
                Is.Null);
        });
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T target)
            {
                yield return target;
            }

            foreach (var descendant in FindChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

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
        YouTubeSettingsStore? youtubeSettings = null, ISettingsStore? uiSettings = null,
        IEnumerable<IPlatformAccountDirectory>? accountDirectories = null,
        PoproshaykaBot.Wpf.Infrastructure.IPlatformWebChatLoginDialog? webChatLogin = null)
    {
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory.FullName, "settings.json"));

        var effectiveRegistry = platformRegistry ?? new ChatPlatformRegistry([new FakeTwitchPlatform()]);

        DashboardTileViewModel viewModel = tile switch
        {
            Tile.Chat => new ChatDisplayTileViewModel(settings,
                new FakeTargetChannelProvider(),
                NullLogger<ChatDisplayTileViewModel>.Instance,
                new FakeShellLauncher(),
                new FakeDialogService(),
                new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance),
                new ChatDisplayStore(NullLogger<ChatDisplayStore>.Instance, _directory.FullName), effectiveRegistry, youtubeSettings,
                uiSettings: uiSettings, accountDirectories: accountDirectories, webChatLogin: webChatLogin),
            Tile.OverlayPreview => new ChatOverlayPreviewTileViewModel(settings, NullLogger<ChatOverlayPreviewTileViewModel>.Instance),
            _ => throw new ArgumentOutOfRangeException(nameof(tile)),
        };

        _disposable = viewModel as IDisposable;

        return viewModel;
    }

    private sealed class FakeTwitchPlatform : IChatPlatform
    {
        public StreamingPlatform Id => StreamingPlatform.Twitch;

        public string DisplayName => "Twitch";

        public PlatformCapabilities Capabilities => PlatformCapabilities.None;

        public bool IsEnabled => true;

        public bool IsConnected => false;

        public IStreamStatus StreamStatus { get; } = new FakeStreamStatus();

        public IPlatformMessenger Messenger => throw new NotSupportedException();

        public bool RequiresOnlineStreamForNativeChat => false;

        public bool SupportsChatClutterBlocking => true;

        public string? BuildNativeChatUrl(StreamInfo? stream, string? targetChannelLogin, bool isDarkTheme) =>
            string.IsNullOrWhiteSpace(targetChannelLogin)
                ? null
                : $"https://www.twitch.tv/popout/{Uri.EscapeDataString(targetChannelLogin.Trim())}/chat?popout=";
    }

    private sealed class StubWebChatLogin(PoproshaykaBot.Wpf.Infrastructure.PlatformWebChatLoginResult result)
        : PoproshaykaBot.Wpf.Infrastructure.IPlatformWebChatLoginDialog
    {
        public Task<PoproshaykaBot.Wpf.Infrastructure.PlatformWebChatLoginResult> SignInAsync(
            PoproshaykaBot.Wpf.Infrastructure.PlatformWebChatLoginRequest request) =>
            Task.FromResult(result);
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
