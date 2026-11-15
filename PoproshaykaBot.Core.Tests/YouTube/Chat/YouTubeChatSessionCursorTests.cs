using PoproshaykaBot.Core.YouTube.Chat;
using PoproshaykaBot.Core.YouTube.Chat.Transport;

namespace PoproshaykaBot.Core.Tests.YouTube.Chat;

[TestFixture]
public class YouTubeChatSessionCursorTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 11, 15, 20, 0, 0, TimeSpan.Zero);

    [Test]
    public void BoundedSeenMessageIdSet_Evicts_Oldest_Fifo_At_Capacity()
    {
        var seen = new BoundedSeenMessageIdSet(capacity: 3);

        Assert.Multiple(() =>
        {
            Assert.That(seen.TryAdd("m1"), Is.True);
            Assert.That(seen.TryAdd("m2"), Is.True);
            Assert.That(seen.TryAdd("m2"), Is.False);
            Assert.That(seen.TryAdd("m3"), Is.True);
            Assert.That(seen.TryAdd("m4"), Is.True);
            Assert.That(seen.Count, Is.EqualTo(3));
            Assert.That(seen.TryAdd("m1"), Is.True);
            Assert.That(seen.Contains("m2"), Is.False);
        });
    }

    [Test]
    public void Initial_Batch_Splits_By_BoundaryUtc_And_Advances_Token_Even_When_All_Historical()
    {
        var cursor = new YouTubeChatSessionCursor("chat-1", BaseTime);

        var onlyOld = cursor.ProcessBatch(new(
            [
                Item("old-1", BaseTime.AddSeconds(-10)),
                Item("old-2", BaseTime.AddSeconds(-1)),
            ],
            "cursor-1",
            TimeSpan.FromSeconds(5),
            null));

        Assert.Multiple(() =>
        {
            Assert.That(onlyOld.LiveItems, Is.Empty);
            Assert.That(onlyOld.HistoricalItems.Select(static i => i.Id), Is.EqualTo(new[] { "old-1", "old-2" }));
            Assert.That(cursor.InitialBatchProcessed, Is.True);
            Assert.That(cursor.PageToken, Is.EqualTo("cursor-1"));
        });

        var second = cursor.ProcessBatch(new(
            [
                Item("old-2", BaseTime.AddSeconds(-1)),
                Item("skewed-live", BaseTime.AddSeconds(-3)),
            ],
            "cursor-2",
            TimeSpan.FromSeconds(5),
            null));

        Assert.Multiple(() =>
        {
            Assert.That(second.LiveItems.Select(static i => i.Id), Is.EqualTo(new[] { "skewed-live" }));
            Assert.That(second.HistoricalItems, Is.Empty);
            Assert.That(cursor.PageToken, Is.EqualTo("cursor-2"));
        });
    }

    [Test]
    public void Initial_Batch_Emits_At_Or_After_Boundary_And_ResetAfterInvalidToken_Preserves_SeenIds()
    {
        var cursor = new YouTubeChatSessionCursor("chat-1", BaseTime);

        var first = cursor.ProcessBatch(new(
            [
                Item("old", BaseTime.AddMilliseconds(-1)),
                Item("exact", BaseTime),
                Item("new", BaseTime.AddSeconds(2)),
            ],
            "cursor-1",
            TimeSpan.FromSeconds(5),
            null));

        Assert.Multiple(() =>
        {
            Assert.That(first.HistoricalItems.Select(static i => i.Id), Is.EqualTo(new[] { "old" }));
            Assert.That(first.LiveItems.Select(static i => i.Id), Is.EqualTo(new[] { "exact", "new" }));
        });

        cursor.ResetAfterInvalidToken(BaseTime.AddSeconds(5));

        var afterReset = cursor.ProcessBatch(new(
            [
                Item("old", BaseTime.AddMilliseconds(-1)),
                Item("new", BaseTime.AddSeconds(2)),
                Item("gap-missed-old", BaseTime.AddSeconds(4)),
                Item("fresh", BaseTime.AddSeconds(6)),
            ],
            "cursor-2",
            TimeSpan.FromSeconds(5),
            null));

        Assert.Multiple(() =>
        {
            Assert.That(afterReset.LiveItems.Select(static i => i.Id), Is.EqualTo(new[] { "fresh" }));
            Assert.That(afterReset.HistoricalItems.Select(static i => i.Id), Is.EqualTo(new[] { "gap-missed-old" }));
            Assert.That(cursor.PageToken, Is.EqualTo("cursor-2"));
        });
    }

    private static YouTubeLiveChatItem Item(string id, DateTimeOffset publishedAt)
    {
        return new(
            id,
            YouTubeLiveChatItemKind.TextMessage,
            publishedAt,
            $"msg-{id}",
            $"msg-{id}",
            new("UCauthor", "User", false, false, false, false),
            "textMessageEvent");
    }
}
