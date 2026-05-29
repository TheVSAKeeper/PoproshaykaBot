using PoproshaykaBot.Core.Statistics;

namespace PoproshaykaBot.Core.Tests.Statistics;

[TestFixture]
public sealed class StreamSessionRecordTests
{
    [Test]
    public void EnsureSegments_WithoutSegments_SynthesizesSingleSegmentFromFlatFields()
    {
        var startedAt = DateTimeOffset.UtcNow.AddHours(-3);
        var endedAt = DateTimeOffset.UtcNow;

        var record = new StreamSessionRecord
        {
            StartedAt = startedAt,
            EndedAt = endedAt,
            Title = "Заголовок",
            Game = "Игра",
            MessageCount = 42,
            PeakViewers = 9,
            AverageViewers = 4,
        };

        record.EnsureSegments();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Segments, Has.Count.EqualTo(1));
            Assert.That(record.Segments[0].StartedAt, Is.EqualTo(startedAt));
            Assert.That(record.Segments[0].EndedAt, Is.EqualTo(endedAt));
            Assert.That(record.Segments[0].Title, Is.EqualTo("Заголовок"));
            Assert.That(record.Segments[0].Game, Is.EqualTo("Игра"));
            Assert.That(record.Segments[0].MessageCount, Is.EqualTo(42));
            Assert.That(record.Segments[0].PeakViewers, Is.EqualTo(9));
            Assert.That(record.Segments[0].AverageViewers, Is.EqualTo(4));
        }
    }

    [Test]
    public void EnsureSegments_WithExistingSegments_IsNoOp()
    {
        var record = new StreamSessionRecord();
        record.Segments.Add(new() { Game = "Существующая" });

        record.EnsureSegments();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Segments, Has.Count.EqualTo(1));
            Assert.That(record.Segments[0].Game, Is.EqualTo("Существующая"));
        }
    }
}
