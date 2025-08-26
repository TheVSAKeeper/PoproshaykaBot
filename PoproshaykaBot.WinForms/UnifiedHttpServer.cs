using PoproshaykaBot.WinForms.Models;
using PoproshaykaBot.WinForms.Settings;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.WinForms;

// TODO: Смешение ответственностей
public class UnifiedHttpServer : IChatDisplay, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private static readonly string ObsOverlayHtmlContent = LoadResourceText($"{typeof(UnifiedHttpServer).Namespace}.Assets.ObsOverlay.html");
    private static readonly string ObsOverlayCssContent = LoadResourceText($"{typeof(UnifiedHttpServer).Namespace}.Assets.obs.css");
    private static readonly byte[] ObsOverlayJsContent = LoadResourceBytes($"{typeof(UnifiedHttpServer).Namespace}.Assets.obs.js");
    private static readonly byte[] FaviconIcoContent = LoadResourceBytes($"{typeof(UnifiedHttpServer).Namespace}.icon.ico");

    private readonly ChatHistoryManager _chatHistoryManager;
    private readonly List<HttpListenerResponse> _sseClients = [];
    private readonly SettingsManager _settingsManager;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly int _port;
    private HttpListener _httpListener;
    private TaskCompletionSource<string>? _oauthCodeTask;
    private Task? _serverTask;

    public UnifiedHttpServer(ChatHistoryManager chatHistoryManager, SettingsManager settingsManager, int port = 8080)
    {
        _chatHistoryManager = chatHistoryManager;
        _settingsManager = settingsManager;
        _port = port;
        _cancellationTokenSource = new();

        _httpListener = CreateListener(port);
    }

    public event Action<string>? LogMessage;

    public bool IsRunning { get; private set; }

    public Task StartAsync()
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (_httpListener.IsListening == false)
            {
                _httpListener.Close();
                _httpListener = CreateListener(_port);
            }

            _httpListener.Start();
            IsRunning = true;

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
        if (IsRunning == false)
        {
            return;
        }

        try
        {
            IsRunning = false;
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
                message = ToServerMessage(chatMessage),
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

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    public void NotifyChatSettingsChanged(ObsChatSettings settings)
    {
        try
        {
            var cssSettings = ObsChatCssSettings.FromObsChatSettings(settings);

            var sseMessage = new
            {
                type = "chat_settings_changed",
                settings = cssSettings,
            };

            var json = JsonSerializer.Serialize(sseMessage, JsonSerializerOptions);

            lock (_sseClients)
            {
                if (_sseClients.Count == 0)
                {
                    LogMessage?.Invoke("Нет подключенных SSE клиентов для отправки уведомления о настройках");
                    return;
                }
            }

            SendSseToAllClients(json);
            LogMessage?.Invoke($"Отправлено уведомление об изменении настроек чата {_sseClients.Count} клиентам");
        }
        catch (JsonException ex)
        {
            LogMessage?.Invoke($"Ошибка сериализации настроек для SSE: {ex.Message}");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка отправки уведомления о настройках чата: {ex.Message}");
        }
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        await StopAsync();
        _httpListener?.Close();
        _cancellationTokenSource?.Dispose();
    }

    private static object ToServerMessage(ChatMessageData chatMessage)
    {
        return new
        {
            username = chatMessage.DisplayName,
            message = chatMessage.Message,
            timestamp = chatMessage.Timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            messageType = chatMessage.MessageType.ToString(),
            isFirstTime = chatMessage.IsFirstTime,
            status = chatMessage.Status,
            emotes = chatMessage.Emotes.Select(e => new
                {
                    id = e.Id,
                    name = e.Name,
                    imageUrl = e.ImageUrl,
                    startIndex = e.StartIndex,
                    endIndex = e.EndIndex,
                })
                .ToArray(),
            badges = chatMessage.Badges.Select(b => new
                {
                    type = b.Key,
                    version = b.Value,
                    imageUrl = chatMessage.BadgeUrls.GetValueOrDefault($"{b.Key}/{b.Value}", ""),
                })
                .ToArray(),
        };
    }

    private static string LoadResourceText(string resourceName)
    {
        var assembly = typeof(UnifiedHttpServer).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            throw new InvalidOperationException($"Ресурс не найден: {resourceName}");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static byte[] LoadResourceBytes(string resourceName)
    {
        var assembly = typeof(UnifiedHttpServer).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream == null)
        {
            throw new InvalidOperationException($"Ресурс не найден: {resourceName}");
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private HttpListener CreateListener(int port)
    {
        var httpListener = new HttpListener();
        httpListener.Prefixes.Add($"http://localhost:{port}/");
        return httpListener;
    }

    private async Task HandleRequestsAsync()
    {
        while (IsRunning && _cancellationTokenSource.Token.IsCancellationRequested == false)
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

                case "/assets/obs.css":
                    ServeText(response, "text/css; charset=utf-8", ObsOverlayCssContent);
                    break;

                case "/assets/obs.js":
                    ServeBytes(response, "application/javascript; charset=utf-8", ObsOverlayJsContent);
                    break;

                case "/favicon.ico":
                    ServeBytes(response, "image/x-icon", FaviconIcoContent);
                    break;

                case "/events":
                    HandleSseConnection(response);
                    break;

                case "/api/history":
                    ServeHistory(response);
                    break;

                case "/api/chat-settings":
                    ServeChatSettings(response);
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
            var buffer = Encoding.UTF8.GetBytes(ObsOverlayHtmlContent);

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

    // TODO: Удалить избыточные сообщения при обновлении настроек
    // TODO: Поправить отображение бейджей и эмодзи в историчных сообщениях
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
                .Select(ToServerMessage);

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

    private void ServeChatSettings(HttpListenerResponse response)
    {
        try
        {
            var settings = _settingsManager.Current.Twitch.ObsChat;
            var cssSettings = ObsChatCssSettings.FromObsChatSettings(settings);

            var json = JsonSerializer.Serialize(cssSettings, JsonSerializerOptions);
            var buffer = Encoding.UTF8.GetBytes(json);

            response.ContentType = "application/json; charset=utf-8";
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Cache-Control", "no-cache");
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);

            LogMessage?.Invoke("Настройки чата успешно отданы клиенту");
        }
        catch (JsonException ex)
        {
            LogMessage?.Invoke($"Ошибка сериализации настроек чата: {ex.Message}");
            response.StatusCode = 500;
            WriteErrorResponse(response, "Ошибка обработки настроек");
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка отдачи настроек чата: {ex.Message}");
            response.StatusCode = 500;
            WriteErrorResponse(response, "Внутренняя ошибка сервера");
        }
        finally
        {
            try
            {
                response.Close();
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Ошибка закрытия ответа: {ex.Message}");
            }
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

    private void WriteErrorResponse(HttpListenerResponse response, string errorMessage)
    {
        try
        {
            var errorObj = new { error = errorMessage };
            var json = JsonSerializer.Serialize(errorObj);
            var buffer = Encoding.UTF8.GetBytes(json);

            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка записи ошибки в ответ: {ex.Message}");
        }
    }

    private void ServeText(HttpListenerResponse response, string contentType, string text)
    {
        var buffer = Encoding.UTF8.GetBytes(text);
        response.ContentType = contentType;
        response.ContentLength64 = buffer.Length;
        response.OutputStream.Write(buffer, 0, buffer.Length);
        response.Close();
    }

    private void ServeBytes(HttpListenerResponse response, string contentType, byte[] bytes)
    {
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }
}
