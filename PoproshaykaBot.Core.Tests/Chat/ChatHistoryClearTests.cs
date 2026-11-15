using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Tests.Chat;

[TestFixture]
public sealed class ChatHistoryClearTests
{
    [Test]
    public void Clear_NotifiesEachAffectedPlatformAndChannelExactlyOnce()
    {
        var directory = Path.Combine(Path.GetTempPath(), "history-clear-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(directory, "settings.json"));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var history = new ChatHistoryManager(settings, bus);
            var cleared = new List<(StreamingPlatform, string?)>();
            using var subscription = bus.Subscribe<ChatHistoryCleared>(e => cleared.Add((e.Platform, e.Channel)));
            history.AddMessage(new() { Platform = StreamingPlatform.Twitch }, "tw");
            history.AddMessage(new() { Platform = StreamingPlatform.YouTube }, "yt-1");
            history.AddMessage(new() { Platform = StreamingPlatform.YouTube }, "yt-1");
            history.AddMessage(new() { Platform = StreamingPlatform.YouTube }, "yt-2");
            history.ClearHistory();
            history.ClearHistory();
            Assert.That(history.GetHistory(), Is.Empty);
            Assert.That(cleared, Is.EquivalentTo(new[]
            {
                (StreamingPlatform.Twitch, "tw"),
                (StreamingPlatform.YouTube, "yt-1"),
                (StreamingPlatform.YouTube, "yt-2"),
            }));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
