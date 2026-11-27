using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using KeepShell.Services;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.Views;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class AutoBroadcastIndicatorTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Resources/PlatformIcons.xaml",
    ];

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
    }

    [Test]
    public async Task Отображает_выкл_авто_активный_отсчёт_и_паузу()
    {
        var dir = Directory.CreateTempSubdirectory("poproshayka-autobroadcast-indicator-");
        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(dir.FullName, "settings.json"));
            var targets = new StubTargetsProvider();
            var broadcastState = new StubBroadcastState();
            var time = new ManualTimeProvider();

            await using var scheduler = new BroadcastScheduler(
                new InertMessenger(),
                targets,
                settings,
                bus,
                time,
                NullLogger<BroadcastScheduler>.Instance);

            using var vm = new AutoBroadcastIndicatorViewModel(
                scheduler,
                settings,
                broadcastState,
                targets,
                new StubPlatformRegistry(),
                bus,
                new ManualUiDispatcher(),
                time: time);

            Assert.Multiple(() =>
            {
                Assert.That(vm.ShortText, Is.EqualTo("Рассылка: выкл"));
                Assert.That(vm.Severity, Is.EqualTo(StatusSeverity.None));
            });

            settings.Mutate(s =>
            {
                s.Twitch.AutoBroadcast.AutoBroadcastEnabled = true;
                s.Twitch.AutoBroadcast.BroadcastIntervalMinutes = 12;
            });
            await bus.PublishAsync(new BroadcastScheduleSettingsChanged());

            Assert.Multiple(() =>
            {
                Assert.That(vm.ShortText, Is.EqualTo("Рассылка: авто"));
                Assert.That(vm.Severity, Is.EqualTo(StatusSeverity.Info));
            });

            broadcastState.IsAnyLive = true;
            await bus.PublishAsync(new StreamWentOnline(StreamingPlatform.Twitch, "streamer", null!));

            Assert.Multiple(() =>
            {
                Assert.That(vm.ShortText, Is.EqualTo("Рассылка: пауза"));
                Assert.That(vm.Severity, Is.EqualTo(StatusSeverity.Warning));
            });

            scheduler.Start("streamer");
            await bus.PublishAsync(new BroadcastSchedulerStateChanged(true, "streamer", 0, scheduler.NextBroadcastTime));

            Assert.Multiple(() =>
            {
                Assert.That(vm.ShortText, Is.EqualTo("Рассылка: 12 мин"));
                Assert.That(vm.Severity, Is.EqualTo(StatusSeverity.Success));
                Assert.That(vm.Tooltip, Does.Contain("12 мин"));
            });
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [TestCase(940)]
    [TestCase(970)]
    public void Индикатор_рассылки_помещается_в_StatusBarView_при_узком_окне(double width)
    {
        var dir = Directory.CreateTempSubdirectory("poproshayka-statusbar-");
        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(dir.FullName, "settings.json"));
            var targets = new StubTargetsProvider();
            var broadcastState = new StubBroadcastState();
            var scheduler = new BroadcastScheduler(new InertMessenger(), targets, settings, bus, TimeProvider.System, NullLogger<BroadcastScheduler>.Instance);
            using var indicator = new AutoBroadcastIndicatorViewModel(
                scheduler,
                settings,
                broadcastState,
                targets,
                new StubPlatformRegistry(),
                bus,
                new ManualUiDispatcher());

            var statusBar = new StatusBarView
            {
                DataContext = new
                {
                    AutoBroadcastIndicator = indicator,
                    StatusSeverity = StatusSeverity.Info,
                    StatusText = "Бот готов к работе",
                    IsOverviewSelected = false,
                },
            };

            statusBar.Measure(new(width, 30));
            statusBar.Arrange(new(0, 0, width, 30));
            statusBar.UpdateLayout();

            var element = FindNamedElement(statusBar, "Авто-рассылка");
            Assert.Multiple(() =>
            {
                Assert.That(element, Is.Not.Null);
                Assert.That(element!.Visibility, Is.EqualTo(System.Windows.Visibility.Visible));
                Assert.That(element.ActualWidth, Is.GreaterThan(60));
            });
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    private static System.Windows.FrameworkElement? FindNamedElement(System.Windows.DependencyObject root, string automationName)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is System.Windows.FrameworkElement fe
                && string.Equals(System.Windows.Automation.AutomationProperties.GetName(fe), automationName, StringComparison.Ordinal))
            {
                return fe;
            }

            if (FindNamedElement(child, automationName) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private sealed class StubTargetsProvider : IBroadcastPlatformTargetsProvider
    {
        public IReadOnlySet<StreamingPlatform> GetTargetPlatforms() => new HashSet<StreamingPlatform> { StreamingPlatform.Twitch };
    }

    private sealed class StubBroadcastState : IBroadcastState
    {
        public bool IsAnyLive { get; set; }
        public Guid? CurrentBroadcastId => null;
        public IReadOnlySet<StreamingPlatform> LivePlatforms => IsAnyLive ? new HashSet<StreamingPlatform> { StreamingPlatform.Twitch } : new HashSet<StreamingPlatform>();
        public bool IsPlatformLive(StreamingPlatform platform) => IsAnyLive && platform == StreamingPlatform.Twitch;
    }

    private sealed class InertMessenger : IBroadcastMessenger
    {
        private static readonly BroadcastSendResult EmptyResult = new(new Dictionary<StreamingPlatform, ChatEnqueueResult>().ToFrozenDictionary());
        public ChatEnqueueResult Send(StreamingPlatform platform, string text, ChatSendCategory category = ChatSendCategory.None) => ChatEnqueueResult.Queued;
        public BroadcastSendResult Send(string text, IReadOnlySet<StreamingPlatform> platforms, ChatSendCategory category = ChatSendCategory.None) => EmptyResult;
        public BroadcastSendResult SendToConnected(string text, ChatSendCategory category = ChatSendCategory.None) => EmptyResult;
        public BroadcastSendResult Send(Func<IChatPlatform, string?> messageFactory, IReadOnlySet<StreamingPlatform>? platforms = null, ChatSendCategory category = ChatSendCategory.None) => EmptyResult;
    }

    private sealed class StubPlatformRegistry : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All { get; } = [];
        public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform) { chatPlatform = null; return false; }
        public IChatPlatform? Find(StreamingPlatform platform) => null;
        public IChatPlatform GetRequired(StreamingPlatform platform) => throw new KeyNotFoundException();
        public IReadOnlyList<IChatPlatform> GetEnabled() => [];
        public IReadOnlyList<IChatPlatform> GetConnected() => [];
    }
}
