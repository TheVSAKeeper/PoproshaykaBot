using PoproshaykaBot.WinForms.Models;
using System.Net;
using System.Text;
using System.Text.Json;

namespace PoproshaykaBot.WinForms;

// TODO: Смешение ответственностей
public class UnifiedHttpServer : IChatDisplay, IDisposable
{
    private readonly HttpListener _httpListener;
    private readonly ChatHistoryManager _chatHistoryManager;
    private readonly List<HttpListenerResponse> _sseClients = [];
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly int _port;
    private TaskCompletionSource<string>? _oauthCodeTask;
    private bool _isRunning;
    private Task? _serverTask;

    public UnifiedHttpServer(ChatHistoryManager chatHistoryManager, int port = 8080)
    {
        _chatHistoryManager = chatHistoryManager;
        _port = port;
        _httpListener = new();
        _cancellationTokenSource = new();

        _httpListener.Prefixes.Add($"http://localhost:{port}/");
    }

    public event Action<string>? LogMessage;

    public Task StartAsync()
    {
        if (_isRunning)
        {
            return Task.CompletedTask;
        }

        try
        {
            _httpListener.Start();
            _isRunning = true;

            _chatHistoryManager.RegisterChatDisplay(this);
            _serverTask = HandleRequestsAsync();

            LogMessage?.Invoke($"HTTP сервер запущен на порту {_port}");

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка запуска HTTP сервера: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_isRunning == false)
        {
            return;
        }

        try
        {
            _isRunning = false;
            await _cancellationTokenSource.CancelAsync();

            _chatHistoryManager.UnregisterChatDisplay(this);

            _httpListener.Stop();

            if (_serverTask != null)
            {
                await _serverTask;
            }

            lock (_sseClients)
            {
                foreach (var client in _sseClients.ToList())
                {
                    try
                    {
                        client.Close();
                    }
                    catch (Exception ex)
                    {
                        LogMessage?.Invoke($"Не удалось отключить sse-клиент {client.RedirectLocation}. Ошибка: {ex.Message}");
                    }
                }

                _sseClients.Clear();
            }

            LogMessage?.Invoke("HTTP сервер остановлен");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка остановки HTTP сервера: {ex.Message}");
        }
    }

    public Task<string> WaitForOAuthCodeAsync()
    {
        _oauthCodeTask = new();
        return _oauthCodeTask.Task;
    }

    public void AddChatMessage(ChatMessageData chatMessage)
    {
        try
        {
            var messageData = new
            {
                type = "message",
                message = new
                {
                    username = chatMessage.DisplayName,
                    message = chatMessage.Message,
                    timestamp = chatMessage.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    messageType = chatMessage.MessageType.ToString(),
                },
            };

            var json = JsonSerializer.Serialize(messageData);
            SendSseToAllClients(json);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка отправки сообщения через SSE: {ex.Message}");
        }
    }

    public void ClearChat()
    {
        try
        {
            var clearData = new { type = "clear" };
            var json = JsonSerializer.Serialize(clearData);
            SendSseToAllClients(json);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка очистки чата через SSE: {ex.Message}");
        }
    }

    public void Dispose()
    {
        StopAsync().Wait();
        _httpListener?.Close();
        _cancellationTokenSource?.Dispose();
    }

    private async Task HandleRequestsAsync()
    {
        while (_isRunning && _cancellationTokenSource.Token.IsCancellationRequested == false)
        {
            try
            {
                var context = await _httpListener.GetContextAsync();
                _ = Task.Run(() => ProcessRequest(context), _cancellationTokenSource.Token);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ошибка обработки HTTP запроса: {ex.Message}");
            }
        }
    }

    private void ProcessRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            LogMessage?.Invoke($"HTTP запрос: {request.HttpMethod} {request.Url?.PathAndQuery}");

