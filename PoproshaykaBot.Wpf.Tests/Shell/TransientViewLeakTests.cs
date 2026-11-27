using KeepShell.Services;
using KeepShell.Testing;
using KeepShell.ViewModels;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.Views;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class TransientViewLeakTests
{
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

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
    }

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "poproshayka-view-leaks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Test]
    public void Пользователи_отписываются_от_вью_модели_при_выгрузке_и_собираются_GC()
    {
        using var viewModel = CreateUsersViewModel();
        var weakRef = MountSwapAndDetach<UserStatisticsPageView>(viewModel, typeof(UserStatisticsPageViewModel), typeof(UserStatisticsPageView));

        CollectAndPump();

        Assert.That(weakRef.IsAlive, Is.False);
        GC.KeepAlive(viewModel);
    }

    [Test]
    public void История_стримов_отписывается_от_вью_модели_при_выгрузке_и_собираются_GC()
    {
        using var viewModel = CreateStreamsViewModel();
        var weakRef = MountSwapAndDetach<StreamHistoryPageView>(viewModel, typeof(StreamHistoryPageViewModel), typeof(StreamHistoryPageView));

        CollectAndPump();

        Assert.That(weakRef.IsAlive, Is.False);
        GC.KeepAlive(viewModel);
    }

    [Test]
    public void Настройки_переключают_разделы_через_OneWay_и_собираются_GC_после_выгрузки()
    {
        using var viewModel = CreateSettingsViewModel();
        var weakRef = MountSwapSettingsAndSwitchSection(viewModel);

        CollectAndPump();

        Assert.That(weakRef.IsAlive, Is.False);
        GC.KeepAlive(viewModel);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MountSwapAndDetach<TView>(object viewModel, Type viewModelType, Type viewType)
        where TView : FrameworkElement
    {
        var host = CreateTemplateHost(viewModelType, viewType);
        var window = CreateOffscreenWindow(host);

        try
        {
            window.Show();
            host.Content = viewModel;
            PumpDispatcher();
            host.UpdateLayout();

            var view = FindVisualChild<TView>(host);
            Assert.That(view, Is.Not.Null);

            var weakRef = new WeakReference(view);
            host.Content = "other";
            PumpDispatcher();
            host.UpdateLayout();

            return weakRef;
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference MountSwapSettingsAndSwitchSection(SettingsPageViewModel viewModel)
    {
        var host = CreateTemplateHost(typeof(SettingsPageViewModel), typeof(SettingsPageView));
        var window = CreateOffscreenWindow(host);

        try
        {
            window.Show();
            host.Content = viewModel;
            PumpDispatcher();
            host.UpdateLayout();

            var view = FindVisualChild<SettingsPageView>(host);
            Assert.That(view, Is.Not.Null);

            viewModel.Sections.Selected = viewModel.Sections["second"];
            PumpDispatcher();
            host.UpdateLayout();

            var hosts = FindVisualChildren<HeaderedContentControl>(view!).ToList();
            Assert.That(hosts.Any(control => control.IsVisible && Equals(control.Header, "Блок B1")), Is.True);

            var weakRef = new WeakReference(view);
            host.Content = "other";
            PumpDispatcher();
            host.UpdateLayout();

            return weakRef;
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static ContentControl CreateTemplateHost(Type viewModelType, Type viewType)
    {
        var host = new ContentControl();
        host.Resources.Add(new DataTemplateKey(viewModelType), new DataTemplate(viewModelType)
        {
            VisualTree = new FrameworkElementFactory(viewType),
        });
        host.Resources.Add(new DataTemplateKey(typeof(string)), new DataTemplate(typeof(string))
        {
            VisualTree = new FrameworkElementFactory(typeof(TextBlock)),
        });

        return host;
    }

    private static Window CreateOffscreenWindow(UIElement content)
    {
        return new()
        {
            Width = 1100,
            Height = 720,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStyle = WindowStyle.None,
            Content = content,
        };
    }

    [SuppressMessage("Major Code Smell", "S1215:Refactor the code to remove this use of 'GC.Collect'", Justification = "Тест на освобождение визуальных деревьев требует принудительной сборки для проверки WeakReference")]
    private static void CollectAndPump()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            PumpDispatcher();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }

        PumpDispatcher();
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.SystemIdle,
            new DispatcherOperationCallback(_ =>
            {
                frame.Continue = false;
                return null;
            }),
            null);
        Dispatcher.PushFrame(frame);
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        return FindVisualChildren<T>(root).FirstOrDefault();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private UserStatisticsPageViewModel CreateUsersViewModel()
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings-users.json"));
        var eventBus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var statistics = new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance);

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
            FakeAvatarCache.CreateProvider());
    }

    private StreamHistoryPageViewModel CreateStreamsViewModel()
    {
        var store = new BroadcastHistoryStore(
            null,
            Path.Combine(_directory, "broadcasts.json"),
            Path.Combine(_directory, "sessions.json"));

        return new(
            store,
            new UserStatisticsRepository(NullLogger<UserStatisticsRepository>.Instance),
            new MemorySettings(),
            new GameBoxArtProvider(new FakeBoxArtCache(), NullLogger<GameBoxArtProvider>.Instance),
            new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance));
    }

    private SettingsPageViewModel CreateSettingsViewModel()
    {
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_directory, "settings-page.json"));
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var kestrel = new KestrelHttpServer(null!, settingsManager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
        var uiSettings = new MemorySettings();

        ISettingsSectionContributor[] contributors =
        [
            new StubSectionContributor(0, "first", "Первый раздел", false, [("first/a", "Блок A")]),
            new StubSectionContributor(1, "second", "Второй раздел", true, [("second/a", "Блок B1"), ("second/b", "Блок B2")]),
        ];

        return new(
            contributors,
            settingsManager,
            kestrel,
            bus,
            NullLogger<SettingsPageViewModel>.Instance,
            DispatchProxy.Create<IDialogService, InertProxy>(),
            uiSettings);
    }

    private sealed class FakeChannelProvider : IChannelProvider
    {
        public string? Channel => "test";
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

    private sealed class StubSectionContributor(
        int order,
        string sectionKey,
        string title,
        bool isFullWidth,
        IReadOnlyList<(string Key, string Title)> blocks) : ISettingsSectionContributor
    {
        public int Order => order;

        public string SectionKey => sectionKey;

        public bool IsFullWidth => isFullWidth;

        public SettingsSection CreateSection()
        {
            var children = blocks.Count > 1
                ? blocks.Select(block => new SettingsSubsection(block.Key[(sectionKey.Length + 1)..], block.Title)).ToArray()
                : [];

            return new(sectionKey, title, PackIconLucideKind.Settings, string.Empty, children);
        }

        public IReadOnlyList<SettingsSectionBlockDescriptor> GetBlocks()
        {
            return [.. blocks.Select(block => new SettingsSectionBlockDescriptor(block.Key, block.Title, "Подсказка", new DummyContent(block.Title)))];
        }

        public void Load(SettingsSessionState session) { }

        public IDisposable SubscribeDirty(SettingsSessionState session, Action markDirty, Action notifyChangesState) => new DummyDisposable();

        public void BeginSave(AppSettings targetSettings) { }

        public Task<SettingsSectionSaveOutcome> SaveAsync(AppSettings targetSettings, bool appSettingsWritten) => Task.FromResult(SettingsSectionSaveOutcome.Clean);

        private sealed class DummyContent(string title)
        {
            public string Title { get; } = title;
        }

        private sealed class DummyDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }
}
