using PoproshaykaBot.WinForms.Models;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.EventSub.Websockets;
using TwitchLib.EventSub.Websockets.Core.EventArgs;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Stream;

namespace PoproshaykaBot.WinForms;

public class StreamStatusManager : IAsyncDisposable
{
    private const int MaxReconnectAttempts = 5;
    private readonly EventSubWebsocketClient _eventSubClient;
    private readonly TwitchAPI _twitchApi;
    private string? _broadcasterUserId;
    private bool _disposed;
    private bool _isInitialized;
    private int _reconnectAttempts;

    public StreamStatusManager()
    {
        _eventSubClient = new();
        _twitchApi = new();

        _eventSubClient.WebsocketConnected += OnWebsocketConnected;
        _eventSubClient.WebsocketDisconnected += OnWebsocketDisconnected;
        _eventSubClient.WebsocketReconnected += OnWebsocketReconnected;
        _eventSubClient.ErrorOccurred += OnErrorOccurred;

        _eventSubClient.StreamOnline += OnStreamOnline;
        _eventSubClient.StreamOffline += OnStreamOffline;
    }

    public event Action<string>? ErrorOccurred;

    public event Action<string>? StatusChanged;

    public event Action<StreamOnlineArgs>? StreamStarted;

    public event Action<StreamOfflineArgs>? StreamStopped;

    public StreamStatus CurrentStatus { get; private set; } = StreamStatus.Unknown;

    public bool IsConnected => _eventSubClient != null && _disposed == false;

