using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Server.Images;
using PoproshaykaBot.Core.Settings.Obs;
using PoproshaykaBot.Core.Users;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Server.Images;

[TestFixture]
public sealed class MessageImagePolicyTests
{
    private const string Link = "https://i.imgur.com/abc.png";

    private static ObsChatSettings EnabledSettings()
    {
        return new()
        {
            ShowMessageImages = true,
        };
    }

    private static ChatMessageData Message(UserStatus status, string text = "смотри " + Link)
    {
        return new()
        {
            MessageId = "1",
            Timestamp = DateTime.UtcNow,
            UserId = "42",
            DisplayName = "user",
            Message = text,
            MessageType = ChatMessageType.UserMessage,
            Status = status,
        };
    }

    [TestCase(UserStatus.Broadcaster, true)]
    [TestCase(UserStatus.Moderator, true)]
    [TestCase(UserStatus.Vip, true)]
    [TestCase(UserStatus.Subscriber, false)]
    [TestCase(UserStatus.None, false)]
    public void BuildImageUrls_DefaultRoles_AllowOnlyBroadcasterModeratorAndVip(UserStatus status, bool expected)
    {
        var urls = MessageImagePolicy.BuildImageUrls(Message(status), EnabledSettings());

        Assert.That(urls.Count > 0, Is.EqualTo(expected));
    }

    [Test]
    public void BuildImageUrls_EveryoneRole_AllowsPlainViewer()
    {
        var settings = EnabledSettings();
        settings.MessageImageRoles = MessageImageSenderRoles.Everyone;

        var urls = MessageImagePolicy.BuildImageUrls(Message(UserStatus.None), settings);

        Assert.That(urls, Has.Count.EqualTo(1));
    }

    [Test]
    public void BuildImageUrls_Disabled_ReturnsNothing()
    {
        var settings = EnabledSettings();
        settings.ShowMessageImages = false;

        Assert.That(MessageImagePolicy.BuildImageUrls(Message(UserStatus.Moderator), settings), Is.Empty);
    }

    [Test]
    public void BuildImageUrls_DefaultSettings_ReturnNothing()
    {
        Assert.That(MessageImagePolicy.BuildImageUrls(Message(UserStatus.Broadcaster), new()), Is.Empty,
            "По умолчанию функция выключена – обновление не должно включать её само.");
    }

    [Test]
    public void BuildImageUrls_ReturnsLocalProxyUrlWithEncodedSource()
    {
        var urls = MessageImagePolicy.BuildImageUrls(Message(UserStatus.Moderator), EnabledSettings());

        Assert.That(urls.Single(), Is.EqualTo("/api/image?u=https%3A%2F%2Fi.imgur.com%2Fabc.png"),
            "Браузер OBS обязан ходить только на свой прокси, внешний URL в DTO попадать не должен.");
    }

    [Test]
    public void BuildImageUrls_KeepsAtMostOneImage()
    {
        var text = "https://i.imgur.com/one.png и https://i.imgur.com/two.png";

        Assert.That(MessageImagePolicy.BuildImageUrls(Message(UserStatus.Moderator, text), EnabledSettings()), Has.Count.EqualTo(1));
    }

    [TestCase(ChatMessageType.BotResponse)]
    [TestCase(ChatMessageType.SystemNotification)]
    public void BuildImageUrls_NonUserMessage_ReturnsNothing(ChatMessageType messageType)
    {
        var settings = EnabledSettings();
        settings.MessageImageRoles = MessageImageSenderRoles.Everyone;

        var message = new ChatMessageData
        {
            MessageId = "1",
            Timestamp = DateTime.UtcNow,
            Message = "смотри " + Link,
            MessageType = messageType,
        };

        Assert.That(MessageImagePolicy.BuildImageUrls(message, settings), Is.Empty);
    }

    [Test]
    public void ToServerMessage_CarriesImagesArray()
    {
        var dto = DtoMapper.ToServerMessage(Message(UserStatus.Moderator), EnabledSettings());
        var json = JsonSerializer.Serialize(dto, ServerJsonOptions.Default);

        using var document = JsonDocument.Parse(json);
        var images = document.RootElement.GetProperty("images");

        Assert.That(images.GetArrayLength(), Is.EqualTo(1));
        Assert.That(images[0].GetString(), Does.StartWith("/api/image?u="));
    }

    [Test]
    public void ToServerMessage_WhenDisabled_CarriesEmptyImagesArray()
    {
        var dto = DtoMapper.ToServerMessage(Message(UserStatus.Moderator), new());
        var json = JsonSerializer.Serialize(dto, ServerJsonOptions.Default);

        using var document = JsonDocument.Parse(json);

        Assert.That(document.RootElement.GetProperty("images").GetArrayLength(), Is.Zero);
    }
}
