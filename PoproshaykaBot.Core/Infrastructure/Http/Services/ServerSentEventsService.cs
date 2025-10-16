using System.Net;
using System.Text;

namespace PoproshaykaBot.Core.Infrastructure.Http.Services;

/// <summary>
/// Сервис для управления Server-Sent Events (SSE) подключениями.
/// </summary>
public class ServerSentEventsService : IDisposable
{
    private readonly List<HttpListenerResponse> _clients = [];
    private readonly object _lock = new();
    private Timer? _keepAliveTimer;
    private bool _disposed;

    public event Action<string>? LogMessage;

    public int ClientCount
    {
        get
        {
            lock (_lock)
            {
                return _clients.Count;
            }
        }
    }

    /// <summary>
    /// Запускает таймер keep-alive для поддержания SSE соединений.
    /// </summary>
    public void StartKeepAlive(TimeSpan interval)
    {
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = new(_ =>
        {
            try
            {
                SendToAll(": keep-alive");
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ошибка keep-alive: {ex.Message}");
            }
        }, null, interval, interval);
    }

    /// <summary>
    /// Регистрирует новое SSE подключение.
    /// </summary>
    public void AddClient(HttpListenerResponse response)
    {
        if (response == null)
        {
            throw new ArgumentNullException(nameof(response));
        }

        lock (_lock)
        {
            _clients.Add(response);
        }

        LogMessage?.Invoke($"Новое SSE подключение. Всего клиентов: {ClientCount}");
    }

    /// <summary>
    /// Отправляет сообщение всем подключённым SSE клиентам.
    /// </summary>
    public void SendToAll(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        lock (_lock)
        {
            var disconnectedClients = new List<HttpListenerResponse>();

            foreach (var client in _clients)
            {
                try
                {
                    var isComment = data.StartsWith(':');
                    var payload = isComment ? data + "\n\n" : $"data: {data}\n\n";
                    var buffer = Encoding.UTF8.GetBytes(payload);

                    client.OutputStream.Write(buffer, 0, buffer.Length);
                    client.OutputStream.Flush();
                }
                catch
                {
                    disconnectedClients.Add(client);
                }
            }

            foreach (var client in disconnectedClients)
            {
                _clients.Remove(client);
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }

            if (disconnectedClients.Count > 0)
            {
                LogMessage?.Invoke($"Удалено {disconnectedClients.Count} отключившихся SSE клиентов. Осталось: {_clients.Count}");
            }
        }
    }

    /// <summary>
    /// Отключает всех клиентов и освобождает ресурсы.
    /// </summary>
    public void DisconnectAll()
    {
        lock (_lock)
        {
            foreach (var client in _clients)
            {
                try
                {
                    client.Close();
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Ошибка закрытия SSE клиента: {ex.Message}");
                }
            }

            _clients.Clear();
            LogMessage?.Invoke("Все SSE клиенты отключены");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;

        DisconnectAll();
        _disposed = true;
    }
}
