using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Commands;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Core.YouTube.Streaming;
using PoproshaykaBot.Wpf.Infrastructure.Platforms;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.ViewModels.Tiles.Chat;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Http;

namespace PoproshaykaBot.Wpf.Tests.Multiplatform;

[TestFixture]
internal sealed class Stage8Layer2Tests
{
    [TestCase(StreamingPlatform.YouTube, "YouTube")]
    [TestCase(StreamingPlatform.VkLive, "VK Live")]
    public async Task StreamMonitoring_hides_icons_for_single_twitch_and_resets_disabled_platform_disconnect(
        StreamingPlatform platform,
        string displayName)
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit, true, StreamStatus.Offline, null, "streamer");
        var second = new StubPlatform(platform, displayName, PlatformCapabilities.None, false, StreamStatus.Offline, null, null);
        var registry = new StubPlatformRegistry(twitch, second);

        using var vm = new StreamMonitoringViewModel(bus, registry);

        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.Twitch, PlatformAuthRole.Bot, StreamMonitoringStatus.Connected));

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsVisible, Is.True);
            Assert.That(vm.ShowPlatformIcons, Is.False);
            Assert.That(vm.Groups[0].IsVisible, Is.True);
            Assert.That(vm.Groups[1].IsVisible, Is.False);
            Assert.That(vm.Groups[1].Platform, Is.EqualTo(platform));
            Assert.That(vm.Groups[1].DisplayName, Is.EqualTo(displayName));
        });

        second.IsEnabledValue = true;
        await bus.PublishAsync(new StreamMonitoringStatusChanged(platform, PlatformAuthRole.Broadcaster, StreamMonitoringStatus.Reconnecting, "повтор после ошибки API"));

        Assert.Multiple(() =>
        {
            Assert.That(vm.ShowPlatformIcons, Is.True);
            Assert.That(vm.Groups[1].IsVisible, Is.True);
            Assert.That(vm.Groups[1].Broadcaster.Severity, Is.EqualTo(StatusSeverity.Warning));
        });

        second.IsEnabledValue = false;
        await bus.PublishAsync(new StreamMonitoringStatusChanged(platform, PlatformAuthRole.Broadcaster, StreamMonitoringStatus.Disconnected));

        Assert.Multiple(() =>
        {
            Assert.That(vm.IsVisible, Is.True);
            Assert.That(vm.ShowPlatformIcons, Is.False);
            Assert.That(vm.Groups[1].IsVisible, Is.False);
        });
    }

    [Test]
    public async Task StreamMonitoring_YouTube_ResetsSupersededRole_WhenSwitchingFromBotToBroadcaster()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit, true, StreamStatus.Offline, null, "streamer");
        var youtube = new StubPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.None, true, StreamStatus.Offline, null, "UC_MAIN")
        {
            UsesSingleMonitoringRole = true,
        };
        var registry = new StubPlatformRegistry(twitch, youtube);

        using var vm = new StreamMonitoringViewModel(bus, registry);

        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.YouTube, PlatformAuthRole.Bot, StreamMonitoringStatus.Connected));
        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.YouTube, PlatformAuthRole.Bot, StreamMonitoringStatus.Disconnected));
        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster, StreamMonitoringStatus.Connected));

        Assert.Multiple(() =>
        {
            Assert.That(vm.Groups[1].Bot.IsReported, Is.False);
            Assert.That(vm.Groups[1].Broadcaster.IsReported, Is.True);
            Assert.That(vm.Groups[1].Broadcaster.Text, Is.EqualTo("Стример: работает"));
        });

        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.YouTube, PlatformAuthRole.Bot, StreamMonitoringStatus.Connected));
        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.YouTube, PlatformAuthRole.Broadcaster, StreamMonitoringStatus.Connected));
        await bus.PublishAsync(new StreamMonitoringStatusChanged(StreamingPlatform.YouTube, PlatformAuthRole.Bot, StreamMonitoringStatus.Disconnected));

        Assert.Multiple(() =>
        {
            Assert.That(vm.Groups[1].Bot.IsReported, Is.False);
            Assert.That(vm.Groups[1].Broadcaster.IsReported, Is.True);
            Assert.That(vm.Groups[1].Broadcaster.Text, Is.EqualTo("Стример: работает"));
        });
    }

    [Test]
    public async Task MultiplatformChatTile_handles_history_gaps_replacements_deletes_clear_and_cap()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"pb-stage8-chat-{Guid.NewGuid():N}.json");
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        try
        {
            var historySettings = new StubHistorySettingsProvider(new ChatSettings { ShowHistoryInApp = true });
            using var historyManager = new ChatHistoryManager(settingsManager, bus, historySettings);

            using var tile = new MultiplatformChatTileViewModel(historyManager, bus, historySettings);
            PlatformUserKey? requestedUser = null;
            tile.UserRequested += (_, key) => requestedUser = key;

            var scope = new ChatHistoryScope(StreamingPlatform.YouTube, "UC123", "yt-channel");
            var t0 = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
            var histEntry = new ChatMessageData
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "yt-channel",
                ChannelId = "UC123",
                MessageId = "h1",
                Timestamp = t0.UtcDateTime,
                UserId = "u-hist",
                DisplayName = "HistUser",
                Message = "Привет из истории",
                IsHistorical = true,
            };
            historyManager.ImportHistory(scope, [histEntry]);
            await bus.PublishAsync(new ChatHistoryLoaded(scope, [histEntry], [histEntry]));

            var liveEntry = new ChatMessageData
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "tw",
                ChannelId = "tw",
                MessageId = "m1",
                Timestamp = t0.AddMinutes(1).UtcDateTime,
                UserId = "u-live",
                DisplayName = "LiveUser",
                Message = "Первое сообщение",
            };
            await bus.PublishAsync(new ChatMessageReceived(StreamingPlatform.Twitch, "tw", "m1", "u-live", "live", "LiveUser", "Первое сообщение", UserStatus.None, false, liveEntry));

            var whisperEntry = new ChatMessageData
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "tw",
                ChannelId = "tw",
                MessageId = "w1",
                Timestamp = t0.AddMinutes(1).AddSeconds(5).UtcDateTime,
                UserId = "bot",
                DisplayName = "Bot",
                Message = "Шёпот",
                IsBot = true,
                CommandResponse = new CommandResponseMark("!bal", CommandResponseTarget.Whisper),
            };
            await bus.PublishAsync(new ChatMessageReceived(StreamingPlatform.Twitch, "tw", "w1", "bot", "bot", "Bot", "Шёпот", UserStatus.None, false, whisperEntry, IsBot: true, CommandResponse: whisperEntry.CommandResponse));

            Assert.That(tile.Items.OfType<ChatFeedHistoryBoundaryItemViewModel>().Count(), Is.EqualTo(1));
            Assert.That(tile.Items.OfType<ChatFeedMessageItemViewModel>().Select(static m => m.Key.MessageId), Is.EqualTo(new[] { "h1", "m1" }));

            var replacement = new ChatMessageData
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "tw",
                ChannelId = "tw",
                MessageId = "m1",
                Timestamp = t0.AddMinutes(1).UtcDateTime,
                UserId = "u-live",
                DisplayName = "LiveUser",
                Message = "Исправленное сообщение",
            };
            await bus.PublishAsync(new ChatMessageReceived(StreamingPlatform.Twitch, "tw", "m1", "u-live", "live", "LiveUser", "Исправленное сообщение", UserStatus.None, false, replacement, IsReplacement: true));

            var liveVm = tile.Items.OfType<ChatFeedMessageItemViewModel>().Single(m => m.Key.MessageId == "m1");
            Assert.That(liveVm.MessageText, Is.EqualTo("Исправленное сообщение"));

            liveVm.OpenUserCommand.Execute(null);
            Assert.That(requestedUser, Is.EqualTo(new PlatformUserKey(StreamingPlatform.Twitch, "u-live")));

            await bus.PublishAsync(new ChatReadingGapDetected(StreamingPlatform.YouTube, "yt-channel", "lc1", t0.AddMinutes(2)));
            await bus.PublishAsync(new ChatReadingGapDetected(StreamingPlatform.YouTube, "yt-channel", "lc1", t0.AddMinutes(2).AddSeconds(3)));
            Assert.That(tile.Items.OfType<ChatFeedGapItemViewModel>().Count(), Is.EqualTo(1));

            tile.SelectedPlatformFilter = PlatformFilterOption.AllOptions.Single(o => o.Platform == StreamingPlatform.YouTube);
            Assert.That(tile.Items.OfType<ChatFeedMessageItemViewModel>().All(m => m.Platform == StreamingPlatform.YouTube), Is.True);

            tile.SelectedPlatformFilter = PlatformFilterOption.All;
            tile.UpdateScrollPosition(false);

            for (var i = 0; i < 260; i++)
            {
                var msg = new ChatMessageData
                {
                    Platform = StreamingPlatform.Twitch,
                    Channel = "tw",
                    ChannelId = "tw",
                    MessageId = $"bulk-{i}",
                    Timestamp = t0.AddMinutes(3).AddSeconds(i).UtcDateTime,
                    UserId = "bulk",
                    DisplayName = "Bulk",
                    Message = $"#{i}",
                };
                await bus.PublishAsync(new ChatMessageReceived(StreamingPlatform.Twitch, "tw", msg.MessageId, "bulk", "bulk", "Bulk", msg.Message, UserStatus.None, false, msg));
            }

            Assert.Multiple(() =>
            {
                Assert.That(tile.Items.Count, Is.LessThanOrEqualTo(MultiplatformChatTileViewModel.MaxFeedItems));
                Assert.That(tile.HasUnread, Is.True);
                Assert.That(tile.UnreadCount, Is.EqualTo(260));
            });

            await bus.PublishAsync(new ChatMessagesDeleted(new ChatHistoryScope(StreamingPlatform.Twitch, "tw", "tw"), ["bulk-259"]));
            Assert.That(tile.Items.OfType<ChatFeedMessageItemViewModel>().Any(m => m.Key.MessageId == "bulk-259"), Is.False);

            await bus.PublishAsync(new ChatHistoryCleared(StreamingPlatform.Twitch, "tw", t0.AddHours(1)));
            Assert.That(tile.Items.OfType<ChatFeedMessageItemViewModel>().Any(m => m.Platform == StreamingPlatform.Twitch), Is.False);
        }
        finally
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
    }

    [Test]
    public async Task MultiplatformChatTile_preserves_scroll_on_append_filters_overlay_in_history_and_shows_youtube_whisper_fallback()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"pb-stage8-chat2-{Guid.NewGuid():N}.json");
        var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        try
        {
            var historySettings = new StubHistorySettingsProvider(new ChatSettings { ShowHistoryInApp = true });
            using var historyManager = new ChatHistoryManager(settingsManager, bus, historySettings);
            var yt = new StubPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.ServerChatHistory, true, StreamStatus.Online, null, "UC1");
            var vk = new StubPlatform(StreamingPlatform.VkLive, "VK Видео Live", PlatformCapabilities.None, true, StreamStatus.Offline, null, "vk-channel");
            var registry = new StubPlatformRegistry(yt, vk);

            var t0 = new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
            var scope = new ChatHistoryScope(StreamingPlatform.YouTube, "UC1", "yt-channel");
            var overlayOnlyHistory = new ChatMessageData
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "yt-channel",
                ChannelId = "UC1",
                MessageId = "ov-1",
                Timestamp = t0.UtcDateTime,
                UserId = "bot",
                DisplayName = "Bot",
                Message = "Только оверлей",
                IsHistorical = true,
                CommandResponse = new CommandResponseMark("!note", CommandResponseTarget.Overlay),
            };
            var normalHistory = new ChatMessageData
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "yt-channel",
                ChannelId = "UC1",
                MessageId = "hist-1",
                Timestamp = t0.AddSeconds(10).UtcDateTime,
                UserId = "u1",
                DisplayName = "User",
                Message = "История",
                IsHistorical = true,
            };
            historyManager.ImportHistory(scope, [overlayOnlyHistory, normalHistory]);

            using var tile = new MultiplatformChatTileViewModel(historyManager, bus, historySettings, platformRegistry: registry);
            Assert.That(tile.Items.OfType<ChatFeedMessageItemViewModel>().Select(static m => m.Key.MessageId), Is.EqualTo(new[] { "hist-1" }));
            Assert.That(tile.PlatformOptions, Is.EqualTo(new PlatformFilterOption[]
            {
                PlatformFilterOption.All,
                new(StreamingPlatform.YouTube, "YouTube"),
                new(StreamingPlatform.VkLive, "VK Видео Live"),
            }));

            var actions = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
            tile.Items.CollectionChanged += (_, e) => actions.Add(e.Action);

            var ytWhisperFallback = new ChatMessageData
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "yt-channel",
                ChannelId = "UC1",
                MessageId = "yt-wh",
                Timestamp = t0.AddSeconds(20).UtcDateTime,
                UserId = "bot",
                DisplayName = "Bot",
                Message = "@User баланс: 10",
                IsBot = true,
                CommandResponse = new CommandResponseMark("!bal", CommandResponseTarget.Whisper | CommandResponseTarget.Caller),
            };
            await bus.PublishAsync(new ChatMessageReceived(StreamingPlatform.YouTube, "yt-channel", "yt-wh", "bot", "bot", "Bot", ytWhisperFallback.Message, UserStatus.None, false, ytWhisperFallback, IsBot: true, CommandResponse: ytWhisperFallback.CommandResponse));

            var earliest = new ChatMessageData
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "yt-channel",
                ChannelId = "UC1",
                MessageId = "early-0",
                Timestamp = t0.AddSeconds(-5).UtcDateTime,
                UserId = "u0",
                DisplayName = "Early",
                Message = "Раннее",
            };
            await bus.PublishAsync(new ChatMessageReceived(StreamingPlatform.YouTube, "yt-channel", "early-0", "u0", "u0", "Early", "Раннее", UserStatus.None, false, earliest));

            Assert.Multiple(() =>
            {
                Assert.That(tile.Items.OfType<ChatFeedMessageItemViewModel>().Select(static m => m.Key.MessageId), Is.EqualTo(new[] { "early-0", "hist-1", "yt-wh" }));
                Assert.That(actions, Does.Not.Contain(System.Collections.Specialized.NotifyCollectionChangedAction.Reset));
            });

            await bus.PublishAsync(new ChatHistoryCleared(StreamingPlatform.YouTube, "yt-channel", t0.AddHours(1)));
            Assert.That(tile.Items.OfType<ChatFeedMessageItemViewModel>(), Is.Empty);

            var vkMessage = new ChatMessageData
            {
                Platform = StreamingPlatform.VkLive,
                Channel = "vk-channel",
                ChannelId = "vk-channel",
                MessageId = "vk-1",
                Timestamp = t0.AddHours(2).UtcDateTime,
                UserId = "vk-user",
                DisplayName = "Пользователь",
                Message = "Привет",
            };
            await bus.PublishAsync(new ChatMessageReceived(vkMessage.Platform, vkMessage.Channel, vkMessage.MessageId, vkMessage.UserId, "vk-user", vkMessage.DisplayName, vkMessage.Message, UserStatus.None, false, vkMessage));
            await bus.PublishAsync(new ChatReadingGapDetected(StreamingPlatform.VkLive, "vk-channel", "vk-live", t0.AddHours(2)));

            Assert.Multiple(() =>
            {
                Assert.That(tile.Items.OfType<ChatFeedMessageItemViewModel>().Single().PlatformDisplayName, Is.EqualTo(vk.DisplayName));
                Assert.That(tile.Items.OfType<ChatFeedGapItemViewModel>().Single().PlatformDisplayName, Is.EqualTo(vk.DisplayName));
            });
        }
        finally
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
    }

    [Test]
    public void Chat_display_pane_uses_registry_label_in_fallback()
    {
        var vk = new StubPlatform(StreamingPlatform.VkLive, "VK Видео Live", PlatformCapabilities.None, true, StreamStatus.Offline, null, "vk-channel");
        var pane = new ChatDisplayPaneViewModel(new RecordingLauncher(), new StubPlatformRegistry(vk));
        pane.SetTarget(StreamingPlatform.VkLive, null, null, shown: true);
        pane.ShowGenericFallback();

        Assert.Multiple(() =>
        {
            Assert.That(pane.DisplayName, Is.EqualTo(vk.DisplayName));
            Assert.That(pane.FallbackHeading, Is.EqualTo($"Не удалось открыть чат {vk.DisplayName}"));
        });
    }

    [Test]
    public void Tile_platform_selection_cycles_registered_platforms_and_preserves_manual_choice()
    {
        var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.None, true, StreamStatus.Offline, null, "tw");
        var vk = new StubPlatform(StreamingPlatform.VkLive, "VK Видео Live", PlatformCapabilities.None, false, StreamStatus.Online, null, "vk");
        var selection = new TilePlatformSelection(new StubPlatformRegistry(twitch, vk));

        Assert.Multiple(() =>
        {
            Assert.That(selection.AvailablePlatforms, Is.EqualTo(new[] { StreamingPlatform.Twitch, StreamingPlatform.VkLive }));
            Assert.That(selection.SelectedPlatform, Is.EqualTo(StreamingPlatform.VkLive));
            Assert.That(selection.SelectedDisplayName, Is.EqualTo(vk.DisplayName));
        });

        Assert.That(selection.Cycle(), Is.True);
        Assert.That(selection.Refresh(), Is.False);
        Assert.That(selection.SelectedPlatform, Is.EqualTo(StreamingPlatform.Twitch));

        vk.MutableStream.Status = StreamStatus.Offline;
        selection.Refresh();
        Assert.That(selection.AvailablePlatforms, Is.EqualTo(new[] { StreamingPlatform.Twitch }));
    }

    [Test]
    public async Task StreamInfoTile_switches_platforms_and_handles_youtube_multiple_broadcasts()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var twitchInfo = new StreamInfo { Title = "Twitch стрим", GameName = "Minecraft", ViewerCount = 42, StartedAt = DateTime.UtcNow.AddMinutes(-10), UserLogin = "streamer" };
        var ytInfo = new StreamInfo { Title = "YouTube стрим", ViewerCount = 120, StartedAt = DateTime.UtcNow.AddMinutes(-5), UserLogin = "yt" };

        var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit, true, StreamStatus.Offline, null, "streamer");
        var yt = new StubPlatform(StreamingPlatform.YouTube, "YouTube", PlatformCapabilities.None, true, StreamStatus.Online, ytInfo, "UC1", "https://youtube.com/watch?v=abc");
        var registry = new StubPlatformRegistry(twitch, yt);
        var launcher = new RecordingLauncher();

        using var tile = new StreamInfoTileViewModel(
            registry,
            bus,
            NullLogger<StreamInfoTileViewModel>.Instance,
            new ManualUiDispatcher(),
            launcher);

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasMultiplePlatforms, Is.True);
            Assert.That(tile.SelectedPlatform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(tile.DetailLabel, Is.EqualTo("Трансляция"));
            Assert.That(tile.DetailValue, Is.EqualTo("Активна"));
        });

        tile.OpenChannelCommand.Execute(null);
        Assert.That(launcher.LastOpened, Is.EqualTo("https://youtube.com/watch?v=abc"));

        await bus.PublishAsync(new YouTubeMultipleBroadcastsDetected("UC1",
        [
            new YouTubeBroadcastSummary("v1", "UC1", "CH1", "chat1", "T1", DateTimeOffset.UtcNow),
            new YouTubeBroadcastSummary("v2", "UC1", "CH1", "chat2", "T2", DateTimeOffset.UtcNow),
        ]));
        Assert.That(tile.HasMultipleBroadcastsNotice, Is.True);

        yt.MutableStream.Status = StreamStatus.Unknown;
        await bus.PublishAsync(new YouTubeActiveBroadcastChanged(null));
        await bus.PublishAsync(new YouTubeMultipleBroadcastsDetected("UC1",
        [
            new YouTubeBroadcastSummary("v1", "UC1", "CH1", "chat1", "T1", DateTimeOffset.UtcNow),
            new YouTubeBroadcastSummary("v2", "UC1", "CH1", "chat2", "T2", DateTimeOffset.UtcNow),
        ]));
        Assert.That(tile.HasMultipleBroadcastsNotice, Is.True);

        await bus.PublishAsync(new YouTubeActiveBroadcastChanged(new YouTubeBroadcastSummary("v1", "UC1", "CH1", "chat1", "T1", DateTimeOffset.UtcNow)));
        Assert.That(tile.HasMultipleBroadcastsNotice, Is.False);

        twitch.MutableStream.Status = StreamStatus.Online;
        twitch.MutableStream.Info = twitchInfo;
        tile.HeaderActions.First(a => a.ToolTip == "Сменить площадку в информации о стриме").Command.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(tile.SelectedPlatform, Is.EqualTo(StreamingPlatform.Twitch));
            Assert.That(tile.DetailLabel, Is.EqualTo("Игра"));
            Assert.That(tile.DetailValue, Is.EqualTo("Minecraft"));
        });
    }

    [TestCase(StreamingPlatform.YouTube, "YouTube", "UC_yt")]
    [TestCase(StreamingPlatform.VkLive, "VK Live", "vk_channel")]
    public async Task BroadcastStatusTile_uses_broadcast_state_targets_and_configured_fallback_channel(
        StreamingPlatform platform,
        string displayName,
        string channel)
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settingsPath = Path.Combine(Path.GetTempPath(), $"pb-stage8-broadcast-{Guid.NewGuid():N}.json");
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, settingsPath);
        try
        {
            var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", PlatformCapabilities.ChannelMetadataEdit, false, StreamStatus.Offline, null, null);
            var second = new StubPlatform(platform, displayName, PlatformCapabilities.None, true, StreamStatus.Online, null, channel);
            var registry = new StubPlatformRegistry(twitch, second);
            var targets = new StubTargetsProvider(new HashSet<StreamingPlatform> { StreamingPlatform.Twitch, platform });
            var state = new StubBroadcastState { IsAnyLive = false };
            await using var scheduler = new BroadcastScheduler(
                new InertBroadcastMessenger(),
                targets,
                settings,
                bus,
                TimeProvider.System,
                NullLogger<BroadcastScheduler>.Instance);

            using var tile = new BroadcastStatusTileViewModel(scheduler, settings, state, registry, targets, bus, new InertDialogService());

            Assert.Multiple(() =>
            {
                Assert.That(tile.PlatformsText, Is.EqualTo($"Twitch, {displayName}"));
                Assert.That(tile.HeaderActions[0].IsEnabled, Is.True);
            });

            tile.HeaderActions[0].Command.Execute(null);
            Assert.That(scheduler.IsActive, Is.True);
            scheduler.Stop();

            settings.Mutate(s => s.Twitch.AutoBroadcast.AutoBroadcastEnabled = true);
            state.IsAnyLive = false;
            await bus.PublishAsync(new StreamWentOffline(platform, channel));

            Assert.That(tile.StatusSeverity, Is.EqualTo(StatusSeverity.Warning));
            scheduler.Start(channel);
            tile.HeaderActions[1].Command.Execute(null);
            Assert.That(scheduler.IsActive, Is.False);
        }
        finally
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
    }

    [Test]
    public void ShellViewModel_formats_connection_error_without_raw_html_payloads()
    {
        var htmlEx = new HttpRequestException("<html><body>502 Bad Gateway</body></html>", null, System.Net.HttpStatusCode.BadGateway);
        var formatted = typeof(ShellViewModel).GetMethod("FormatConnectionError", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, [htmlEx]) as string;

        Assert.That(formatted, Is.EqualTo("сетевая ошибка HTTP 502"));
    }

    private sealed class StubHistorySettingsProvider(ChatSettings current) : IChatHistorySettingsProvider
    {
        public event Action<ChatSettings>? Changed
        {
            add { }
            remove { }
        }

        public ChatSettings GetCurrent() => current;
    }

    private sealed class RecordingLauncher : IShellLauncher
    {
        public string? LastOpened { get; private set; }

        public bool Open(string pathOrUrl)
        {
            LastOpened = pathOrUrl;
            return true;
        }

        public bool Reveal(string path) => true;

        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class InertBroadcastMessenger : IBroadcastMessenger
    {
        private static readonly BroadcastSendResult EmptyResult = new(new Dictionary<StreamingPlatform, ChatEnqueueResult>().ToFrozenDictionary());

        public ChatEnqueueResult Send(StreamingPlatform platform, string text, ChatSendCategory category = ChatSendCategory.None)
            => ChatEnqueueResult.Queued;

        public BroadcastSendResult Send(string text, IReadOnlySet<StreamingPlatform> platforms, ChatSendCategory category = ChatSendCategory.None)
            => EmptyResult;

        public BroadcastSendResult SendToConnected(string text, ChatSendCategory category = ChatSendCategory.None)
            => EmptyResult;

        public BroadcastSendResult Send(Func<IChatPlatform, string?> messageFactory, IReadOnlySet<StreamingPlatform>? platforms = null, ChatSendCategory category = ChatSendCategory.None)
            => EmptyResult;
    }

    private sealed class StubTargetsProvider(IReadOnlySet<StreamingPlatform> targets) : IBroadcastPlatformTargetsProvider
    {
        public IReadOnlySet<StreamingPlatform> GetTargetPlatforms() => targets;
    }

    private sealed class StubBroadcastState : IBroadcastState
    {
        public bool IsAnyLive { get; set; }

        public Guid? CurrentBroadcastId => null;

        public IReadOnlySet<StreamingPlatform> LivePlatforms => new HashSet<StreamingPlatform>();

        public bool IsPlatformLive(StreamingPlatform platform) => IsAnyLive && platform == StreamingPlatform.YouTube;
    }

    private sealed class InertDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(true);

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(true);

        public bool Confirm(string title, string message, bool defaultYes = false) => true;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;

        public void Info(string title, string message) { }

        public void Warning(string title, string message) { }

        public void Error(string title, string message) { }
    }

    private sealed class StubPlatformRegistry(params IChatPlatform[] platforms) : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All => platforms;

        public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            chatPlatform = platforms.FirstOrDefault(p => p.Id == platform);
            return chatPlatform is not null;
        }

        public IChatPlatform? Find(StreamingPlatform platform) => platforms.FirstOrDefault(p => p.Id == platform);

        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform) ?? throw new KeyNotFoundException();

        public IReadOnlyList<IChatPlatform> GetEnabled() => platforms.Where(p => p.IsEnabled).ToArray();

        public IReadOnlyList<IChatPlatform> GetConnected() => platforms.Where(p => p.IsConnected).ToArray();
    }

    private sealed class StubPlatform : IChatPlatform
    {
        private readonly string? _url;

        public StubPlatform(
            StreamingPlatform id,
            string displayName,
            PlatformCapabilities capabilities,
            bool isEnabled,
            StreamStatus status,
            StreamInfo? info,
            string? configuredChannel,
            string? url = null)
        {
            Id = id;
            DisplayName = displayName;
            Capabilities = capabilities;
            IsEnabledValue = isEnabled;
            ConfiguredChannel = configuredChannel;
            _url = url;
            MutableStream = new MutableStreamStatus(status, info);
        }

        public StreamingPlatform Id { get; }

        public string DisplayName { get; }

        public PlatformCapabilities Capabilities { get; }

        public bool IsEnabledValue { get; set; }

        public bool IsEnabled => IsEnabledValue;

        public bool IsConnected => false;

        public bool UsesSingleMonitoringRole { get; init; }

        public MutableStreamStatus MutableStream { get; }

        public IStreamStatus StreamStatus => MutableStream;

        public IPlatformMessenger Messenger => null!;

        public string? ConfiguredChannel { get; }

        public string? BuildStreamUrl(StreamInfo? stream = null) => _url ?? (stream is not null ? $"https://twitch.tv/{stream.UserLogin}" : null);
    }

    private sealed class MutableStreamStatus(StreamStatus status, StreamInfo? info) : IStreamStatus
    {
        public StreamStatus Status { get; set; } = status;

        public StreamInfo? Info { get; set; } = info;

        public StreamStatus CurrentStatus => Status;

        public StreamInfo? CurrentStream => Info;

        public StreamStatusSnapshot GetSnapshot() => new(Status, Info, null);

        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }
}
