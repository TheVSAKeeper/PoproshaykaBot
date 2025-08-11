using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Models;
using PoproshaykaBot.WinForms.Settings;
using System.Globalization;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Stream;

namespace PoproshaykaBot.WinForms;

public class Bot : IAsyncDisposable
{
    private readonly TwitchClient _client;
    private readonly TwitchAPI _twitchApi;
    private readonly TwitchSettings _settings;
    private readonly StatisticsCollector _statisticsCollector;
    private readonly Dictionary<string, UserInfo> _seenUsers;

    private readonly ChatDecorationsProvider _chatDecorations;
    private readonly StreamStatusManager? _streamStatusManager;
    private readonly BroadcastScheduler _broadcastScheduler;
    private bool _disposed;

    private string? _channel;

    public Bot(
        string accessToken,
        TwitchSettings settings,
        StatisticsCollector statisticsCollector,
        TwitchClient client,
        TwitchAPI twitchApi,
        ChatDecorationsProvider chatDecorationsProvider,
        BroadcastScheduler broadcastScheduler,
        StreamStatusManager? streamStatusManager = null)
    {
        _settings = settings;
        _statisticsCollector = statisticsCollector;
        _seenUsers = [];

        _client = client;

        _client.OnLog += Client_OnLog;
        _client.OnMessageReceived += Client_OnMessageReceived;
        _client.OnConnected += Client_OnConnected;
        _client.OnJoinedChannel += Сlient_OnJoinedChannel;

        _twitchApi = twitchApi;
        _chatDecorations = chatDecorationsProvider;
        _broadcastScheduler = broadcastScheduler;

        if (_settings.AutoBroadcast.AutoBroadcastEnabled)
        {
            _streamStatusManager = streamStatusManager ?? throw new ArgumentNullException(nameof(streamStatusManager));
            _streamStatusManager.StreamStarted += OnStreamStarted;
            _streamStatusManager.StreamStopped += OnStreamStopped;
            _streamStatusManager.StatusChanged += OnStreamStatusChanged;
            _streamStatusManager.ErrorOccurred += OnStreamErrorOccurred;
        }
    }

    public event Action<ChatMessageData>? ChatMessageReceived;

    public event Action<string>? Connected;

    public event Action<string>? ConnectionProgress;

    public event Action<string>? LogMessage;

    public event Action? StreamStatusChanged;

    public bool IsBroadcastActive => _broadcastScheduler.IsActive;

