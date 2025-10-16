using System.Net;

namespace PoproshaykaBot.Core.Infrastructure.Http.Handlers;

/// <summary>
/// Интерфейс обработчика HTTP запросов.
/// </summary>
public interface IHttpRequestHandler
{
    /// <summary>
    /// Событие для логирования сообщений.
    /// </summary>
    event Action<string>? LogMessage;

    /// <summary>
    /// Проверяет, может ли обработчик обработать данный запрос.
    /// </summary>
    bool CanHandle(HttpListenerRequest request);

    /// <summary>
    /// Обрабатывает HTTP запрос.
    /// </summary>
    Task HandleAsync(HttpListenerContext context);
}
