using PoproshaykaBot.WinForms.Server;

namespace PoproshaykaBot.WinForms.Tests.Server;

[TestFixture]
public sealed class SseFormatterTests
{
    [Test]
    public void Format_NamedEvent_PrefixesEventField()
    {
        var result = SseFormatter.Format(new("message", "{\"a\":1}"));

        Assert.That(result, Is.EqualTo("event: message\ndata: {\"a\":1}\n\n"));
    }

    [Test]
    public void Format_Comment_NoEventField_KeepsLeadingColon()
    {
        var result = SseFormatter.Format(SseEnvelope.Comment("keep-alive"));

        Assert.That(result, Is.EqualTo(": keep-alive\n\n"));
    }

    [TestCase("clear")]
    [TestCase("chat_settings_changed")]
    public void Format_OtherEventTypes_AreSupported(string eventType)
    {
        var result = SseFormatter.Format(new(eventType, "{}"));

        Assert.That(result, Does.StartWith($"event: {eventType}\n"));
        Assert.That(result, Does.EndWith("\n\n"));
    }

    [Test]
    public void Format_Throws_WhenEventTypeContainsNewline()
    {
        Assert.That(() => SseFormatter.Format(new("bad\nevent", "{}")),
            Throws.ArgumentException);
    }
}
