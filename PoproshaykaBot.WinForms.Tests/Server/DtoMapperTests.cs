using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Server;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Tests.Server;

[TestFixture]
public sealed class DtoMapperTests
{
    [Test]
    public void ToServerMessage_IncludesMessageId()
    {
        var data = new ChatMessageData
        {
            Timestamp = DateTime.UtcNow,
            DisplayName = "user",
            Message = "hi",
            MessageType = ChatMessageType.UserMessage,
            MessageId = "twitch-message-id-42",
        };

        var dto = DtoMapper.ToServerMessage(data);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

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
            Timestamp = new(2026, 4, 30, 12, 0, 0, DateTimeKind.Utc),
            DisplayName = "alice",
            Message = "hello",
            MessageType = ChatMessageType.UserMessage,
            IsFirstTime = true,
        };

        var dto = DtoMapper.ToServerMessage(data);
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.GetProperty("displayName").GetString(), Is.EqualTo("alice"));
            Assert.That(root.GetProperty("message").GetString(), Is.EqualTo("hello"));
            Assert.That(root.GetProperty("isFirstTime").GetBoolean(), Is.True);
        }
    }
}
