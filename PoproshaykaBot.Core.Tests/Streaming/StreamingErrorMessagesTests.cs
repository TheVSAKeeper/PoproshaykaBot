using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Client;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace PoproshaykaBot.Core.Tests.Streaming;

[TestFixture]
public sealed class StreamingErrorMessagesTests
{
    [Test]
    public void YouTube_api_and_oauth_exceptions_do_not_fall_back_to_twitch_messages()
    {
        var unauthorized = new YouTubeApiException(YouTubeApiErrorKind.Unauthorized, "raw", statusCode: HttpStatusCode.Unauthorized);
        var quota = new YouTubeApiException(YouTubeApiErrorKind.QuotaExceeded, "raw");
        var oauthDenied = new YouTubeOAuthException("raw", "access_denied");
        var oauthGeneric = new YouTubeOAuthException("raw description");

        Assert.Multiple(() =>
        {
            Assert.That(StreamingErrorMessages.SafeMessage(unauthorized), Does.Contain("YouTube").And.Not.Contains("Twitch"));
            Assert.That(StreamingErrorMessages.SafeMessage(quota), Does.Contain("квоты YouTube").And.Not.Contains("Twitch"));
            Assert.That(StreamingErrorMessages.SafeMessage(oauthDenied), Is.EqualTo("Доступ к YouTube не подтверждён."));
            Assert.That(StreamingErrorMessages.SafeMessage(oauthGeneric), Is.EqualTo("Ошибка авторизации YouTube."));
        });
    }

    [Test]
    public void Twitch_socket_and_json_exceptions_keep_existing_messages()
    {
        Assert.Multiple(() =>
        {
            Assert.That(StreamingErrorMessages.SafeMessage(new SocketException()), Is.EqualTo("соединение с EventSub прервано"));
            Assert.That(StreamingErrorMessages.SafeMessage(new JsonException()), Is.EqualTo("некорректный ответ Twitch"));
            Assert.That(StreamingErrorMessages.SafeMessage(new InvalidOperationException()), Is.EqualTo("Twitch отклонил запрос"));
            Assert.That(StreamingErrorMessages.SafeMessage(new HelixRequestException(HttpMethod.Get, "/users", HttpStatusCode.NotFound, "raw", null)), Is.EqualTo("Ресурс Twitch не найден."));
        });
    }
}
