using System.Net;
using System.Text;

namespace PoproshaykaBot.Core.Tests.Twitch.Helix;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();

    public List<string?> RequestBodies { get; } = new();

    public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

    public static StubHttpMessageHandler ReturnsJson(HttpStatusCode status, string body)
    {
        return new()
        {
            Responder = _ => new(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            },
        };
    }

    public static StubHttpMessageHandler ReturnsStatus(HttpStatusCode status)
    {
        return new()
        {
            Responder = _ => new(status),
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (request.Content != null)
        {
            RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
        }
        else
        {
            RequestBodies.Add(null);
        }

        var response = Responder?.Invoke(request)
                       ?? new HttpResponseMessage(HttpStatusCode.NotImplemented);

        return response;
    }
}