    public StreamStatus StreamStatus => _streamStatusManager?.CurrentStatus ?? StreamStatus.Unknown;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client.IsConnected)
        {
            var message = "Бот уже подключен";
            ConnectionProgress?.Invoke(message);
            LogMessage?.Invoke(message);
            return;
        }

        var initMessage = "Инициализация подключения...";
        ConnectionProgress?.Invoke(initMessage);
        LogMessage?.Invoke(initMessage);

        try
        {
            var connectingMessage = "Подключение к серверу Twitch...";
            ConnectionProgress?.Invoke(connectingMessage);
            LogMessage?.Invoke(connectingMessage);

            _client.Connect();

            var timeout = TimeSpan.FromSeconds(30);
            var startTime = DateTime.UtcNow;

            while (_client.IsConnected == false && DateTime.UtcNow - startTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var waitingMessage = "Ожидание подтверждения подключения...";
                ConnectionProgress?.Invoke(waitingMessage);
                LogMessage?.Invoke(waitingMessage);
                await Task.Delay(500, cancellationToken);
            }

            if (_client.IsConnected)
            {
                var successMessage = "Подключение установлено успешно";
                ConnectionProgress?.Invoke(successMessage);
                LogMessage?.Invoke(successMessage);
            }
            else
            {
                throw new TimeoutException("Превышено время ожидания подключения к Twitch");
            }

            var statsMessage = "Инициализация статистики...";
            ConnectionProgress?.Invoke(statsMessage);
            LogMessage?.Invoke(statsMessage);
            await _statisticsCollector.StartAsync();
            _statisticsCollector.ResetBotStartTime();

            var emotesMessage = "Загрузка эмодзи и бэйджей...";
            ConnectionProgress?.Invoke(emotesMessage);
            LogMessage?.Invoke(emotesMessage);
            await _chatDecorations.LoadAsync();
            LogMessage?.Invoke($"Загружено {_chatDecorations.GlobalEmotesCount} глобальных эмодзи и {_chatDecorations.GlobalBadgeSetsCount} типов глобальных бэйджей");

            if (_streamStatusManager != null)
            {
                var streamMessage = "Инициализация мониторинга стрима...";
                ConnectionProgress?.Invoke(streamMessage);
                LogMessage?.Invoke(streamMessage);
                await InitializeStreamMonitoringAsync();
            }
        }
        catch (OperationCanceledException)
        {
            var cancelMessage = "Подключение отменено пользователем";
            ConnectionProgress?.Invoke(cancelMessage);
            LogMessage?.Invoke(cancelMessage);
            throw;
        }
        catch (Exception exception)
        {
            var errorMessage = $"Ошибка подключения: {exception.Message}";
            ConnectionProgress?.Invoke(errorMessage);
            LogMessage?.Invoke(errorMessage);
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_client.IsConnected)
        {
            if (string.IsNullOrWhiteSpace(_channel) == false)
            {
                var messages = new List<string>();

                if (_settings.Messages.FarewellEnabled
                    && string.IsNullOrWhiteSpace(_settings.Messages.Farewell) == false)
                {
                    var userNames = string.Join(", ", _seenUsers.Values.Select(u => u.DisplayName));
                    var farewellMessage = _settings.Messages.Farewell.Replace("{username}", userNames);
                    messages.Add(farewellMessage);
                }

                if (_settings.Messages.DisconnectionEnabled
                    && string.IsNullOrWhiteSpace(_settings.Messages.Disconnection) == false)
                {
                    messages.Add(_settings.Messages.Disconnection);
                }

                if (messages.Count > 0)
                {
                    var combinedMessage = string.Join(" ", messages);
                    _client.SendMessage(_channel, combinedMessage);
                }
            }

            _client.Disconnect();
        }

        if (_streamStatusManager != null)
        {
            await _streamStatusManager.StopMonitoringAsync();
        }

        await _statisticsCollector.StopAsync();
    }

    public void StartBroadcast()
    {
        if (string.IsNullOrWhiteSpace(_channel))
        {
            return;
        }

        _broadcastScheduler.Start(_channel);
    }

    public void StopBroadcast()
    {
        _broadcastScheduler.Stop();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    protected virtual async ValueTask DisposeAsyncCore()
    {
        if (_disposed)
        {
            return;
        }

        await DisconnectAsync();

        if (_streamStatusManager != null)
        {
            await _streamStatusManager.DisposeAsync();
        }

        await _broadcastScheduler.DisposeAsync();
        await _statisticsCollector.DisposeAsync();
        _disposed = true;
    }

    private void OnStreamStarted(StreamOnlineArgs args)
    {
        if (_settings.AutoBroadcast.AutoBroadcastEnabled && IsBroadcastActive == false)
        {
            StartBroadcast();
            LogMessage?.Invoke("🔴 Стрим запущен. Автоматически запускаю рассылку.");

            if (_settings.AutoBroadcast.StreamStatusNotificationsEnabled
                && string.IsNullOrEmpty(_settings.AutoBroadcast.StreamStartMessage) == false
                && string.IsNullOrEmpty(_channel) == false)
            {
                _client.SendMessage(_channel, _settings.AutoBroadcast.StreamStartMessage);
            }
        }

        StreamStatusChanged?.Invoke();
    }

    private void OnStreamStopped(StreamOfflineArgs args)
    {
        if (_settings.AutoBroadcast.AutoBroadcastEnabled && IsBroadcastActive)
        {
            StopBroadcast();
            LogMessage?.Invoke("⚫ Стрим завершен. Автоматически останавливаю рассылку.");

            if (_settings.AutoBroadcast.StreamStatusNotificationsEnabled
                && string.IsNullOrEmpty(_settings.AutoBroadcast.StreamStopMessage) == false
                && string.IsNullOrEmpty(_channel) == false)
            {
                _client.SendMessage(_channel, _settings.AutoBroadcast.StreamStopMessage);
            }
        }

        StreamStatusChanged?.Invoke();
    }

    private void OnStreamStatusChanged(string status)
    {
        LogMessage?.Invoke($"EventSub: {status}");
    }

    private void OnStreamErrorOccurred(string error)
    {
        LogMessage?.Invoke($"Ошибка EventSub: {error}");
    }

    private void Client_OnLog(object? sender, OnLogArgs e)
    {
        var logMessage = $"{e.DateTime}: {e.BotUsername} - {e.Data}";
        Console.WriteLine(logMessage);
        LogMessage?.Invoke(logMessage);
    }

    private void Client_OnConnected(object? sender, OnConnectedArgs e)
    {
        var connectionMessage = "Подключен!";
        Console.WriteLine(connectionMessage);
        Connected?.Invoke(connectionMessage);
        LogMessage?.Invoke(connectionMessage);
    }

    private void Сlient_OnJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        if (_settings.Messages.ConnectionEnabled
            && string.IsNullOrWhiteSpace(_settings.Messages.Connection) == false)
        {
            _client.SendMessage(e.Channel, _settings.Messages.Connection);
        }

        _channel = e.Channel;

        if (_settings.AutoBroadcast.AutoBroadcastEnabled == false)
        {
            StartBroadcast();
        }

        var connectionMessage = $"Подключен к каналу {e.Channel}";
        Console.WriteLine(connectionMessage);
        Connected?.Invoke(connectionMessage);
        LogMessage?.Invoke(connectionMessage);
    }

    private void Client_OnMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        _statisticsCollector.TrackMessage(e.ChatMessage.UserId, e.ChatMessage.Username);

        var userMessage = new ChatMessageData
        {
            Timestamp = DateTime.UtcNow,
            DisplayName = e.ChatMessage.DisplayName,
            Message = e.ChatMessage.Message,
            MessageType = ChatMessageType.UserMessage,
            Status = GetUserStatusFlags(e.ChatMessage),

            Emotes = _chatDecorations.ExtractEmotes(e.ChatMessage, _settings.ObsChat.EmoteSizePixels),
            Badges = e.ChatMessage.Badges,
            BadgeUrls = _chatDecorations.ExtractBadgeUrls(e.ChatMessage.Badges, _settings.ObsChat.BadgeSizePixels),
        };

        ChatMessageReceived?.Invoke(userMessage);

        string? botResponse = null;

        var userInfo = new UserInfo(e.ChatMessage.UserId, e.ChatMessage.DisplayName);

        if (_settings.Messages.WelcomeEnabled && _seenUsers.TryAdd(e.ChatMessage.UserId, userInfo))
        {
            var welcomeMessage = _settings.Messages.Welcome.Replace("{username}", e.ChatMessage.DisplayName);
            _client.SendReply(e.ChatMessage.Channel, e.ChatMessage.Id, welcomeMessage);

            var welcomeResponse = new ChatMessageData
            {
                Timestamp = DateTime.UtcNow,
                DisplayName = _settings.BotUsername,
                Message = welcomeMessage,
                MessageType = ChatMessageType.BotResponse,
                Status = UserStatus.None,
            };

            ChatMessageReceived?.Invoke(welcomeResponse);
        }

        switch (e.ChatMessage.Message.ToLower())
        {
            case "!привет":
                // TODO: Обработать дублирование привета
                botResponse = $"Привет, {e.ChatMessage.Username}!";
                _client.SendMessage(e.ChatMessage.Channel, botResponse);
                break;

            case "!деньги":
                botResponse = "Принимаем криптой, СБП, куаркод справа снизу, подробнее можно узнать в телеге https://t.me/bobito217";
                _client.SendMessage(e.ChatMessage.Channel, botResponse);
                break;

            case "!сколькосообщений":
                {
                    var userStats = _statisticsCollector.GetUserStatistics(e.ChatMessage.UserId);
                    var messageCount = userStats?.MessageCount ?? 0;
                    botResponse = $"У тебя {FormatNumber(messageCount)} сообщений";
                    _client.SendReply(e.ChatMessage.Channel, e.ChatMessage.Id, botResponse);
                    break;
                }

            case "!статистикабота":
                {
                    var botStats = _statisticsCollector.GetBotStatistics();
                    var uptime = FormatTimeSpan(botStats.TotalUptime);
                    var totalMessages = FormatNumber(botStats.TotalMessagesProcessed);
                    var startTime = FormatDateTime(botStats.BotStartTime);

                    botResponse = $"📊 Статистика бота: Обработано {totalMessages} сообщений | Время работы: {uptime} | Запущен: {startTime}";

                    _client.SendMessage(e.ChatMessage.Channel, botResponse);
                    break;
                }

            case "!топпользователи":
                {
                    var topUsers = _statisticsCollector.GetTopUsers(5);

                    if (topUsers.Count == 0)
                    {
                        botResponse = "Пока нет данных о пользователях";
                        _client.SendMessage(e.ChatMessage.Channel, botResponse);
                        break;
                    }

                    var response = "🏆 Топ-5 активных пользователей: ";

                    for (var i = 0; i < topUsers.Count; i++)
                    {
                        var user = topUsers[i];
                        response += $"{i + 1}. {user.Name} ({FormatNumber(user.MessageCount)})";

                        if (i < topUsers.Count - 1)
                        {
                            response += " | ";
                        }
                    }

                    botResponse = response;
                    _client.SendMessage(e.ChatMessage.Channel, botResponse);
                    break;
                }

            case "!мойпрофиль":
                {
                    var userStats = _statisticsCollector.GetUserStatistics(e.ChatMessage.UserId);

                    if (userStats == null)
                    {
                        botResponse = "У тебя пока нет статистики";
                        _client.SendReply(e.ChatMessage.Channel, e.ChatMessage.Id, botResponse);
                        break;
                    }

                    var messageCount = FormatNumber(userStats.MessageCount);
                    var firstSeen = FormatDateTime(userStats.FirstSeen);
                    var lastSeen = FormatDateTime(userStats.LastSeen);

                    botResponse = $"👤 Твой профиль: {messageCount} сообщений | Впервые: {firstSeen} | Последний раз: {lastSeen}";
                    _client.SendReply(e.ChatMessage.Channel, e.ChatMessage.Id, botResponse);
                    break;
                }

            case "!пользователи":
            case "!чат":
                if (_seenUsers.Count == 0)
                {
                    botResponse = "В чате пока нет активных пользователей";
                    _client.SendMessage(e.ChatMessage.Channel, botResponse);
                    break;
                }

                var userNames = _seenUsers.Values.Select(x => x.DisplayName).ToList();
                var userCount = userNames.Count;

                if (userCount <= 10)
                {
                    var userList = string.Join(", ", userNames);
                    botResponse = $"👥 Активные пользователи чата ({userCount}): {userList}";
                }
                else
                {
                    var firstUsers = userNames.Take(8).ToList();
                    var userList = string.Join(", ", firstUsers);
                    botResponse = $"👥 Активные пользователи чата ({userCount}): {userList} и ещё {userCount - 8}";
                }

                _client.SendMessage(e.ChatMessage.Channel, botResponse);
                break;

            case "!пока":
                botResponse = _settings.Messages.Farewell.Replace("{username}", e.ChatMessage.DisplayName);
                _client.SendMessage(e.ChatMessage.Channel, botResponse);
                _seenUsers.Remove(e.ChatMessage.UserId);
                break;
        }

        if (string.IsNullOrEmpty(botResponse) == false)
        {
            var responseMessage = new ChatMessageData
            {
                Timestamp = DateTime.UtcNow,
                DisplayName = _settings.BotUsername,
                Message = botResponse,
                MessageType = ChatMessageType.BotResponse,
                Status = UserStatus.None,
            };

            ChatMessageReceived?.Invoke(responseMessage);
        }

        LogMessage?.Invoke(e.ChatMessage.DisplayName + ": " + e.ChatMessage.Message);
    }

    private static UserStatus GetUserStatusFlags(ChatMessage chatMessage)
    {
        var status = UserStatus.None;

        if (chatMessage.IsBroadcaster)
        {
            status |= UserStatus.Broadcaster;
        }

        if (chatMessage.IsModerator)
        {
            status |= UserStatus.Moderator;
        }

        if (chatMessage.IsVip)
        {
            status |= UserStatus.Vip;
        }

        if (chatMessage.IsSubscriber)
        {
            status |= UserStatus.Subscriber;
        }

        return status;
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
        {
            return $"{(int)timeSpan.TotalDays} дн. {timeSpan.Hours} ч. {timeSpan.Minutes} мин.";
        }

        if (timeSpan.TotalHours >= 1)
        {
            return $"{timeSpan.Hours} ч. {timeSpan.Minutes} мин.";
        }

        return $"{timeSpan.Minutes} мин. {timeSpan.Seconds} сек.";
    }

    private static string FormatNumber(ulong number)
    {
        return number.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static string FormatDateTime(DateTime dateTime)
    {
        var moscowTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Russian Standard Time");
        var moscowTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime, moscowTimeZone);

        return moscowTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU")) + " по МСК";
    }

    private async Task InitializeStreamMonitoringAsync()
    {
        if (_streamStatusManager == null)
        {
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(_settings.ClientId))
            {
                LogMessage?.Invoke("Client ID не установлен. Мониторинг стрима недоступен.");
                return;
            }

            if (string.IsNullOrEmpty(_twitchApi.Settings.AccessToken))
            {
                LogMessage?.Invoke("Access Token не установлен. Мониторинг стрима недоступен.");
                return;
            }

            await _streamStatusManager.InitializeAsync("", _settings.ClientId, _twitchApi.Settings.AccessToken);

            var broadcasterUserId = await _streamStatusManager.GetBroadcasterUserIdAsync(_settings.Channel);

            if (string.IsNullOrEmpty(broadcasterUserId))
            {
                LogMessage?.Invoke($"Не удалось получить ID пользователя для канала {_settings.Channel}");
                return;
            }

            await _streamStatusManager.InitializeAsync(broadcasterUserId, _settings.ClientId, _twitchApi.Settings.AccessToken);
            await _streamStatusManager.StartMonitoringAsync();
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка инициализации мониторинга стрима: {ex.Message}");
        }
    }
}

public record UserInfo(string UserId, string DisplayName);
