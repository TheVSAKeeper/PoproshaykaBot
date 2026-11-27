using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast.Profiles;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Streaming.Switching;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.Views.Tiles;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class StreamAndChannelsTileTests
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
    public async Task Отображает_спокойное_ожидание_и_завершение_эфира_без_красной_ошибки()
    {
        var dir = Directory.CreateTempSubdirectory("poproshayka-stream-channels-");
        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(dir.FullName, "channels.json"));
            Assert.That(catalog.Mutate(s =>
            {
                s.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC-1", DisplayName = "Основной", IsConfigured = true });
                s.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC-2", DisplayName = "Игровой", IsConfigured = true });
            }), Is.True);

            var twitchStatus = new MutableStreamStatus { CurrentStatus = StreamStatus.Online, CurrentStream = new() { Title = "Стрим", GameName = "Code", ViewerCount = 42, StartedAt = DateTime.UtcNow.AddMinutes(-5) } };
            var youtubeStatus = new MutableStreamStatus { CurrentStatus = StreamStatus.Unknown };

            var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit, true, true, twitchStatus, "streamer");
            var youtube = new StubPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.None, true, true, youtubeStatus, "UC-1");
            var registry = new StubPlatformRegistry(twitch, youtube);

            using var tile = new StreamAndChannelsTileViewModel(
                registry,
                catalog,
                bus,
                NullLogger<StreamAndChannelsTileViewModel>.Instance,
                new ManualUiDispatcher(),
                new InertShellLauncher());

            Assert.Multiple(() =>
            {
                Assert.That(tile.Title, Is.EqualTo("Эфир и каналы"));
                Assert.That(tile.YouTubeStatusText, Is.EqualTo("Ожидание эфира"));
                Assert.That(tile.YouTubeStatusSeverity, Is.Not.EqualTo(StatusSeverity.Error));
                Assert.That(tile.TwitchAnchorSeverity, Is.EqualTo(StatusSeverity.Success));
            });

            youtubeStatus.CurrentStatus = StreamStatus.Offline;
            await bus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "UC-1"));

            Assert.Multiple(() =>
            {
                Assert.That(tile.YouTubeStatusText, Is.EqualTo("Эфир завершён / не в сети"));
                Assert.That(tile.YouTubeStatusSeverity, Is.Not.EqualTo(StatusSeverity.Error));
                Assert.That(tile.TwitchAnchorSeverity, Is.EqualTo(StatusSeverity.Success));
            });
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [Test]
    public async Task Переключение_канала_YouTube_не_трогает_якорный_Twitch_и_имеет_явную_подпись()
    {
        var dir = Directory.CreateTempSubdirectory("poproshayka-yt-switch-");
        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var catalog = new ChannelCatalogStore(filePath: Path.Combine(dir.FullName, "channels.json"));
            Assert.That(catalog.Mutate(s =>
            {
                s.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC-1", DisplayName = "Образование", IsConfigured = true });
                s.Channels.Add(new() { Platform = StreamingPlatform.YouTube, ChannelId = "UC-2", DisplayName = "Игры", IsConfigured = true });
                s.Platforms.Add(new() { Platform = StreamingPlatform.YouTube, ActiveChannelId = "UC-1" });
            }), Is.True);

            var twitchStatus = new MutableStreamStatus { CurrentStatus = StreamStatus.Online, CurrentStream = new() { Title = "Twitch", ViewerCount = 10, StartedAt = DateTime.UtcNow } };
            var youtubeStatus = new MutableStreamStatus { CurrentStatus = StreamStatus.Unknown };

            var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit, true, true, twitchStatus, "streamer");
            var youtube = new StubPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.None, true, true, youtubeStatus, "UC-1");
            var registry = new StubPlatformRegistry(twitch, youtube);
            var switchCoordinator = new RecordingSwitchCoordinator();

            using var tile = new StreamAndChannelsTileViewModel(
                registry,
                catalog,
                bus,
                NullLogger<StreamAndChannelsTileViewModel>.Instance,
                new ManualUiDispatcher(),
                new InertShellLauncher(),
                switchCoordinator: switchCoordinator);

            var view = new StreamAndChannelsTileView { DataContext = tile };
            view.Measure(new(360, 400));
            view.Arrange(new(0, 0, 360, view.DesiredSize.Height));
            view.UpdateLayout();

            var label = FindVisualChildren<TextBlock>(view).FirstOrDefault(t => t.Text == "Канал YouTube");
            var combo = FindVisualChildren<ComboBox>(view).FirstOrDefault();

            Assert.Multiple(() =>
            {
                Assert.That(label, Is.Not.Null);
                Assert.That(label!.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(label.ActualWidth, Is.GreaterThan(0));
                Assert.That(combo, Is.Not.Null);
                Assert.That(combo!.Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(combo.ActualWidth, Is.GreaterThan(0));
            });

            await tile.SwitchYouTubeChannelAsync(tile.YouTubeChannels[1]);

            Assert.Multiple(() =>
            {
                Assert.That(switchCoordinator.SwitchedCalls, Is.EqualTo(new[] { (StreamingPlatform.YouTube, "UC-2") }));
                Assert.That(tile.TwitchAnchorStatusText, Is.EqualTo("Якорь активен · эфир и чат"));
                Assert.That(tile.TwitchAnchorSeverity, Is.EqualTo(StatusSeverity.Success));
            });
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [Test]
    public async Task Профили_эфира_называются_во_множественном_числе_и_позволяют_повторить_сбой()
    {
        var dir = Directory.CreateTempSubdirectory("poproshayka-profiles-retry-");
        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var profilesStore = new BroadcastProfilesStore(null, Path.Combine(dir.FullName, "profiles.json"));
            var profile = new BroadcastProfile { Name = "Вечерний", Title = "Играем" };
            var applier = new FlakyApplier { ShouldFail = true };
            var manager = new BroadcastProfilesManager(profilesStore, applier, bus, TimeProvider.System, NullLogger<BroadcastProfilesManager>.Instance);
            manager.Upsert(profile, [StreamingPlatform.Twitch]);

            var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit, true, true, new MutableStreamStatus(), "streamer");
            var registry = new StubPlatformRegistry(twitch);

            var services = new ServiceCollection().BuildServiceProvider();
            using var vm = new BroadcastProfilesTileViewModel(
                manager,
                profilesStore,
                twitch.StreamStatus,
                applier,
                new InertChannelsApi(),
                new StaticBroadcasterId(),
                new InertDialogService(),
                services.GetRequiredService<IServiceScopeFactory>(),
                bus,
                registry);

            Assert.That(vm.Title, Is.EqualTo("Профили эфира"));

            await vm.Items[0].ApplyCommand.ExecuteAsync(null);
            await bus.PublishAsync(new BroadcastProfileApplyFailed(profile, "Сбой Twitch"));

            Assert.Multiple(() =>
            {
                Assert.That(vm.IsStatusError, Is.True);
                Assert.That(vm.CanRetryLastApply, Is.True);
                Assert.That(vm.RetryLastApplyCommand.CanExecute(null), Is.True);
            });

            applier.ShouldFail = false;
            await vm.RetryLastApplyCommand.ExecuteAsync(null);
            await bus.PublishAsync(new BroadcastProfileApplied(profile));

            Assert.Multiple(() =>
            {
                Assert.That(applier.ApplyCount, Is.EqualTo(2));
                Assert.That(vm.IsStatusError, Is.False);
                Assert.That(vm.CanRetryLastApply, Is.False);
            });
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (var nested in FindVisualChildren<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class RecordingSwitchCoordinator : IChannelSwitchCoordinator
    {
        public List<(StreamingPlatform Platform, string ChannelId)> SwitchedCalls { get; } = [];

        public AutoDiscoverPrompt? GetActivePrompt(StreamingPlatform platform) => null;
        public IReadOnlyList<AutoDiscoverPrompt> GetActivePrompts() => [];
        public Task<ChannelSwitchResult> SwitchActiveChannelAsync(StreamingPlatform platform, ChannelKey targetChannel, CancellationToken cancellationToken = default)
        {
            SwitchedCalls.Add((platform, targetChannel.ChannelId));
            return Task.FromResult(new ChannelSwitchResult(ChannelSwitchOutcome.Switched, targetChannel));
        }

        public Task<ChannelSwitchResult> SwitchActiveChannelAsync(StreamingPlatform platform, ChannelKey targetChannel, bool preservePreselectedNext, CancellationToken cancellationToken = default)
            => SwitchActiveChannelAsync(platform, targetChannel, cancellationToken);

        public Task<ChannelSwitchResult> SwitchCatalogChannelAsync(ChannelCatalogEntry targetChannel, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChannelSwitchResult(ChannelSwitchOutcome.Switched, targetChannel.Key));

        public Task<ChannelSwitchResult?> SetPreselectedNextAsync(StreamingPlatform platform, ChannelKey? targetChannel, CancellationToken cancellationToken = default)
            => Task.FromResult<ChannelSwitchResult?>(null);

        public Task SetSwitchModeAsync(StreamingPlatform platform, ChannelSwitchMode mode, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ChannelSwitchResult> ConfirmAutoDiscoverCandidateAsync(StreamingPlatform platform, ChannelKey candidateChannel, CancellationToken cancellationToken = default)
            => Task.FromResult(new ChannelSwitchResult(ChannelSwitchOutcome.Switched, candidateChannel));

        public Task DismissAutoDiscoverPromptAsync(StreamingPlatform platform, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task EvaluateStartupPendingAsync(StreamingPlatform platform, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FlakyApplier : IChannelInformationApplier
    {
        public bool ShouldFail { get; set; }
        public int ApplyCount { get; private set; }

        public Task<bool> ApplyAsync(BroadcastProfile profile, CancellationToken cancellationToken)
        {
            ApplyCount++;
            return Task.FromResult(!ShouldFail);
        }

        public Task<bool> ApplyPatchAsync(string? title, string? gameId, string? gameName, CancellationToken cancellationToken)
        {
            ApplyCount++;
            return Task.FromResult(!ShouldFail);
        }
    }

    private sealed class InertChannelsApi : ITwitchChannelsApi
    {
        public Task<ChannelInfo?> GetChannelInformationAsync(string broadcasterId, CancellationToken cancellationToken)
            => Task.FromResult<ChannelInfo?>(null);

        public Task ModifyChannelInformationAsync(string broadcasterId, PatchChannelRequest request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StaticBroadcasterId : IBroadcasterIdProvider
    {
        public Task<string?> GetAsync(CancellationToken cancellationToken) => Task.FromResult<string?>("123");
    }

    private sealed class InertDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);
        public bool Confirm(string title, string message, bool defaultYes = false) => true;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }

    private sealed class InertShellLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class MutableStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus { get; set; }
        public StreamInfo? CurrentStream { get; set; }
        public StreamStatusSnapshot GetSnapshot() => new(CurrentStatus, CurrentStream, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class StubPlatform(
        StreamingPlatform id,
        string displayName,
        PlatformCapabilities capabilities,
        bool isEnabled,
        bool isConnected,
        IStreamStatus streamStatus,
        string? configuredChannel) : IChatPlatform
    {
        public StreamingPlatform Id { get; } = id;
        public string DisplayName { get; } = displayName;
        public PlatformCapabilities Capabilities { get; } = capabilities;
        public bool IsEnabled { get; } = isEnabled;
        public bool IsConnected { get; } = isConnected;
        public string? ConfiguredChannel { get; } = configuredChannel;
        public IStreamStatus StreamStatus { get; } = streamStatus;
        public IPlatformMessenger Messenger => null!;
        public ChatConnectionStatus? CaptureChatStatus(BotLifecyclePhase phase) =>
            IsConnected
                ? new ChatConnectionStatus(ConnectionState.Connected, null, null, null, ChatReadIssue.None)
                : new ChatConnectionStatus(ConnectionState.Disconnected, null, null, null, ChatReadIssue.None);
    }

    private sealed class StubPlatformRegistry(params IChatPlatform[] platforms) : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All { get; } = platforms;
        public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            chatPlatform = platforms.FirstOrDefault(p => p.Id == platform);
            return chatPlatform is not null;
        }
        public IChatPlatform? Find(StreamingPlatform platform) => All.FirstOrDefault(p => p.Id == platform);
        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform) ?? throw new KeyNotFoundException();
        public IReadOnlyList<IChatPlatform> GetEnabled() => platforms.Where(static p => p.IsEnabled).ToArray();
        public IReadOnlyList<IChatPlatform> GetConnected() => platforms.Where(static p => p.IsConnected).ToArray();
    }
}
