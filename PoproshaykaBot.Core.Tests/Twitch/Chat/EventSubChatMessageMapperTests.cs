using PoproshaykaBot.Core.Twitch.Chat;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Twitch.Chat;

[TestFixture]
public sealed class EventSubChatMessageMapperTests
{
    private const string BotId = "bot-42";
    private const string ViewerId = "viewer-7";

    private static JsonElement Payload(string chatterUserId, string? color = null)
    {
        var colorField = color is null ? string.Empty : $", \"color\": \"{color}\"";

        var json = $$"""
                     {
                       "event": {
                         "broadcaster_user_login": "channel",
                         "message_id": "msg-1",
                         "chatter_user_id": "{{chatterUserId}}",
                         "chatter_user_login": "chatter",
                         "chatter_user_name": "Chatter"{{colorField}},
                         "message": { "text": "привет" },
                         "badges": []
                       }
                     }
                     """;

        return JsonDocument.Parse(json).RootElement;
    }

    [Test]
    public void Map_WhenChatterIsBot_MarksIsBotTrue()
    {
        var message = EventSubChatMessageMapper.Map(Payload(BotId), BotId);

        Assert.That(message.IsBot, Is.True);
    }

    [Test]
    public void Map_WhenChatterIsViewer_LeavesIsBotFalse()
    {
        var message = EventSubChatMessageMapper.Map(Payload(ViewerId), BotId);

        Assert.That(message.IsBot, Is.False);
    }

    [Test]
    public void Map_WhenBotIdIsNull_LeavesIsBotFalse()
    {
        var message = EventSubChatMessageMapper.Map(Payload(BotId));

        Assert.That(message.IsBot, Is.False);
    }

    [Test]
    public void Map_WhenBotIdIsEmpty_LeavesIsBotFalse()
    {
        var message = EventSubChatMessageMapper.Map(Payload(BotId), string.Empty);

        Assert.That(message.IsBot, Is.False);
    }

    [Test]
    public void Map_WhenColorIsValidHex_PreservesIt()
    {
        var message = EventSubChatMessageMapper.Map(Payload(ViewerId, "#FF0000"), BotId);

        Assert.That(message.Color, Is.EqualTo("#FF0000"));
    }

    [Test]
    public void Map_WhenColorIsMissing_ReturnsEmpty()
    {
        var message = EventSubChatMessageMapper.Map(Payload(ViewerId), BotId);

        Assert.That(message.Color, Is.Empty);
    }

    [Test]
    public void Map_WhenColorIsEmptyString_ReturnsEmpty()
    {
        var message = EventSubChatMessageMapper.Map(Payload(ViewerId, ""), BotId);

        Assert.That(message.Color, Is.Empty);
    }

    [Test]
    public void Map_WhenColorIsMalformed_ReturnsEmpty()
    {
        var message = EventSubChatMessageMapper.Map(Payload(ViewerId, "red"), BotId);

        Assert.That(message.Color, Is.Empty);
    }
}
