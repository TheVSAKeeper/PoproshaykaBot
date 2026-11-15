using Grpc.Core;
using PoproshaykaBot.Core.YouTube.Client;
using System.Net;

namespace PoproshaykaBot.Core.Tests.YouTube.Client;

[TestFixture]
public class YouTubeErrorClassifierTests
{
    [TestCase("quotaExceeded", nameof(YouTubeApiErrorKind.QuotaExceeded))]
    [TestCase("dailyLimitExceeded", nameof(YouTubeApiErrorKind.QuotaExceeded))]
    [TestCase("rateLimitExceeded", nameof(YouTubeApiErrorKind.RateLimitExceeded))]
    [TestCase("userRateLimitExceeded", nameof(YouTubeApiErrorKind.RateLimitExceeded))]
    [TestCase("pageTokenInvalid", nameof(YouTubeApiErrorKind.InvalidPageToken))]
    [TestCase("liveChatEnded", nameof(YouTubeApiErrorKind.LiveChatEnded))]
    [TestCase("liveChatDisabled", nameof(YouTubeApiErrorKind.LiveChatEnded))]
    public void Classifies_Http_Json_Error_Reasons(string reason, string expectedKindName)
    {
        var expectedKind = Enum.Parse<YouTubeApiErrorKind>(expectedKindName);
        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            error = new
            {
                code = 403,
                errors = new[] { new { reason } },
            },
        });
        var exception = YouTubeErrorClassifier.FromHttpResponse(HttpStatusCode.Forbidden, body, "test");

        Assert.Multiple(() =>
        {
            Assert.That(exception.Kind, Is.EqualTo(expectedKind));
            Assert.That(exception.Reason, Is.EqualTo(reason));
        });
    }

    [Test]
    public void Classifies_Grpc_ResourceExhausted_Quota_Vs_RateLimit()
    {
        var quotaRpc = new RpcException(new Status(StatusCode.ResourceExhausted, "Quota exceeded (quotaExceeded)"));
        var dailyLimitRpc = new RpcException(new Status(StatusCode.ResourceExhausted, "dailyLimitExceeded"));
        var rateRpc = new RpcException(new Status(StatusCode.ResourceExhausted, "rateLimitExceeded"));
        var documentedThrottleRpc = new RpcException(new Status(StatusCode.ResourceExhausted, "Resource has been exhausted (e.g. check quota)"));
        var unimplRpc = new RpcException(new Status(StatusCode.Unimplemented, "StreamList disabled"));
        var unauthRpc = new RpcException(new Status(StatusCode.Unauthenticated, "Bad token"));

        Assert.Multiple(() =>
        {
            Assert.That(YouTubeErrorClassifier.FromRpcException(quotaRpc, "StreamList").Kind, Is.EqualTo(YouTubeApiErrorKind.QuotaExceeded));
            Assert.That(YouTubeErrorClassifier.FromRpcException(dailyLimitRpc, "StreamList").Kind, Is.EqualTo(YouTubeApiErrorKind.QuotaExceeded));
            Assert.That(YouTubeErrorClassifier.FromRpcException(rateRpc, "StreamList").Kind, Is.EqualTo(YouTubeApiErrorKind.RateLimitExceeded));
            Assert.That(YouTubeErrorClassifier.FromRpcException(documentedThrottleRpc, "StreamList").Kind, Is.EqualTo(YouTubeApiErrorKind.RateLimitExceeded));
            Assert.That(YouTubeErrorClassifier.FromRpcException(unimplRpc, "StreamList").Kind, Is.EqualTo(YouTubeApiErrorKind.TransportUnsupported));
            Assert.That(YouTubeErrorClassifier.FromRpcException(unauthRpc, "StreamList").Kind, Is.EqualTo(YouTubeApiErrorKind.Unauthorized));
        });
    }

    [Test]
    public void Classifies_HttpProtocolException_As_TransportUnsupported()
    {
        var inner = new HttpProtocolException(0, "HTTP/2 error", null);
        var wrapped = new HttpRequestException("Proxy downgrade", inner);

        Assert.That(
            YouTubeErrorClassifier.FromTransportException(wrapped, "StreamList").Kind,
            Is.EqualTo(YouTubeApiErrorKind.TransportUnsupported));
    }
}
