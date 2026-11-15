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
            Assert.That(cursor.HasTransitionedToLive, Is.False);
            Assert.That(cursor.PageToken, Is.EqualTo("cursor-1"));
        });

        var secondHistory = cursor.ProcessBatch(new(
            [
                Item("old-2", BaseTime.AddSeconds(-1)),
                Item("history-second", BaseTime.AddMinutes(-9)),
                Item("old-3", BaseTime.AddSeconds(-3)),
            ],
            "cursor-2",
            TimeSpan.FromSeconds(5),
            null));

        Assert.Multiple(() =>
        {
            Assert.That(secondHistory.LiveItems, Is.Empty);
            Assert.That(secondHistory.HistoricalItems.Select(static i => i.Id), Is.EqualTo(new[] { "history-second", "old-3" }));
            Assert.That(cursor.HasTransitionedToLive, Is.False);
            Assert.That(cursor.PageToken, Is.EqualTo("cursor-2"));
        });

        var firstLive = cursor.ProcessBatch(new(
            [
                Item("fresh-1", BaseTime.AddSeconds(1)),
            ],
            "cursor-3",
            TimeSpan.FromSeconds(5),
            null));

        Assert.Multiple(() =>
        {
            Assert.That(firstLive.LiveItems.Select(static i => i.Id), Is.EqualTo(new[] { "fresh-1" }));
            Assert.That(firstLive.HistoricalItems, Is.Empty);
            Assert.That(cursor.HasTransitionedToLive, Is.True);
            Assert.That(cursor.PageToken, Is.EqualTo("cursor-3"));
        });

        var skewedAfterLive = cursor.ProcessBatch(new(
            [
                Item("skewed-live", BaseTime.AddSeconds(-3)),
            ],
            "cursor-4",
            TimeSpan.FromSeconds(5),
            null));

        Assert.Multiple(() =>
        {
            Assert.That(skewedAfterLive.LiveItems.Select(static i => i.Id), Is.EqualTo(new[] { "skewed-live" }));
            Assert.That(skewedAfterLive.HistoricalItems, Is.Empty);
            Assert.That(cursor.PageToken, Is.EqualTo("cursor-4"));
        });
    }

    [Test]
    public void MultiBatch_History_After_Empty_Or_Reset_Remains_Filtered_Until_Live_Message()
    {
        var cursor = new YouTubeChatSessionCursor("chat-1", BaseTime);

        var emptyInitial = cursor.ProcessBatch(new([], "cursor-0", TimeSpan.FromSeconds(5), null));
        var oldAfterEmpty = cursor.ProcessBatch(new([Item("old-1", BaseTime.AddMinutes(-5))], "cursor-1", TimeSpan.FromSeconds(5), null));

        Assert.Multiple(() =>
        {
            Assert.That(emptyInitial.LiveItems, Is.Empty);
            Assert.That(oldAfterEmpty.LiveItems, Is.Empty);
            Assert.That(oldAfterEmpty.HistoricalItems.Select(static i => i.Id), Is.EqualTo(new[] { "old-1" }));
            Assert.That(cursor.HasTransitionedToLive, Is.False);
        });

        cursor.ResetAfterInvalidToken(BaseTime.AddSeconds(10));

        var resetHistory1 = cursor.ProcessBatch(new([Item("gap-old-1", BaseTime.AddSeconds(2))], "r-1", TimeSpan.FromSeconds(5), null));
        var resetHistory2 = cursor.ProcessBatch(new([Item("gap-old-2", BaseTime.AddSeconds(8))], "r-2", TimeSpan.FromSeconds(5), null));
        var resetLive = cursor.ProcessBatch(new([Item("post-reset", BaseTime.AddSeconds(12))], "r-3", TimeSpan.FromSeconds(5), null));

        Assert.Multiple(() =>
        {
            Assert.That(resetHistory1.LiveItems, Is.Empty);
            Assert.That(resetHistory2.LiveItems, Is.Empty);
            Assert.That(resetHistory2.HistoricalItems.Select(static i => i.Id), Is.EqualTo(new[] { "gap-old-2" }));
            Assert.That(resetLive.LiveItems.Select(static i => i.Id), Is.EqualTo(new[] { "post-reset" }));
            Assert.That(cursor.HasTransitionedToLive, Is.True);
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
