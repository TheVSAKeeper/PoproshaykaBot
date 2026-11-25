using KeepShell.Bootstrap;
using KeepShell.Mcp;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using KeepShell.Views.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.Views;
using PoproshaykaBot.Wpf.Views.Settings;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KeepShellMcpOptions = KeepShell.Mcp.McpServerOptions;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class VisualPagesSmokeTests
{
    private ResourceDictionary[]? _previousMergedDictionaries;
    private string? _previousThemeKey;
    private double _previousScale;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _previousMergedDictionaries = Application.Current?.Resources.MergedDictionaries.ToArray();
        _previousThemeKey = ThemeManager.Current?.Key;
        _previousScale = FontScaleManager.Current;

        var application = TestResources.Ensure();

        if (_previousMergedDictionaries is { Length: > 0 })
        {
            application.Resources.MergedDictionaries.Clear();
            foreach (var source in AppResources.All)
            {
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = source });
            }
        }

        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);
        ThemeManager.Apply(ThemeManager.DefaultLight.Key);
        FontScaleManager.Initialize(FontScaleManager.DefaultScale);
        AppScaledSizes.Register(application);
    }

    [TearDown]
    public void TearDown()
    {
        FontScaleManager.Apply(FontScaleManager.DefaultScale);
        ThemeManager.Apply(ThemeManager.DefaultLight.Key);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        var application = Application.Current;
        if (application is null)
        {
            return;
        }

        if (_previousMergedDictionaries is { Length: > 0 })
        {
            application.Resources.MergedDictionaries.Clear();
            foreach (var dictionary in _previousMergedDictionaries)
            {
                application.Resources.MergedDictionaries.Add(dictionary);
            }
        }

        ThemeManager.Apply(_previousThemeKey ?? ThemeManager.DefaultLight.Key);
        FontScaleManager.Apply(_previousScale);
        AppScaledSizes.Register(application);
    }

    [TestCase(1.0)]
    [TestCase(1.6)]
    public void AppearanceSettingsView_отрисовывается_и_переключает_темы_при_любом_масштабе(double scale)
    {
        FontScaleManager.Apply(scale);

        var settings = new MemorySettings();
        using var themeSelection = new ThemeSelectionViewModel(settings);
        var vm = new AppearanceSettingsSectionViewModel(themeSelection, new ShellPreferences(settings));

        using var bindingErrors = BindingErrorSink.Attach();
        bindingErrors.Clear();

        var view = new AppearanceSettingsView { DataContext = vm };
        var window = CreateHostWindow(new ScrollViewer { Content = view });

        using var host = VisualTestHost.Show(window, 1024, 640);
        host.Settle();

        var switcher = Descendants<ThemeSwitcher>(view).Single();
        var fontScaleField = Descendants<FontScaleField>(view).Single();
        var toggleStyle = Application.Current!.TryFindResource("CheckBox.ToggleSwitch");
        var toggles = Descendants<CheckBox>(view)
            .Where(static toggle => toggle.Content is "Показывать заголовок страницы" or "Показывать всплывающие уведомления")
            .ToArray();
        var lightBg = PaletteBrushColor(ThemeManager.DefaultLight.Palette);
        var darkBg = PaletteBrushColor(ThemeManager.DefaultDark.Palette);

        Assert.Multiple(() =>
        {
            Assert.That(switcher.ActualWidth, Is.GreaterThan(0));
            Assert.That(switcher.ActualHeight, Is.GreaterThan(0));
            Assert.That(fontScaleField.ActualWidth, Is.GreaterThan(0));
            Assert.That(fontScaleField.ActualHeight, Is.GreaterThan(0));
            Assert.That(toggles, Has.Length.EqualTo(2));
            Assert.That(toggles.All(toggle => ReferenceEquals(toggle.Style, toggleStyle) && toggle.ActualWidth > 0 && toggle.ActualHeight > 0), Is.True);
            Assert.That(BackgroundColor(window), Is.EqualTo(lightBg));
        });

        themeSelection.Select(ThemeManager.DefaultDark.Key);
        host.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(ThemeManager.Current?.Key, Is.EqualTo(ThemeManager.DefaultDark.Key));
            Assert.That(BackgroundColor(window), Is.EqualTo(darkBg));
        });

        themeSelection.Select(ThemeManager.DefaultLight.Key);
        host.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(ThemeManager.Current?.Key, Is.EqualTo(ThemeManager.DefaultLight.Key));
            Assert.That(BackgroundColor(window), Is.EqualTo(lightBg));
            Assert.That(bindingErrors.Count, Is.Zero, () => bindingErrors.Report);
        });
    }

    [Test]
    public async Task AutoMessages_сохраняет_выбор_и_даёт_доступ_с_клавиатуры()
    {
        var directory = Directory.CreateTempSubdirectory("pb-visual-auto-messages-");
        try
        {
            var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(directory.FullName, "settings.json"));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var registry = new ChatPlatformRegistry([]);
            var targets = new DefaultBroadcastPlatformTargetsProvider();
            await using var scheduler = new BroadcastScheduler(
                new BroadcastMessenger(registry, NullLogger<BroadcastMessenger>.Instance), targets, settings, bus,
                TimeProvider.System, NullLogger<BroadcastScheduler>.Instance);
            var prompt = new FakeUnsavedChangesPrompt { Answer = UnsavedChangesDecision.Stay };
            using var page = new AutoMessagesPageViewModel(
                settings, new PollsStore(filePath: Path.Combine(directory.FullName, "polls.json")), prompt, registry,
                targets, new BroadcastProfilesStore(filePath: Path.Combine(directory.FullName, "broadcast-profiles.json")),
                scheduler, new BotMessagingNavigation(), bus, NullLogger<AutoMessagesPageViewModel>.Instance,
                new StubBroadcastState(false), new StubDialogService());
            var view = new AutoMessagesPageView { DataContext = page };
            using var errors = BindingErrorSink.Attach();
            errors.Clear();
            using var host = VisualTestHost.Show(CreateHostWindow(view), 760, 480);
            host.Settle();

            var list = Descendants<ListBox>(view).Single();
            var editor = Descendants<TextBox>(view).Single(box => AutomationProperties.GetName(box) == "Текст сообщения");
            editor.Text = "Привет, {username}!";
            host.Settle();
            list.SelectedItem = page.Options.Single(item => item.Key == "farewell");
            host.Settle();
            var save = Descendants<Button>(view).Single(button => button.Content is "Сохранить");

            Assert.Multiple(() =>
            {
                Assert.That(page.SelectedOption.Key, Is.EqualTo("welcome"));
                Assert.That(list.SelectedItem, Is.SameAs(page.SelectedOption));
                Assert.That(page.Preview, Is.EqualTo("Привет, Алекс!"));
                Assert.That(editor.Focusable && KeyboardNavigation.GetIsTabStop(editor), Is.True);
                Assert.That(save.IsEnabled && save.Focusable && KeyboardNavigation.GetIsTabStop(save), Is.True);
                Assert.That(errors.Count, Is.Zero, () => errors.Report);
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public void CommandsPageView_управляет_опросом_через_IsVisibleChanged_без_ошибок_привязки()
    {
        var directory = Directory.CreateTempSubdirectory("pb-visual-commands-");

        try
        {
            var settingsManager = new SettingsManager(
                NullLogger<SettingsManager>.Instance,
                Path.Combine(directory.FullName, "settings.json"));
            var commandStore = new CommandSettingsStore(null, Path.Combine(directory.FullName, "commands.json"));
            var usage = new CommandUsageRepository(TimeProvider.System);
            var processor = new ChatCommandProcessor(
                [new StubChatCommand("помощь"), new StubChatCommand("баланс")],
                commandStore,
                usage,
                NullLogger<ChatCommandProcessor>.Instance,
                "!");
            var dispatcher = new ManualUiDispatcher();

            var vm = new CommandsPageViewModel(
                processor,
                commandStore,
                settingsManager,
                usage,
                new FakeUnsavedChangesPrompt(),
                new AccountsStore(null, Path.Combine(directory.FullName, "accounts.json")),
                NullLogger<CommandsPageViewModel>.Instance,
                uiDispatcher: dispatcher);

            using var bindingErrors = BindingErrorSink.Attach();
            bindingErrors.Clear();

            var view = new CommandsPageView { DataContext = vm };
            var window = CreateHostWindow(view);

            using var host = VisualTestHost.Show(window, 1024, 640);
            host.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(view.ActualWidth, Is.GreaterThan(0));
                Assert.That(view.ActualHeight, Is.GreaterThan(0));
                Assert.That(dispatcher.Timers, Has.Count.EqualTo(1));
                Assert.That(dispatcher.Timers[0].IsRunning, Is.True);
            });

            view.Visibility = Visibility.Collapsed;
            host.Settle();
            Assert.That(dispatcher.Timers[0].IsRunning, Is.False);

            view.Visibility = Visibility.Visible;
            host.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(dispatcher.Timers[0].IsRunning, Is.True);
                Assert.That(bindingErrors.Count, Is.Zero, () => bindingErrors.Report);
            });
        }
        finally
        {
            if (Directory.Exists(directory.FullName))
            {
                Directory.Delete(directory.FullName, recursive: true);
            }
        }
    }

    [TestCase(true, false, false, true, true)]
    [TestCase(false, true, true, true, false)]
    [TestCase(false, true, false, false, true)]
    public async Task AutoMessages_применяет_режим_к_текущему_таймеру(
        bool live, bool initiallyActive, bool confirm, bool expectedEnabled, bool expectedActive)
    {
        var directory = Directory.CreateTempSubdirectory("pb-broadcast-mode-");
        try
        {
            var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(directory.FullName, "settings.json"));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var registry = new ChatPlatformRegistry([new StubBroadcastPlatform()]);
            var targets = new DefaultBroadcastPlatformTargetsProvider();
            await using var scheduler = new BroadcastScheduler(
                new BroadcastMessenger(registry, NullLogger<BroadcastMessenger>.Instance), targets, settings, bus,
                TimeProvider.System, NullLogger<BroadcastScheduler>.Instance);
            using var page = new AutoMessagesPageViewModel(
                settings, new PollsStore(filePath: Path.Combine(directory.FullName, "polls.json")),
                new FakeUnsavedChangesPrompt(), registry, targets,
                new BroadcastProfilesStore(filePath: Path.Combine(directory.FullName, "broadcast-profiles.json")),
                scheduler, new BotMessagingNavigation(), bus, NullLogger<AutoMessagesPageViewModel>.Instance,
                new StubBroadcastState(live), new StubDialogService(confirm));
            if (initiallyActive)
            {
                scheduler.Start("test-channel");
            }

            Assert.That(page.TryOpen("broadcast"), Is.True);
            page.Enabled = true;
            Assert.That(page.TrySaveDraft(), Is.EqualTo(expectedEnabled));

            Assert.Multiple(() =>
            {
                Assert.That(settings.Current.Twitch.AutoBroadcast.AutoBroadcastEnabled, Is.EqualTo(expectedEnabled));
                Assert.That(scheduler.IsActive, Is.EqualTo(expectedActive));
                if (expectedEnabled && !expectedActive)
                {
                    Assert.That(page.BroadcastStatus, Does.Contain("Автоматический режим включён"));
                }
            });
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public void McpSettingsSectionView_при_1024x640_и_1_6x_размещает_SettingRowPanel_и_ToggleSwitch_без_ошибок_привязки()
    {
        FontScaleManager.Apply(1.6);

        var options = new KeepShellMcpOptions
        {
            ServerName = "poproshaykabot",
            AppName = AppInfo.Name,
            AppVersion = AppInfo.Version,
            DefaultPort = 7656,
            ShotsDirectory = Path.Combine(Path.GetTempPath(), "pb-visual-mcp-shots"),
        };
        var preferences = new McpPreferences(new MemorySettings(), options, isElevated: false);
        using var server = new McpServerHost(
            preferences,
            automation: null,
            dispatch: null,
            options,
            new EmptyServiceProvider(),
            NullLoggerFactory.Instance,
            NullLogger<McpServerHost>.Instance);
        using var vm = new McpSettingsSectionViewModel(
            preferences,
            server,
            options,
            new StubDialogService(),
            new StubClipboardService());

        using var bindingErrors = BindingErrorSink.Attach();
        bindingErrors.Clear();

        var view = new McpSettingsSectionView { DataContext = vm };
        var window = CreateHostWindow(new ScrollViewer { Content = view });

        using var host = VisualTestHost.Show(window, 1024, 640);
        host.Settle();

        var rows = Descendants<SettingRowPanel>(view).ToArray();
        var toggles = Descendants<CheckBox>(view).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(rows, Is.Not.Empty);
            Assert.That(rows.All(static row => row.ActualWidth > 0 && row.ActualHeight > 0), Is.True);
            Assert.That(toggles, Is.Not.Empty);
            Assert.That(toggles.All(static toggle => toggle.ActualWidth > 0 && toggle.ActualHeight > 0), Is.True);
            Assert.That(bindingErrors.Count, Is.Zero, () => bindingErrors.Report);
        });
    }

    private static Window CreateHostWindow(FrameworkElement content)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            Content = content,
        };
        window.SetResourceReference(Control.BackgroundProperty, "Bg.Base");
        window.SetResourceReference(Control.ForegroundProperty, "Fg.Primary");
        window.SetResourceReference(Control.FontFamilyProperty, "Font.Sans");
        window.SetResourceReference(Control.FontSizeProperty, "Font.Size.M");
        return window;
    }

    private static Color BackgroundColor(Control control) =>
        ((SolidColorBrush)control.Background).Color;

    private static Color PaletteBrushColor(Uri paletteUri)
    {
        var palette = new ResourceDictionary { Source = paletteUri };
        return ((SolidColorBrush)palette["Bg.Base"]).Color;
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

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class StubChatCommand(string canonical) : IChatCommand
    {
        public string Canonical => canonical;
        public IReadOnlyCollection<string> Aliases => [];
        public string Description => canonical;
        public PlatformCapabilities RequiredCapabilities => PlatformCapabilities.None;
        public bool CanExecute(CommandContext context) => true;
        public Task<OutgoingMessage?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken) =>
            Task.FromResult<OutgoingMessage?>(null);
    }

    private sealed class StubClipboardService : IClipboardService
    {
        public bool TrySetText(string? text) => true;
    }

    private sealed class StubDialogService(bool? answer = null) : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => answer ?? defaultYes;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => defaultYes;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }

    private sealed record StubBroadcastState(bool IsAnyLive) : IBroadcastState
    {
        public Guid? CurrentBroadcastId => null;
        public IReadOnlySet<StreamingPlatform> LivePlatforms =>
            IsAnyLive ? new HashSet<StreamingPlatform> { StreamingPlatform.Twitch } : new HashSet<StreamingPlatform>();
        public bool IsPlatformLive(StreamingPlatform platform) => IsAnyLive && platform == StreamingPlatform.Twitch;
    }

    private sealed class StubBroadcastPlatform : IChatPlatform
    {
        public StreamingPlatform Id => StreamingPlatform.Twitch;
        public string DisplayName => "Twitch";
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => true;
        public bool IsConnected => true;
        public IStreamStatus StreamStatus => throw new NotSupportedException();
        public IPlatformMessenger Messenger => throw new NotSupportedException();
        public string ConfiguredChannel => "test-channel";
    }
}
