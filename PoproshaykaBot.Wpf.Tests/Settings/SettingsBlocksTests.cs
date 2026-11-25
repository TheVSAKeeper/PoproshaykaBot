using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Settings.Update;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Settings.Contributors;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views;
using PoproshaykaBot.Wpf.Views.Settings;
using UpdateCandidate = KeepShell.Updates.UpdateCandidate;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class SettingsBlocksTests
{
    [Test]
    public void PlatformSettingsUseRegisteredProvidersAndTheirOwnBlocks()
    {
        var vk = new VkSettingsPlatform();
        var catalog = new SettingsSectionBlockDescriptor("channels/catalog", "Каналы", null, new object());
        var credentials = new SettingsSectionBlockDescriptor("vk/credentials", "Приложение VK", null, new object())
        {
            Platform = StreamingPlatform.VkLive,
        };
        var extracted = PlatformSettingsPages.Create(
        [
            new(new("channels", "Каналы", PackIconLucideKind.Layers, ""), [catalog], false),
            new(new("vk", "VK Видео Live", PackIconLucideKind.Layers, ""), [credentials], false),
        ], profiles: null, registry: new ChatPlatformRegistry([vk]));

        var presentation = extracted.PlatformPresentations.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(presentation.Group?.Title, Is.EqualTo("VK Видео Live"));
            Assert.That(PlatformSettingsPages.GetPlatform(presentation.Group), Is.EqualTo(StreamingPlatform.VkLive));
            Assert.That(presentation.Blocks, Is.EqualTo(new[] { catalog, credentials }));
            Assert.That(extracted.LinkTargets.Select(t => t.Key), Is.EqualTo(new[] { "vklive" }));
            Assert.That(PlatformVisuals.Get(StreamingPlatform.VkLive).Platform, Is.EqualTo(StreamingPlatform.VkLive));
            Assert.That(PlatformVisuals.ResolveGeometry(StreamingPlatform.VkLive),
                Is.Not.SameAs(PlatformVisuals.ResolveGeometry(StreamingPlatform.Twitch)));
        }
    }

    private sealed class VkSettingsPlatform : IChatPlatform
    {
        public StreamingPlatform Id => StreamingPlatform.VkLive;
        public string DisplayName => "VK Видео Live";
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => true;
        public bool IsConnected => false;
        public IStreamStatus StreamStatus => throw new NotSupportedException();
        public IPlatformMessenger Messenger => throw new NotSupportedException();
    }

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

    private string _root = null!;

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
    }

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "poproshayka-settings-blocks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void Каждому_подпункту_рейла_отвечает_якорь_блока_в_разметке()
    {
        using var pageVm = CreateFullPageViewModel();
        var scrollSections = pageVm.Sections.Items.Where(section => section.Key != "platforms").ToArray();
        var expected = scrollSections
            .SelectMany(section => section.Children.Select(child => $"{section.Key}/{child.Key}"))
            .ToArray();

        var view = new SettingsPageView
        {
            DataContext = pageVm,
        };

        var anchors = new List<string>();
        foreach (var section in scrollSections)
        {
            pageVm.Sections.Selected = section;
            Render(view);
            CollectVisible(view, anchors);
        }

        Assert.Multiple(() =>
        {
            Assert.That(anchors, Is.EquivalentTo(expected),
                "Ключи блоков разметки разошлись с подпунктами рейла: прокрутка к блоку и подсветка подпункта молча перестают работать.");
            Assert.That(pageVm.SectionPresentations.Single(p => p.Section.Key == "dashboard").IsFullWidth, Is.True);
        });
    }

    [Test]
    public void Площадки_в_настройках_одной_секцией_без_подпунктов_ищутся_по_словам_и_маршрутизируются()
    {
        using var pageVm = CreateFullPageViewModel();
        var section = pageVm.Sections[PlatformSettingsPages.SectionKey];
        var requested = new List<StreamingPlatform?>();
        pageVm.MultistreamRequested += (_, platform) => requested.Add(platform);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(section.Title, Is.EqualTo("Площадки"));
            Assert.That(pageVm.PlatformsCardTitle, Is.EqualTo("Площадки и мультистрим"));
            Assert.That(section.Children, Is.Empty);
            Assert.That(pageVm.Sections.Items.Select(item => item.Key),
                Has.None.Matches<string>(key => key is "oauth" or "youtube" or "channels"));
            Assert.That(pageVm.PlatformLinkTargets.Select(t => t.Key), Is.EqualTo(new[] { "twitch", "youtube", "multistream" }));
        }

        foreach (var term in new[] { "oauth", "квота", "профили" })
        {
            pageVm.Sections.Filter(term);
            Assert.That(section.IsVisible, Is.True, $"Поиск по «{term}» должен находить раздел «Площадки».");
        }
        pageVm.Sections.Filter(string.Empty);

        foreach (var legacy in new[]
        {
            "oauth/app",
            "youtube/project",
            "channels/catalog",
            "channels/groups",
            "basic/channel",
            "stream/auto",
            "stream/lifecycle",
            "platforms/twitch",
            "platforms/youtube",
            "platforms/multistream",
        })
        {
            Assert.That(PlatformSettingsPages.RestorePath(legacy), Is.EqualTo(PlatformSettingsPages.SectionKey),
                $"Устаревший путь «{legacy}» должен восстанавливаться на «{PlatformSettingsPages.SectionKey}».");
        }

        pageVm.NavigateToMultistreamCommand.Execute(new PlatformLinkTarget("twitch", "Twitch", StreamingPlatform.Twitch));
        pageVm.NavigateToMultistreamCommand.Execute(new PlatformLinkTarget("youtube", "YouTube", StreamingPlatform.YouTube));
        pageVm.NavigateToMultistreamCommand.Execute(new PlatformLinkTarget("multistream", "Профили и автоматика", null));

        Assert.That(requested, Is.EqualTo(new StreamingPlatform?[]
        {
            StreamingPlatform.Twitch,
            StreamingPlatform.YouTube,
            null,
        }));
    }

    [Test]
    public void Смена_DataContext_сбрасывает_кеш_блоков_и_подхватывает_новую_секцию()
    {
        using var firstVm = CreateSingleContributorPageViewModel("alpha", "one", "Первый");
        using var secondVm = CreateSingleContributorPageViewModel("beta", "two", "Второй");

        var view = new SettingsPageView
        {
            DataContext = firstVm,
        };
        Render(view);

        var blocksMethod = typeof(SettingsPageView).GetMethod("Blocks", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var initialBlocks = (IReadOnlyList<(string Key, FrameworkElement Element)>)blocksMethod.Invoke(view, null)!;
        Assert.That(initialBlocks.Select(block => block.Key), Is.EquivalentTo(["alpha/one"]));

        view.DataContext = secondVm;
        Render(view);

        var updatedBlocks = (IReadOnlyList<(string Key, FrameworkElement Element)>)blocksMethod.Invoke(view, null)!;
        Assert.That(updatedBlocks.Select(block => block.Key), Is.EquivalentTo(["beta/two"]));
    }

    [Test]
    public void Невыбранные_разделы_не_строят_визуальное_дерево_до_первого_открытия_и_сохраняют_его_после_ухода()
    {
        using var pageVm = CreateFullPageViewModel();
        var view = new SettingsPageView { DataContext = pageVm };
        Render(view);

        var sectionsHost = (ItemsControl)view.FindName("SectionsHost")!;
        var initialKey = pageVm.Sections.Selected!.Key;
        var presenters = new List<(string Key, ContentPresenter Presenter)>();

        for (var i = 0; i < pageVm.SectionPresentations.Count; i++)
        {
            var presenter = (ContentPresenter)sectionsHost.ItemContainerGenerator.ContainerFromIndex(i);
            presenters.Add((pageVm.SectionPresentations[i].Section.Key, presenter));
        }

        var initialPresenter = presenters.Single(p => p.Key == initialKey).Presenter;
        Assert.Multiple(() =>
        {
            Assert.That(initialPresenter.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(VisualTreeHelper.GetChildrenCount(initialPresenter), Is.EqualTo(1));
            Assert.That(presenters.Where(p => p.Key != initialKey).Select(p => VisualTreeHelper.GetChildrenCount(p.Presenter)),
                Is.All.EqualTo(0),
                "Неоткрытые разделы не должны строить визуальное дерево до выбора");
        });

        var firstRoot = VisualTreeHelper.GetChild(initialPresenter, 0);

        pageVm.Sections.Selected = pageVm.Sections["obs"];
        Render(view);

        var obsPresenter = presenters.Single(p => p.Key == "obs").Presenter;
        Assert.Multiple(() =>
        {
            Assert.That(obsPresenter.Visibility, Is.EqualTo(Visibility.Visible));
            Assert.That(VisualTreeHelper.GetChildrenCount(obsPresenter), Is.EqualTo(1));
            Assert.That(initialPresenter.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(presenters.Where(p => p.Key != initialKey && p.Key != "obs")
                    .Select(p => VisualTreeHelper.GetChildrenCount(p.Presenter)),
                Is.All.EqualTo(0));
        });

        pageVm.Sections.Selected = pageVm.Sections[initialKey];
        Render(view);

        Assert.That(VisualTreeHelper.GetChild(initialPresenter, 0), Is.SameAs(firstRoot),
            "Повторное открытие раздела должно возвращать уже построенное дерево");
    }

    [Test]
    public void Прокрутка_к_подпункту_нераскрытого_раздела_материализует_блоки_и_находит_якорь()
    {
        using var pageVm = CreateFullPageViewModel();
        var view = new SettingsPageView { DataContext = pageVm };
        var window = new Window
        {
            Content = view,
            Width = 1100,
            Height = 120,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
        };

        try
        {
            window.Show();
            view.UpdateLayout();

            var findBlock = typeof(SettingsPageView).GetMethod("FindBlock", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var scrollToBlock = typeof(SettingsPageView).GetMethod("ScrollToBlock", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var sectionScroll = (ScrollViewer)view.FindName("SectionScroll")!;

            var targetSection = pageVm.Sections.Items.Last(s => s.Children.Count >= 2 && s.Key != pageVm.Sections.Selected?.Key);
            pageVm.Sections.Selected = targetSection;
            var lastChild = targetSection.Children[^1];
            scrollToBlock.Invoke(view, [lastChild]);
            view.UpdateLayout();

            var anchor = (FrameworkElement?)findBlock.Invoke(view, [$"{targetSection.Key}/{lastChild.Key}"]);
            Assert.Multiple(() =>
            {
                Assert.That(anchor, Is.Not.Null);
                Assert.That(anchor!.IsVisible, Is.True);
                Assert.That(sectionScroll.VerticalOffset, Is.GreaterThan(0));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [TestCase(0, false, "channel", TestName = "Спай_в_начале_карточки_берёт_первый_блок")]
    [TestCase(100, false, "channel", TestName = "Спай_держит_блок_пока_следующий_заголовок_ниже_кромки")]
    [TestCase(180, false, "limits", TestName = "Спай_переходит_на_блок_чуть_раньше_его_кромки")]
    [TestCase(420, false, "messages", TestName = "Спай_берёт_последний_заголовок_выше_кромки")]
    public void Видимый_блок_считается_по_верхнему_краю_карточки(double offset, bool atBottom, string expected)
    {
        (string Key, double Top)[] blocks = [("channel", 0), ("limits", 200), ("messages", 420)];

        Assert.That(SettingsBlock.VisibleAt(blocks, offset, atBottom), Is.EqualTo(expected));
    }

    [Test]
    public void Дно_прокрутки_подсвечивает_последний_блок_даже_если_его_заголовок_выше_кромки()
    {
        (string Key, double Top)[] blocks = [("channel", 0), ("limits", 200), ("messages", 420)];

        Assert.That(SettingsBlock.VisibleAt(blocks, 300, true), Is.EqualTo("messages"),
            "Короткий последний блок иначе остался бы неподсвеченным: до его верхнего края прокрутка не доходит.");
    }

    private static void Render(SettingsPageView view)
    {
        view.Measure(new(1100, 720));
        view.Arrange(new(new Point(), new Size(1100, 720)));
        view.UpdateLayout();
    }

    private SettingsPageViewModel CreateSingleContributorPageViewModel(string sectionKey, string childKey, string title)
    {
        var gate = new SettingsWriteGate();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, $"settings-{sectionKey}.json"), gate);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var kestrel = new KestrelHttpServer(null!, settingsManager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
        var contributor = new StubMultiBlockContributor(sectionKey, childKey, title);

        return new(
            [contributor],
            settingsManager,
            gate,
            kestrel,
            bus,
            NullLogger<SettingsPageViewModel>.Instance,
            DispatchProxy.Create<IDialogService, InertProxy>(),
            new MemorySettings());
    }

    private SettingsPageViewModel CreateFullPageViewModel()
    {
        var gate = new SettingsWriteGate();
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_root, "settings.json"), gate);
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var uiSettings = new MemorySettings();
        var dialogs = DispatchProxy.Create<IDialogService, InertProxy>();
        var kestrel = new KestrelHttpServer(null!, settingsManager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);

        var accountsStore = new AccountsStore(null, Path.Combine(_root, "accounts.json"), gate);
        var youtubeSettingsStore = new YouTubeSettingsStore(null, Path.Combine(_root, "youtube.json"), gate);
        var youtubeAccountsStore = new YouTubeAccountsStore(null, Path.Combine(_root, "youtube-accounts.json"), gate);
        var chatSettingsStore = new ChatSettingsStore(settingsManager, null, Path.Combine(_root, "chat.json"), gate);
        var obsChatStore = new ObsChatStore(bus, null, Path.Combine(_root, "obs-chat.json"), gate);
        var obsIntegrationStore = new ObsIntegrationStore(bus, null, Path.Combine(_root, "obs-integration.json"), gate);
        var updateStore = new UpdateStore(null, Path.Combine(_root, "update.json"), gate);
        var debugChannelStore = new DebugChannelStore(null, Path.Combine(_root, "debug-channel.json"));
        var pollsStore = new PollsStore(null, Path.Combine(_root, "polls.json"), gate);
        var layoutStore = new DashboardLayoutStore(null, Path.Combine(_root, "dashboard-layout.json"), gate);

        var mcpOptions = new KeepShell.Mcp.McpServerOptions { ServerName = "test", AppName = "test", AppVersion = "1.0", ShotsDirectory = Path.Combine(_root, "shots") };
        var mcpPreferences = new KeepShell.Mcp.McpPreferences(uiSettings, mcpOptions);

        var oauth = new OAuthSettingsViewModel(
            DispatchProxy.Create<ITwitchOAuthService, InertProxy>(),
            accountsStore,
            dialogs,
            DispatchProxy.Create<IShellLauncher, InertProxy>(),
            DispatchProxy.Create<IEmbeddedTwitchAuthDialog, InertProxy>(),
            new ManualUiDispatcher(),
            TimeProvider.System,
            NullLoggerFactory.Instance);

        var youTube = new YouTubeSettingsSectionViewModel(
            DispatchProxy.Create<IYouTubeOAuthService, InertProxy>(),
            DispatchProxy.Create<IShellLauncher, InertProxy>(),
            dialogs,
            new ManualUiDispatcher(),
            TimeProvider.System,
            NullLoggerFactory.Instance);

        var obsChat = new ObsChatSettingsSectionViewModel(obsChatStore, settingsManager, dialogs, null!);
        var obsIntegration = new ObsIntegrationSectionViewModel(null!, settingsManager, NullLogger<ObsIntegrationSectionViewModel>.Instance, dialogs, null!);
        var polls = new PollsSettingsSectionViewModel(pollsStore, TimeProvider.System);
        var updateEnv = new FakeUpdateEnvironment();
        var update = new UpdateSettingsSectionViewModel(
            new UpdateStoreSettingsAdapter(updateStore),
            new FakeUpdateCoordinator(),
            PoproshaykaBot.Core.Update.UpdateServiceCollectionExtensions.CreateOptions(updateEnv),
            updateEnv,
            updateStore,
            dialogs,
            new ManualUiDispatcher(),
            DispatchProxy.Create<IMainWindowController, InertProxy>());
        var debugChannel = new DebugChannelSectionViewModel(DebugChannelOverride.None, debugChannelStore, null!, TimeProvider.System, NullLogger<DebugChannelSectionViewModel>.Instance);
        var channelCatalogStore = new ChannelCatalogStore(null, Path.Combine(_root, "channels.json"), gate);
        var userStatistics = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);
        var channelCatalog = new ChannelCatalogSettingsSectionViewModel(channelCatalogStore);
        var channelGroups = new ChannelGroupsSettingsSectionViewModel(channelCatalogStore, userStatistics);
        var mcp = new McpSettingsSectionViewModel(mcpPreferences, new(mcpPreferences, null!, null!, mcpOptions, new EmptyServiceProvider()), mcpOptions, dialogs, null!);
        var dashboardLayout = new DashboardLayoutSectionViewModel(Array.Empty<DashboardTileViewModel>(), uiSettings, layoutStore);

        ISettingsSectionContributor[] contributors =
        [
            new BasicSettingsSectionContributor(new(), new(), new(), settingsManager, new FakeTargetChannelProvider()),
            new TwitchConnectionSettingsContributor(oauth, accountsStore, settingsManager, NullLogger<TwitchConnectionSettingsContributor>.Instance),
            new YouTubeConnectionSettingsContributor(youTube, youtubeSettingsStore, youtubeAccountsStore),
            new ChatSettingsSectionContributor(new(), chatSettingsStore),
            new ObsSettingsSectionContributor(obsIntegration, obsChat, new(kestrel, dialogs, null!), obsIntegrationStore, obsChatStore, dialogs),
            new StreamSettingsSectionContributor(new(), new(), polls),
            new RanksSettingsSectionContributor(new()),
            new ChannelsSettingsSectionContributor(channelCatalog, channelGroups, channelCatalogStore),
            new DashboardSettingsSectionContributor(dashboardLayout, new DashboardLayoutCoordinator(layoutStore)),
            new UpdateSettingsSectionContributor(update, updateStore),
            new DebugChannelSettingsSectionContributor(debugChannel, debugChannelStore),
            new McpSettingsSectionContributor(mcp),
            new AppearanceSettingsSectionContributor(new AppearanceSettingsSectionViewModel(new(uiSettings), new(uiSettings))),
            new MiscSettingsSectionContributor(new(settingsManager, null!, dialogs, null!, null!, null!)),
        ];

        return new(
            contributors,
            settingsManager,
            gate,
            kestrel,
            bus,
            NullLogger<SettingsPageViewModel>.Instance,
            dialogs,
            uiSettings);
    }

    private static void CollectVisible(DependencyObject root, List<string> anchors)
    {
        if (root is UIElement { Visibility: not Visibility.Visible })
        {
            return;
        }

        if (root is FrameworkElement element && SettingsBlock.GetKey(element) is { Length: > 0 } key)
        {
            anchors.Add(key);
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            CollectVisible(VisualTreeHelper.GetChild(root, index), anchors);
        }
    }

    public class InertProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            return targetMethod is not null
                && targetMethod.ReturnType != typeof(void)
                && targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }

    private sealed class FakeUpdateCoordinator : KeepShell.Updates.IUpdateCoordinator
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public bool IsUpdatable => false;

        public KeepShell.Updates.UpdateStage Stage => KeepShell.Updates.UpdateStage.Idle;

        public UpdateCandidate? Candidate => null;

        public KeepShell.Updates.PendingUpdate? Pending => null;

        public int Progress => 0;

        public string? Error => null;

        public bool RestorePending() => false;

        public Task<bool> CheckAsync(bool manual, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> DownloadAsync(CancellationToken cancellationToken) => Task.FromResult(false);

        public Task CancelPendingDownloadsAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public bool DiscardPending(Version? version = null) => false;

        public void Skip()
        {
        }

        public void Dismiss()
        {
        }
    }

    private sealed class FakeUpdateEnvironment : IUpdateEnvironment
    {
        public UpdateKind Kind => UpdateKind.Unsupported;
        KeepShell.Updates.UpdateKind KeepShell.Updates.IUpdateEnvironment.Kind => KeepShell.Updates.UpdateKind.None;
        public bool SelfContained => false;
        public string Architecture => "x64";
        public Version CurrentVersion { get; } = new(1, 0);
        public int RuntimeMajor => 10;
        public string ExecutablePath => "Bot.exe";
        public string StagingDirectory => "update";
        public string RepositorySlug => "owner/repo";
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
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

    private sealed class StubMultiBlockContributor(string sectionKey, string childKey, string title) : ISettingsSectionContributor
    {
        public int Order => 0;

        public string SectionKey => sectionKey;

        public SettingsSection CreateSection()
        {
            return new(sectionKey, title, PackIconLucideKind.Settings, string.Empty, [new SettingsSubsection(childKey, title)]);
        }

        public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
        {
            return [new($"{sectionKey}/{childKey}", title, null, new())];
        }

        public void Load(SettingsSessionState session) { }

        public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState) => new DummyDisposable();

        public void BeginSave(AppSettings targetSettings) { }

        public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten) => Task.FromResult(SettingsSectionSaveOutcome.Clean);

        private sealed class DummyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
