using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Settings;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Tests.Settings.Stores;

[TestFixture]
public sealed class ObsChatStoreTests
{
    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("obs-chat-store");
        _filePath = Path.Combine(_directory.FullName, "obs-chat.json");
        _eventBus = Substitute.For<IEventBus>();
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(recursive: true);
    }

    private DirectoryInfo _directory = null!;
    private string _filePath = null!;
    private IEventBus _eventBus = null!;

    [Test]
    public async Task Save_PublishesTheEventOnlyOnceTheFileIsOnDisk()
    {
        var onDiskWhenPublished = string.Empty;

        _eventBus.When(bus => bus.PublishAsync(Arg.Any<ChatSettingsChangedEvent>(), Arg.Any<CancellationToken>()))
            .Do(_ => onDiskWhenPublished = File.ReadAllText(_filePath));

        new ObsChatStore(_eventBus, NullLogger<ObsChatStore>.Instance, _filePath)
            .Save(new() { MaxMessages = 77 });

        using (Assert.EnterMultipleScope())
        {
            await _eventBus.Received(1).PublishAsync(
                Arg.Is<ChatSettingsChangedEvent>(@event => @event.Settings.MaxMessages == 77),
                Arg.Any<CancellationToken>());

            Assert.That(onDiskWhenPublished, Does.Contain("\"maxMessages\": 77"),
                "Подписчик, читающий файл по событию, обязан увидеть уже записанное значение, а не предыдущее.");
        }
    }

    [Test]
    public async Task Save_WhenTheWriteFails_PublishesNothing()
    {
        var blocked = Path.Combine(_directory.FullName, "blocked.json");
        Directory.CreateDirectory(blocked);

        var store = new ObsChatStore(_eventBus, NullLogger<ObsChatStore>.Instance, blocked);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => store.Save(new() { MaxMessages = 77 }), Throws.InstanceOf<IOException>());

            await _eventBus.DidNotReceive().PublishAsync(
                Arg.Any<ChatSettingsChangedEvent>(),
                Arg.Any<CancellationToken>());
        }
    }

    [Test]
    public void Save_WhenTheWriteFails_LeavesTheCacheAheadOfDisk()
    {
        File.WriteAllText(_filePath, "{ \"maxMessages\": 10 }");

        var store = new ObsChatStore(_eventBus, NullLogger<ObsChatStore>.Instance, _filePath);

        Directory.CreateDirectory(_filePath + ".old");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => store.Save(new() { MaxMessages = 77 }), Throws.InstanceOf<IOException>());

            Assert.That(store.Load().MaxMessages, Is.EqualTo(77),
                "Стор подменяет кэш до записи и не откатывает его при сбое, в отличие от DashboardLayoutStore.");

            Assert.That(File.ReadAllText(_filePath), Does.Contain("\"maxMessages\": 10"),
                "На диске остаётся последняя удачная версия – кэш и файл разъезжаются.");
        }
    }
}