            switch (request.Url?.AbsolutePath)
            {
                case "/":
                    HandleOAuthCallback(context);
                    break;

                case "/chat":
                    ServeObsOverlay(response);
                    break;

                case "/events":
                    HandleSseConnection(response);
                    break;

                case "/api/history":
                    ServeHistory(response);
                    break;

                default:
                    response.StatusCode = 404;
                    response.Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка обработки запроса: {ex.Message}");

            context.Response.StatusCode = 500;
            context.Response.Close();
        }
    }

    private void HandleOAuthCallback(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var code = request.QueryString["code"];
            var error = request.QueryString["error"];

            if (string.IsNullOrEmpty(code) == false)
            {
                _oauthCodeTask?.SetResult(code);

                var successHtml =
                    """
                    <!DOCTYPE html>
                    <html>
                    <head>
                        <title>Авторизация успешна</title>
                        <style>
                            body { font-family: Arial, sans-serif; text-align: center; margin-top: 50px; }
                            .success { color: green; font-size: 24px; }
                        </style>
                    </head>
                    <body>
                        <div class='success'>✓ Авторизация успешна!</div>
                        <p>Вы можете закрыть это окно и вернуться к приложению.</p>
                    </body>
                    </html>
                    """;

                var buffer = Encoding.UTF8.GetBytes(successHtml);
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            else if (string.IsNullOrEmpty(error) == false)
            {
                _oauthCodeTask?.SetException(new InvalidOperationException($"OAuth ошибка: {error}"));

                var errorHtml =
                    $$"""

                      <!DOCTYPE html>
                      <html>
                      <head>
                          <title>Ошибка авторизации</title>
                          <style>
                              body { font-family: Arial, sans-serif; text-align: center; margin-top: 50px; }
                              .error { color: red; font-size: 24px; }
                          </style>
                      </head>
                      <body>
                          <div class='error'>✗ Ошибка авторизации</div>
                          <p>Ошибка: {{error}}</p>
                          <p>Попробуйте еще раз.</p>
                      </body>
                      </html>
                      """;

                var buffer = Encoding.UTF8.GetBytes(errorHtml);
                response.ContentType = "text/html; charset=utf-8";
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                response.StatusCode = 400;
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка обработки OAuth callback: {ex.Message}");
            response.StatusCode = 500;
        }
        finally
        {
            response.Close();
        }
    }

    private void ServeObsOverlay(HttpListenerResponse response)
    {
        try
        {
            var html = GetObsOverlayHtml();
            var buffer = Encoding.UTF8.GetBytes(html);

            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка отдачи OBS overlay: {ex.Message}");
            response.StatusCode = 500;
        }
        finally
        {
            response.Close();
        }
    }

    private string GetObsOverlayHtml()
    {
        return """
               <!DOCTYPE html>
               <html>
               <head>
                   <title>PoproshaykaBot Chat Overlay</title>
                   <meta charset='utf-8'>
                   <style>
                       body { 
                           background: transparent; 
                           font-family: Arial, sans-serif; 
                           margin: 0; 
                           padding: 10px; 
                           color: white;
                       }
                       .message { 
                           margin: 5px 0; 
                           animation: slideIn 0.3s ease-out; 
                           padding: 5px;
                           border-radius: 5px;
                           background: rgba(0, 0, 0, 0.7);
                       }
                       @keyframes slideIn { 
                           from { transform: translateX(-100%); opacity: 0; } 
                           to { transform: translateX(0); opacity: 1; }
                       }
                       .username { 
                           font-weight: bold; 
                           color: #9146ff;
                       }
                       .timestamp { 
                           color: #999; 
                           font-size: 0.8em; 
                           margin-right: 5px;
                       }
                       .message-text {
                           color: white;
                       }
                       .system-message {
                           color: #ffcc00;
                           font-style: italic;
                       }
                   </style>
               </head>
               <body>
                   <div id='chat'></div>
                   <script>
                       const chatContainer = document.getElementById('chat');
                       const maxMessages = 50;

                       const eventSource = new EventSource('/events');
                       
                       eventSource.onmessage = function(event) {
                           try {
                               const data = JSON.parse(event.data);
                               if (data.type === 'message') {
                                   addMessage(data.message);
                               } else if (data.type === 'clear') {
                                   clearChat();
                               }
                           } catch (e) {
                               console.error('Ошибка парсинга SSE данных:', e);
                           }
                       };

                       eventSource.onerror = function(event) {
                           console.error('SSE ошибка:', event);
                       };

                       function addMessage(message) {
                           const messageDiv = document.createElement('div');
                           messageDiv.className = 'message';
                           
                           const timestamp = new Date(message.timestamp).toLocaleTimeString();
                           const isSystemMessage = message.messageType !== 'UserMessage';
                           
                           if (isSystemMessage) {
                               messageDiv.innerHTML = `
                                   <span class='timestamp'>${timestamp}</span>
                                   <span class='system-message'>${message.message}</span>
                               `;
                           } else {
                               messageDiv.innerHTML = `
                                   <span class='timestamp'>${timestamp}</span>
                                   <span class='username'>${message.username}:</span>
                                   <span class='message-text'> ${message.message}</span>
                               `;
                           }
                           
                           chatContainer.appendChild(messageDiv);
                           
                           while (chatContainer.children.length > maxMessages) {
                               chatContainer.removeChild(chatContainer.firstChild);
                           }
                           
                           chatContainer.scrollTop = chatContainer.scrollHeight;
                       }

                       function clearChat() {
                           chatContainer.innerHTML = '';
                       }

                       fetch('/api/history')
                           .then(response => response.json())
                           .then(messages => {
                               messages.forEach(message => addMessage(message));
                           })
                           .catch(error => console.error('Ошибка загрузки истории:', error));
                   </script>
               </body>
               </html>
               """;
    }

    private void HandleSseConnection(HttpListenerResponse response)
    {
        try
        {
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");
            response.Headers.Add("Access-Control-Allow-Origin", "*");

            lock (_sseClients)
            {
                _sseClients.Add(response);
            }

            LogMessage?.Invoke("Новое SSE подключение установлено");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка SSE подключения: {ex.Message}");
        }
    }

    private void ServeHistory(HttpListenerResponse response)
    {
        try
        {
            var history = _chatHistoryManager.GetHistory()
                .TakeLast(10)
                .Select(msg => new
                {
                    username = msg.DisplayName,
                    message = msg.Message,
                    timestamp = msg.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    messageType = msg.MessageType.ToString(),
                });

            var json = JsonSerializer.Serialize(history);
            var buffer = Encoding.UTF8.GetBytes(json);

            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка отдачи истории: {ex.Message}");
            response.StatusCode = 500;
        }
        finally
        {
            response.Close();
        }
    }

    private void SendSseToAllClients(string data)
    {
        lock (_sseClients)
        {
            var disconnectedClients = new List<HttpListenerResponse>();

            foreach (var client in _sseClients)
            {
                try
                {
                    var message = $"data: {data}\n\n";
                    var buffer = Encoding.UTF8.GetBytes(message);
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
                _sseClients.Remove(client);
                client.Close();
            }

            if (disconnectedClients.Count > 0)
            {
                LogMessage?.Invoke($"Удалено {disconnectedClients.Count} отключившихся SSE клиентов");
            }
        }
    }
}
