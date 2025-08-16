using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Models;
using PoproshaykaBot.WinForms.Settings;
using System.Text;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Stream;

namespace PoproshaykaBot.WinForms;

public class Bot : IAsyncDisposable
{
    private const int CharsLimit = 500;

    private readonly TwitchClient _client;
    private readonly TwitchAPI _twitchApi;
    private readonly TwitchSettings _settings;
    private readonly StatisticsCollector _statisticsCollector;
    private readonly AudienceTracker _audienceTracker;
    private readonly ChatDecorationsProvider _chatDecorations;
    private readonly ChatCommandProcessor _commandProcessor;
    private readonly StreamStatusManager _streamStatusManager;
    private readonly BroadcastScheduler _broadcastScheduler;

    private bool _disposed;
    private string? _channel;
    private bool _streamHandlersAttached;

    public Bot(
        TwitchSettings settings,
        StatisticsCollector statisticsCollector,
        TwitchClient client,
        TwitchAPI twitchApi,
        ChatDecorationsProvider chatDecorationsProvider,
        AudienceTracker audienceTracker,
        BroadcastScheduler broadcastScheduler,
        ChatCommandProcessor commandProcessor,
        StreamStatusManager streamStatusManager)
    {
        _settings = settings;
        _statisticsCollector = statisticsCollector;
        _audienceTracker = audienceTracker;

        _client = client;

        _client.OnLog += Client_OnLog;
        _client.OnMessageReceived += Client_OnMessageReceived;
        _client.OnConnected += Client_OnConnected;
        _client.OnJoinedChannel += Сlient_OnJoinedChannel;

        _twitchApi = twitchApi;
        _chatDecorations = chatDecorationsProvider;
        _broadcastScheduler = broadcastScheduler;
        _commandProcessor = commandProcessor;
        _streamStatusManager = streamStatusManager;
        AttachStreamStatusHandlers();
    }

    public event Action<ChatMessageData>? ChatMessageReceived;

    public event Action<string>? Connected;

    public event Action<string>? ConnectionProgress;

    public event Action<string>? LogMessage;

    public event Action? StreamStatusChanged;

    public bool IsBroadcastActive => _broadcastScheduler.IsActive;

    public StreamStatus StreamStatus => _streamStatusManager.CurrentStatus;
    public StreamInfo? CurrentStream => _streamStatusManager.CurrentStream;

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

            if (_settings.AutoBroadcast.AutoBroadcastEnabled)
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

                var collectiveFarewell = _audienceTracker.CreateCollectiveFarewell();

                if (string.IsNullOrWhiteSpace(collectiveFarewell) == false)
                {
                    messages.Add(collectiveFarewell);
                }

                if (_settings.Messages.DisconnectionEnabled
                    && string.IsNullOrWhiteSpace(_settings.Messages.Disconnection) == false)
                {
                    messages.Add(_settings.Messages.Disconnection);
                }

                if (messages.Count > 0)
                {
                    var combinedMessage = string.Join(" ", messages);
                    SendMessageSmart(_channel, combinedMessage);
                }

                _audienceTracker.ClearAll();
            }

            _client.Disconnect();
        }

        if (_settings.AutoBroadcast.AutoBroadcastEnabled)
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
        DetachStreamStatusHandlers();
        await _streamStatusManager.DisposeAsync();
        await _broadcastScheduler.DisposeAsync();
        await _statisticsCollector.StopAsync();
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
                SendMessageSmart(_channel, _settings.AutoBroadcast.StreamStartMessage);
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
                SendMessageSmart(_channel, _settings.AutoBroadcast.StreamStopMessage);
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
            SendMessageSmart(e.Channel, _settings.Messages.Connection);
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

        var isFirstSeen = _audienceTracker.OnUserMessage(e.ChatMessage.UserId, e.ChatMessage.DisplayName);

        if (_settings.Messages.WelcomeEnabled && isFirstSeen)
        {
            var welcomeMessage = _audienceTracker.CreateWelcome(e.ChatMessage.DisplayName);

            if (string.IsNullOrWhiteSpace(welcomeMessage) == false)
            {
                SendReplySmart(e.ChatMessage.Channel, e.ChatMessage.Id, welcomeMessage);

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
        }

        var context = new CommandContext
        {
            Channel = e.ChatMessage.Channel,
            MessageId = e.ChatMessage.Id,
            UserId = e.ChatMessage.UserId,
            Username = e.ChatMessage.Username,
            DisplayName = e.ChatMessage.DisplayName,
        };

        if (_commandProcessor.TryProcess(e.ChatMessage.Message, context, out var response) == false)
        {
        }

        if (response != null)
        {
            switch (response.Delivery)
            {
                case DeliveryType.Reply:
                    SendReplySmart(context.Channel, response.ReplyToMessageId ?? context.MessageId, response.Text);
                    break;

                case DeliveryType.Normal:
                default:
                    SendMessageSmart(context.Channel, response.Text);
                    break;
            }

            botResponse = response.Text;

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

    private static IEnumerable<string> SplitByLimit(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            yield return text;
            yield break;
        }

        var parts = text.Split(" | ");
        var builder = new StringBuilder();

        foreach (var part in parts)
        {
            var isFirstInChunk = builder.Length == 0;
            var candidate = isFirstInChunk ? part : " | " + part;

            if (builder.Length + candidate.Length <= maxLength)
            {
                builder.Append(candidate);
            }
            else
            {
                if (builder.Length > 0)
                {
                    yield return builder.ToString();
                    builder.Clear();
                }

                if (part.Length > maxLength)
                {
                    for (var i = 0; i < part.Length; i += maxLength)
                    {
                        var length = Math.Min(maxLength, part.Length - i);
                        yield return part.Substring(i, length);
                    }
                }
                else
                {
                    builder.Append(part);
                }
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private void AttachStreamStatusHandlers()
    {
        if (_streamHandlersAttached)
        {
            return;
        }

        _streamStatusManager.StreamStarted += OnStreamStarted;
        _streamStatusManager.StreamStopped += OnStreamStopped;
        _streamStatusManager.StatusChanged += OnStreamStatusChanged;
        _streamStatusManager.ErrorOccurred += OnStreamErrorOccurred;
        _streamHandlersAttached = true;
    }

    private void DetachStreamStatusHandlers()
    {
        if (_streamHandlersAttached == false)
        {
            return;
        }

        _streamStatusManager.StreamStarted -= OnStreamStarted;
        _streamStatusManager.StreamStopped -= OnStreamStopped;
        _streamStatusManager.StatusChanged -= OnStreamStatusChanged;
        _streamStatusManager.ErrorOccurred -= OnStreamErrorOccurred;
        _streamHandlersAttached = false;
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

            await _streamStatusManager.InitializeAsync(_settings.ClientId, _twitchApi.Settings.AccessToken);
            await _streamStatusManager.StartMonitoringAsync(_settings.Channel);
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка инициализации мониторинга стрима: {ex.Message}");
        }
    }

    private void SendMessageSmart(string channel, string text)
    {
        foreach (var chunk in SplitByLimit(text, CharsLimit))
        {
            _client.SendMessage(channel, chunk);
        }
    }

    private void SendReplySmart(string channel, string replyToMessageId, string text)
    {
        foreach (var chunk in SplitByLimit(text, CharsLimit))
        {
            _client.SendReply(channel, replyToMessageId, chunk);
        }
    }
}
