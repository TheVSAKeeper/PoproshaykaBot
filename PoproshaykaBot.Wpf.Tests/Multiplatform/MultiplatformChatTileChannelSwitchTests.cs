using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.ViewModels.Tiles.Chat;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Multiplatform;

[TestFixture]
internal sealed class MultiplatformChatTileChannelSwitchTests
{
    [Test]
    public async Task ActiveChannelChanged_ReloadsFeedForNewChannel_AndDropsPreviousChannelMessages()
    {
        var dir = Directory.CreateTempSubdirectory("pb-tile-switch");
        try
        {
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(dir.FullName, "settings.json"));
            var catalog = new ChannelCatalogStore(NullLogger<ChannelCatalogStore>.Instance, Path.Combine(dir.FullName, "channels.json"));
            catalog.Save(new()
            {
                Channels =
                [
                    new() { Platform = StreamingPlatform.Twitch, ChannelId = "100", DisplayName = "chanA", IsConfigured = true },
                    new() { Platform = StreamingPlatform.Twitch, ChannelId = "200", DisplayName = "chanB", IsConfigured = true },
                ],
                Platforms =
                [
                    new() { Platform = StreamingPlatform.Twitch, ActiveChannelId = "100" },
                ],
            });

            var historySettings = new StaticChatHistorySettingsProvider(new ChatSettings { ShowHistoryInApp = true });
            using var history = new ChatHistoryManager(settings, bus, historySettings, channelCatalogStore: catalog);
            using var tile = new MultiplatformChatTileViewModel(history, bus, historySettings);

            var t0 = new DateTime(2026, 11, 21, 16, 0, 0, DateTimeKind.Utc);
            var msgA = new ChatMessageData
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "chanA",
                ChannelId = "100",
                MessageId = "m-a1",
                Timestamp = t0,
                UserId = "u1",
                DisplayName = "UserA",
                Message = "Старый канал",
            };
            var msgB = new ChatMessageData
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "chanB",
                ChannelId = "200",
                MessageId = "m-b1",
                Timestamp = t0.AddSeconds(1),
                UserId = "u2",
                DisplayName = "UserB",
                Message = "Новый канал",
            };

            history.AddMessage(msgA);
            await bus.PublishAsync(new ChatMessageReceived(
                StreamingPlatform.Twitch,
                "chanA",
                msgA.MessageId,
                msgA.UserId,
                "usera",
                msgA.DisplayName,
                msgA.Message,
                PoproshaykaBot.Core.Users.UserStatus.None,
                false,
                msgA));
            history.AddMessage(msgB);

            Assert.That(tile.Items.OfType<ChatFeedMessageItemViewModel>().Select(static m => m.MessageText),
                Is.EqualTo(new[] { "Старый канал" }));

            await bus.PublishAsync(new ActiveChannelChanged(
                StreamingPlatform.Twitch,
                new ChannelKey(StreamingPlatform.Twitch, "100"),
                new ChannelKey(StreamingPlatform.Twitch, "200"),
                "chanB",
                ChannelSwitchMode.Manual));

            Assert.That(tile.Items.OfType<ChatFeedMessageItemViewModel>().Select(static m => m.MessageText),
                Is.EqualTo(new[] { "Новый канал" }));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private sealed class StaticChatHistorySettingsProvider(ChatSettings current) : IChatHistorySettingsProvider
    {
        public event Action<ChatSettings>? Changed
        {
            add { }
            remove { }
        }

        public ChatSettings GetCurrent() => current;
    }
}
