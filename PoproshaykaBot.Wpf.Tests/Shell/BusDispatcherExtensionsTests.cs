using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
public class BusDispatcherExtensionsTests
{
    [Test]
    public async Task Доставляет_событие_когда_тестовый_Application_создан_на_другом_потоке()
    {
        var thread = new Thread(() => TestResources.Ensure([]));

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var received = 0;

        using var subscription = bus.SubscribeOnUi<StreamWentOffline>(_ => received++);

        await bus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "channel"));

        Assert.That(received, Is.EqualTo(1));
    }

    [Test]
    public async Task Не_вызывает_обработчик_после_отписки()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var received = 0;

        var subscription = bus.SubscribeOnUi<StreamWentOffline>(_ => received++);
        subscription.Dispose();

        await bus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "channel"));

        Assert.That(received, Is.Zero);
    }

    [Test]
    public async Task SubscribePlatformOnUi_IgnoresEventsFromOtherPlatforms()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var platforms = new List<StreamingPlatform>();

        using var subscription = bus.SubscribePlatformOnUi<StreamWentOffline>(
            StreamingPlatform.Twitch,
            e => platforms.Add(e.Platform));

        await bus.PublishAsync(new StreamWentOffline(StreamingPlatform.YouTube, "yt-channel"));
        await bus.PublishAsync(new StreamWentOffline(StreamingPlatform.Twitch, "tw-channel"));

        Assert.That(platforms, Is.EqualTo(new[] { StreamingPlatform.Twitch }));
    }
}
