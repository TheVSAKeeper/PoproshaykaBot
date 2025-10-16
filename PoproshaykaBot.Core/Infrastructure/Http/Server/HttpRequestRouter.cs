using PoproshaykaBot.Core.Infrastructure.Http.Handlers;
using System.Net;

namespace PoproshaykaBot.Core.Infrastructure.Http.Server;

/// <summary>
/// Маршрутизатор HTTP запросов к соответствующим обработчикам.
/// </summary>
public class HttpRequestRouter
{
    private readonly List<IHttpRequestHandler> _handlers = [];

    public event Action<string>? LogMessage;

    /// <summary>
    /// Регистрирует обработчик запросов.
    /// </summary>
    public void RegisterHandler(IHttpRequestHandler handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        _handlers.Add(handler);
        handler.LogMessage += msg => LogMessage?.Invoke(msg);
    }

    /// <summary>
    /// Обрабатывает HTTP запрос, находя подходящий обработчик.
    /// </summary>
    public async Task RouteRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            LogMessage?.Invoke($"HTTP запрос: {request.HttpMethod} {request.Url?.PathAndQuery}");

            var handler = _handlers.FirstOrDefault(h => h.CanHandle(request));

            if (handler != null)
            {
                await handler.HandleAsync(context);
            }
            else
            {
                response.StatusCode = 404;
                response.Close();
                LogMessage?.Invoke($"404: Обработчик не найден для {request.Url?.AbsolutePath}");
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка обработки запроса {request.Url?.PathAndQuery}: {ex.Message}");

            try
            {
                if (!response.OutputStream.CanWrite)
                {
                    return;
                }

                response.StatusCode = 500;
                response.Close();
            }
            catch
            {
            }
        }
    }
}
