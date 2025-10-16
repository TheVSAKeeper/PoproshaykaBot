using PoproshaykaBot.Core.Application.Chat;
using PoproshaykaBot.Core.Domain.Models.Chat;
using PoproshaykaBot.Core.Domain.Models.Settings;
using PoproshaykaBot.Core.Infrastructure.Http.Handlers;
using PoproshaykaBot.Core.Infrastructure.Http.Server;
using PoproshaykaBot.Core.Infrastructure.Http.Services;
using PoproshaykaBot.Core.Infrastructure.Interfaces;
using PoproshaykaBot.Core.Infrastructure.Persistence;
using System.Net;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace PoproshaykaBot.Core.Infrastructure.Http;

/// <summary>
/// Унифицированный HTTP сервер для OAuth callback, SSE чата и статических файлов.
/// Использует компонентную архитектуру с обработчиками и роутером.
/// </summary>
public class UnifiedHttpServer : IChatDisplay, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    private readonly ChatHistoryManager _chatHistoryManager;
    private readonly SettingsManager _settingsManager;
    private readonly HttpRequestRouter _router;
    private readonly ServerSentEventsService _sseService;
    private readonly OAuthCallbackHandler _oauthHandler;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly int _port;

    private HttpListener? _httpListener;
    private Task? _serverTask;
    private bool _disposed;

    public UnifiedHttpServer(
        ChatHistoryManager chatHistoryManager,
        SettingsManager settingsManager,
        HttpRequestRouter router,
        ServerSentEventsService sseService,
        OAuthCallbackHandler oauthHandler,
        int port = 8080)
    {
        _chatHistoryManager = chatHistoryManager ?? throw new ArgumentNullException(nameof(chatHistoryManager));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _sseService = sseService ?? throw new ArgumentNullException(nameof(sseService));
        _oauthHandler = oauthHandler ?? throw new ArgumentNullException(nameof(oauthHandler));
        _port = port;
        _cancellationTokenSource = new();

        _router.LogMessage += msg => LogMessage?.Invoke(msg);
        _sseService.LogMessage += msg => LogMessage?.Invoke(msg);
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
            _httpListener = new();
            _httpListener.Prefixes.Add($"http://localhost:{_port}/");
            _httpListener.Start();

            IsRunning = true;

            _chatHistoryManager.RegisterChatDisplay(this);

            _serverTask = HandleRequestsAsync();

            var keepAliveSeconds = Math.Max(5, _settingsManager.Current.Twitch.Infrastructure.SseKeepAliveSeconds);
            _sseService.StartKeepAlive(TimeSpan.FromSeconds(keepAliveSeconds));

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
        if (!IsRunning)
        {
            return;
        }

        try
        {
            IsRunning = false;

            await _cancellationTokenSource.CancelAsync();

            _chatHistoryManager.UnregisterChatDisplay(this);

            _httpListener?.Stop();
            _httpListener?.Close();

            _sseService.DisconnectAll();

            if (_serverTask != null)
            {
                await _serverTask;
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
        return _oauthHandler.WaitForOAuthCodeAsync();
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

            var json = JsonSerializer.Serialize(messageData, JsonSerializerOptions);
            _sseService.SendToAll(json);
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
            var json = JsonSerializer.Serialize(clearData, JsonSerializerOptions);
            _sseService.SendToAll(json);
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
            if (_sseService.ClientCount == 0)
            {
                LogMessage?.Invoke("Нет подключенных SSE клиентов для отправки уведомления о настройках");
                return;
            }

            var cssSettings = ObsChatCssSettings.FromObsChatSettings(settings);
            var sseMessage = new
            {
                type = "chat_settings_changed",
                settings = cssSettings,
            };

            var json = JsonSerializer.Serialize(sseMessage, JsonSerializerOptions);
            _sseService.SendToAll(json);

            LogMessage?.Invoke($"Отправлено уведомление об изменении настроек чата {_sseService.ClientCount} клиентам");
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
        if (_disposed)
        {
            return;
        }

        await StopAsync();

        _httpListener?.Close();
        _httpListener = null;

        _sseService?.Dispose();
        _cancellationTokenSource?.Dispose();

        _disposed = true;
    }

    private static object ToServerMessage(ChatMessageData chatMessage)
    {
        return new
        {
            username = chatMessage.DisplayName,
            displayName = chatMessage.DisplayName,
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

    private async Task HandleRequestsAsync()
    {
        while (IsRunning && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (_httpListener == null)
                {
                    break;
                }

                var context = await _httpListener.GetContextAsync();
                _ = Task.Run(async () => await _router.RouteRequestAsync(context), _cancellationTokenSource.Token);
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
}
