using Microsoft.Extensions.Logging.Abstractions;
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

        await bus.PublishAsync(new StreamWentOffline("channel"));

        Assert.That(received, Is.EqualTo(1));
    }

    [Test]
    public async Task Не_вызывает_обработчик_после_отписки()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var received = 0;

        var subscription = bus.SubscribeOnUi<StreamWentOffline>(_ => received++);
        subscription.Dispose();

        await bus.PublishAsync(new StreamWentOffline("channel"));

        Assert.That(received, Is.Zero);
    }
}
