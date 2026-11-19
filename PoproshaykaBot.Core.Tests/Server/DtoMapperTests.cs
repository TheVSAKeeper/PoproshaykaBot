using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Server;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Server;

[TestFixture]
public sealed class DtoMapperTests
{
    private static readonly JsonSerializerOptions CamelCaseOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Test]
    public void ToServerMessage_IncludesMessageId()
    {
        var data = new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            Timestamp = DateTime.UtcNow,
            DisplayName = "user",
            Message = "hi",
            MessageType = ChatMessageType.UserMessage,
            MessageId = "twitch-message-id-42",
        };

        var dto = DtoMapper.ToServerMessage(data, new());
        var json = JsonSerializer.Serialize(dto, CamelCaseOptions);

        using var doc = JsonDocument.Parse(json);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(doc.RootElement.TryGetProperty("messageId", out var messageId), Is.True,
                "DTO для OBS должен включать messageId для дедупликации на фронте.");

            Assert.That(messageId.GetString(), Is.EqualTo("twitch-message-id-42"));
        }
    }

    [Test]
    public void ToServerMessage_PreservesCoreFields()
    {
        var data = new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            Timestamp = new(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc),
            DisplayName = "alice",
            Message = "hello",
            MessageType = ChatMessageType.UserMessage,
            IsFirstTime = true,
        };

        var dto = DtoMapper.ToServerMessage(data, new());
        var json = JsonSerializer.Serialize(dto, CamelCaseOptions);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.GetProperty("displayName").GetString(), Is.EqualTo("alice"));
            Assert.That(root.GetProperty("message").GetString(), Is.EqualTo("hello"));
            Assert.That(root.GetProperty("isFirstTime").GetBoolean(), Is.True);
        }
    }

    [Test]
    public void ToServerMessage_IncludesUsernameColor()
    {
        var data = new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            Timestamp = DateTime.UtcNow,
            DisplayName = "alice",
            Message = "hi",
            MessageType = ChatMessageType.UserMessage,
            Color = "#FF7F50",
        };

        var dto = DtoMapper.ToServerMessage(data, new());
        var json = JsonSerializer.Serialize(dto, CamelCaseOptions);

        using var doc = JsonDocument.Parse(json);

        Assert.That(doc.RootElement.GetProperty("color").GetString(), Is.EqualTo("#FF7F50"));
    }

    [Test]
    public void ToServerMessage_IncludesRankWhenProvided_AndNullWhenOmitted()
    {
        var data = new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            Timestamp = DateTime.UtcNow,
            DisplayName = "alice",
            Message = "hi",
            MessageType = ChatMessageType.UserMessage,
        };

        var withoutRankJson = JsonSerializer.Serialize(DtoMapper.ToServerMessage(data, new()), CamelCaseOptions);
        var withRankJson = JsonSerializer.Serialize(
            DtoMapper.ToServerMessage(data, new(), new("♛", "ФЕРЗЬ", 100, 2)),
            CamelCaseOptions);

        using var withoutDoc = JsonDocument.Parse(withoutRankJson);
        using var withDoc = JsonDocument.Parse(withRankJson);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withoutDoc.RootElement.GetProperty("rank").ValueKind, Is.EqualTo(JsonValueKind.Null));
            var rankEl = withDoc.RootElement.GetProperty("rank");
            Assert.That(rankEl.GetProperty("emoji").GetString(), Is.EqualTo("♛"));
            Assert.That(rankEl.GetProperty("name").GetString(), Is.EqualTo("ФЕРЗЬ"));
            Assert.That(rankEl.GetProperty("tier").GetInt32(), Is.EqualTo(2));
        }
    }
}
