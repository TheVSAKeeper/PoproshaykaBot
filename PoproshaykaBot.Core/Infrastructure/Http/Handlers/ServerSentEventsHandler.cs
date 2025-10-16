using PoproshaykaBot.Core.Infrastructure.Http.Services;
using System.Net;

namespace PoproshaykaBot.Core.Infrastructure.Http.Handlers;

/// <summary>
/// Обработчик Server-Sent Events подключений.
/// </summary>
public class ServerSentEventsHandler : IHttpRequestHandler
{
    private readonly ServerSentEventsService _sseService;

    public ServerSentEventsHandler(ServerSentEventsService sseService)
    {
        _sseService = sseService ?? throw new ArgumentNullException(nameof(sseService));
    }

    public event Action<string>? LogMessage;

    public bool CanHandle(HttpListenerRequest request)
    {
        return request.Url?.AbsolutePath == "/events";
    }

    public Task HandleAsync(HttpListenerContext context)
    {
        var response = context.Response;

        try
        {
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");
            response.Headers.Add("Access-Control-Allow-Origin", "*");

            _sseService.AddClient(response);

            LogMessage?.Invoke("Новое SSE подключение установлено");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка SSE подключения: {ex.Message}");
            try
            {
                response.Close();
            }
            catch
            {
            }
        }

        return Task.CompletedTask;
    }
}
