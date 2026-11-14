using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Core.Tests.Debugging;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class CommandUsageStreamResetTests
{
    [Test]
    public void CommandUsageData_DeserializeWithoutBroadcastId_DefaultsToEmptyString()
    {
        const string legacyJson = """
                                  {
                                    "commands": [
                                      {
                                        "canonical": "пинг",
                                        "totalCount": 4,
                                        "streamCount": 2,
                                        "lastUsedBy": "Alice"
                                      }
                                    ]
                                  }
                                  """;

        var data = JsonSerializer.Deserialize<CommandUsageData>(legacyJson, JsonStoreOptions.Default)!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(data.BroadcastId, Is.EqualTo(string.Empty));
            Assert.That(data.Commands.Single(c => c.Canonical == "пинг").TotalCount, Is.EqualTo(4));
            Assert.That(data.Commands.Single(c => c.Canonical == "пинг").StreamCount, Is.EqualTo(2));
        }
    }

    [Test]
    public async Task BroadcastStarted_ResetsStreamCountsAndStoresBroadcastId()
    {
        var repository = new CommandUsageRepository(TimeProvider.System);
        repository.Track("помощь", "Alice");

        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var targets = new FakeTargetChannelProvider("twitch-chan");
        using var handler = new CommandUsageStreamResetHandler(
            repository,
            bus,
            NullLogger<CommandUsageStreamResetHandler>.Instance,
            targets);

        var firstId = Guid.NewGuid();
        await bus.PublishAsync(new BroadcastStarted(firstId, StreamingPlatform.Twitch, "twitch-chan", null, DateTimeOffset.UtcNow));

        var snapshot = repository.CreateSnapshotAndMarkSaved();
        Assert.That(snapshot.BroadcastId, Is.EqualTo(firstId.ToString("D")));
        Assert.That(snapshot.Commands.Single(c => c.Canonical == "помощь").StreamCount, Is.EqualTo(0));
        Assert.That(snapshot.Commands.Single(c => c.Canonical == "помощь").TotalCount, Is.EqualTo(1));

        repository.Track("стрим", "Bob");
        await bus.PublishAsync(new BroadcastStarted(firstId, StreamingPlatform.Twitch, "twitch-chan", null, DateTimeOffset.UtcNow));

        var afterSameId = repository.CreateSnapshotAndMarkSaved();
        Assert.That(afterSameId.Commands.Single(c => c.Canonical == "стрим").StreamCount, Is.EqualTo(1));
    }

    [Test]
    public async Task BroadcastStarted_DebugChannelWithoutDataRecording_PreservesStreamCounts()
    {
        var repository = new CommandUsageRepository(TimeProvider.System);
        repository.Track("помощь", "Alice");

        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var targets = FakeTargetChannelProvider.Foreign("debug-chan", "own-chan");

        using var handler = new CommandUsageStreamResetHandler(
            repository,
            bus,
            NullLogger<CommandUsageStreamResetHandler>.Instance,
            targets);

        await bus.PublishAsync(new BroadcastStarted(Guid.NewGuid(), StreamingPlatform.Twitch, "debug-chan", null, DateTimeOffset.UtcNow));

        var snapshot = repository.CreateSnapshotAndMarkSaved();
        Assert.That(snapshot.Commands.Single(c => c.Canonical == "помощь").StreamCount, Is.EqualTo(1));
    }
}