    public async Task InitializeAsync(string broadcasterUserId, string clientId, string accessToken)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            throw new ArgumentException("Client ID не может быть пустым", nameof(clientId));
        }

        if (string.IsNullOrEmpty(accessToken))
        {
            throw new ArgumentException("Access Token не может быть пустым", nameof(accessToken));
        }

        _broadcasterUserId = broadcasterUserId;

        _twitchApi.Settings.ClientId = clientId;
        _twitchApi.Settings.AccessToken = accessToken;

        _isInitialized = true;
    }

    public async Task StartMonitoringAsync()
    {
        if (_isInitialized == false)
        {
            throw new InvalidOperationException("StreamStatusManager не инициализирован. Вызовите InitializeAsync сначала.");
        }

        if (string.IsNullOrEmpty(_broadcasterUserId))
        {
            throw new InvalidOperationException("BroadcasterUserId не установлен.");
        }

        _reconnectAttempts = 0;

        try
        {
            StatusChanged?.Invoke("Подключение к EventSub WebSocket...");

            var connected = await _eventSubClient.ConnectAsync();

            if (connected == false)
            {
                var errorMessage = "Не удалось подключиться к EventSub WebSocket";
                ErrorOccurred?.Invoke(errorMessage);
                throw new InvalidOperationException(errorMessage);
            }

            StatusChanged?.Invoke("Ожидание подтверждения подключения...");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Ошибка запуска мониторинга: {ex.Message}");
            throw;
        }
    }

    public async Task StopMonitoringAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            StatusChanged?.Invoke("Отключение от EventSub WebSocket...");
            await _eventSubClient.DisconnectAsync();
            CurrentStatus = StreamStatus.Unknown;
            StatusChanged?.Invoke("Мониторинг стрима остановлен");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Ошибка остановки мониторинга: {ex.Message}");
        }
    }

    public async Task<string?> GetBroadcasterUserIdAsync(string channelName)
    {
        if (string.IsNullOrEmpty(channelName))
        {
            ErrorOccurred?.Invoke("Имя канала не может быть пустым");
            return null;
        }

        if (string.IsNullOrEmpty(_twitchApi.Settings.ClientId) || string.IsNullOrEmpty(_twitchApi.Settings.AccessToken))
        {
            ErrorOccurred?.Invoke("TwitchAPI не настроен. Убедитесь, что ClientId и AccessToken установлены.");
            return null;
        }

        try
        {
            StatusChanged?.Invoke($"Получение ID пользователя для канала: {channelName}");

            var users = await _twitchApi.Helix.Users.GetUsersAsync(logins: [channelName]);

            if (users?.Users == null || users.Users.Length == 0)
            {
                ErrorOccurred?.Invoke($"Пользователь с именем '{channelName}' не найден");
                return null;
            }

            var userId = users.Users.First().Id;
            StatusChanged?.Invoke($"ID пользователя получен: {userId}");
            return userId;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Ошибка получения ID пользователя: {ex.Message}");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await StopMonitoringAsync();

        GC.SuppressFinalize(this);
    }

    private async Task OnWebsocketConnected(object sender, WebsocketConnectedArgs e)
    {
        _reconnectAttempts = 0;
        StatusChanged?.Invoke($"EventSub WebSocket подключен (Session: {_eventSubClient.SessionId})");

        if (e.IsRequestedReconnect == false && string.IsNullOrEmpty(_broadcasterUserId) == false)
        {
            await CreateEventSubSubscriptions();
        }
    }

    private async Task OnWebsocketDisconnected(object sender, EventArgs e)
    {
        StatusChanged?.Invoke("EventSub WebSocket отключен");
        CurrentStatus = StreamStatus.Unknown;

        if (_reconnectAttempts < MaxReconnectAttempts)
        {
            _reconnectAttempts++;
            var delay = 1000 * Math.Pow(2, _reconnectAttempts - 1);

            StatusChanged?.Invoke($"Попытка переподключения {_reconnectAttempts}/{MaxReconnectAttempts} через {delay / 1000:F0} сек...");
            await Task.Delay(TimeSpan.FromMilliseconds(delay));


                try
                {
                    var success = await _eventSubClient.ReconnectAsync();

                    if (success)
                    {
                        _reconnectAttempts = 0;
                        StatusChanged?.Invoke("Переподключение успешно");
                    }
                    else
                    {
                        ErrorOccurred?.Invoke($"Не удалось переподключиться (попытка {_reconnectAttempts}/{MaxReconnectAttempts})");
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"Ошибка переподключения (попытка {_reconnectAttempts}/{MaxReconnectAttempts}): {ex.Message}");
                }

        }
        else if (_reconnectAttempts >= MaxReconnectAttempts)
        {
            ErrorOccurred?.Invoke($"Превышено максимальное количество попыток переподключения ({MaxReconnectAttempts}). Мониторинг стрима остановлен.");
        }
    }

    private Task OnWebsocketReconnected(object sender, EventArgs e)
    {
        StatusChanged?.Invoke($"EventSub WebSocket переподключен (Session: {_eventSubClient.SessionId})");
        return Task.CompletedTask;
    }

    private Task OnErrorOccurred(object sender, ErrorOccuredArgs e)
    {
        ErrorOccurred?.Invoke($"Ошибка EventSub WebSocket: {e.Message}");
        return Task.CompletedTask;
    }

    private Task OnStreamOnline(object sender, StreamOnlineArgs e)
    {
        CurrentStatus = StreamStatus.Online;
        StatusChanged?.Invoke($"🔴 Стрим запущен: {e.Notification.Payload.Event.Type}");
        StreamStarted?.Invoke(e);
        return Task.CompletedTask;
    }

    private Task OnStreamOffline(object sender, StreamOfflineArgs e)
    {
        CurrentStatus = StreamStatus.Offline;
        StatusChanged?.Invoke("⚫ Стрим завершен");
        StreamStopped?.Invoke(e);
        return Task.CompletedTask;
    }

    private async Task CreateEventSubSubscriptions()
    {
        if (string.IsNullOrEmpty(_broadcasterUserId))
        {
            return;
        }

        StatusChanged?.Invoke("Создание подписок EventSub...");

        var subscriptionsCreated = 0;

        try
        {
            var onlineCondition = new Dictionary<string, string>
            {
                { "broadcaster_user_id", _broadcasterUserId },
            };

            StatusChanged?.Invoke("Создание подписки stream.online...");

            var response = await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync("stream.online",
                "1",
                onlineCondition,
                EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId);

            subscriptionsCreated++;
            StatusChanged?.Invoke($"Подписка на stream.online создана (ID: {response.Subscriptions?.FirstOrDefault()?.Id})");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Ошибка создания подписки stream.online: {ex.Message}");
        }

        try
        {
            var offlineCondition = new Dictionary<string, string>
            {
                { "broadcaster_user_id", _broadcasterUserId },
            };

            StatusChanged?.Invoke("Создание подписки stream.offline...");

            var response = await _twitchApi.Helix.EventSub.CreateEventSubSubscriptionAsync("stream.offline",
                "1",
                offlineCondition,
                EventSubTransportMethod.Websocket,
                _eventSubClient.SessionId);

            subscriptionsCreated++;
            StatusChanged?.Invoke($"Подписка на stream.offline создана (ID: {response.Subscriptions?.FirstOrDefault()?.Id})");
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke($"Ошибка создания подписки stream.offline: {ex.Message}");
        }

        if (subscriptionsCreated == 2)
        {
            StatusChanged?.Invoke("Все подписки EventSub созданы успешно");
        }
    }
}
