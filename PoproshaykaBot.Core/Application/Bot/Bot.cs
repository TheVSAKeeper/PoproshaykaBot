using PoproshaykaBot.Core.Application.Broadcasting;
using PoproshaykaBot.Core.Application.Chat;
using PoproshaykaBot.Core.Application.Streaming;
using PoproshaykaBot.Core.Domain.Models.Settings;
using PoproshaykaBot.Core.Domain.Models.Stream;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.EventSub.Websockets.Core.EventArgs.Stream;

namespace PoproshaykaBot.Core.Application.Bot;

/// <summary>
/// Главный оркестратор бота.
/// Координирует работу всех компонентов и управляет событиями.
/// </summary>
public class Bot : IAsyncDisposable
{
    private readonly TwitchClient _client;
    private readonly TwitchChatMessenger _messenger;
    private readonly TwitchSettings _settings;
    private readonly AudienceTracker _audienceTracker;
    private readonly StreamStatusManager _streamStatusManager;
    private readonly BroadcastScheduler _broadcastScheduler;
    private readonly BotLifecycleManager _lifecycleManager;
    private readonly BotMessageHandler _messageHandler;

    private bool _disposed;
    private string? _channel;
    private bool _streamHandlersAttached;

    public Bot(
        TwitchSettings settings,
        TwitchClient client,
        TwitchChatMessenger messenger,
        TwitchAPI twitchApi,
        AudienceTracker audienceTracker,
        BroadcastScheduler broadcastScheduler,
        StreamStatusManager streamStatusManager,
        BotLifecycleManager lifecycleManager,
        BotMessageHandler messageHandler)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _messenger = messenger ?? throw new ArgumentNullException(nameof(messenger));
        _audienceTracker = audienceTracker ?? throw new ArgumentNullException(nameof(audienceTracker));
        _broadcastScheduler = broadcastScheduler ?? throw new ArgumentNullException(nameof(broadcastScheduler));
        _streamStatusManager = streamStatusManager ?? throw new ArgumentNullException(nameof(streamStatusManager));
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _messageHandler = messageHandler ?? throw new ArgumentNullException(nameof(messageHandler));

        _client.OnLog += Client_OnLog;
        _client.OnMessageReceived += Client_OnMessageReceived;
        _client.OnConnected += Client_OnConnected;
        _client.OnJoinedChannel += Сlient_OnJoinedChannel;

        _lifecycleManager.ConnectionProgress += msg => ConnectionProgress?.Invoke(msg);
        _lifecycleManager.LogMessage += msg => LogMessage?.Invoke(msg);

        AttachStreamStatusHandlers();
    }

    public event Action<string>? Connected;

    public event Action<string>? ConnectionProgress;

    public event Action<string>? LogMessage;

    public event Action? StreamStatusChanged;

    public bool IsBroadcastActive => _broadcastScheduler.IsActive;

    public StreamStatus StreamStatus => _streamStatusManager.CurrentStatus;
    public StreamInfo? CurrentStream => _streamStatusManager.CurrentStream;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return _lifecycleManager.ConnectAsync(cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        if (_client.IsConnected)
        {
            if (!string.IsNullOrWhiteSpace(_channel))
            {
                SendFarewellMessages();
                _audienceTracker.ClearAll();
            }
        }

        await _lifecycleManager.DisconnectAsync();
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

    // TODO: Вынести в настройки
    public void SendPunishmentMessage(string userName, ulong removedMessages)
    {
        if (_channel == null || !_client.IsConnected)
        {
            return;
        }

        var punishmentMessage = $"🏴‍☠️ ВНИМАНИЕ! Пользователь @{userName} был лично наказан СЕРЁГОЙ ПИРАТОМ! "
                                + $"⚔️ Убрано {removedMessages} сообщений из статистики. "
                                + $"💀 #пиратская_справедливость";

        _messenger.Send(_channel, punishmentMessage);
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
        _disposed = true;
    }

    private void OnStreamStarted(StreamOnlineArgs args)
    {
        if (_settings.AutoBroadcast.AutoBroadcastEnabled && !IsBroadcastActive)
        {
            StartBroadcast();
            LogMessage?.Invoke("🔴 Стрим запущен. Автоматически запускаю рассылку.");

            if (_settings.AutoBroadcast.StreamStatusNotificationsEnabled
                && !string.IsNullOrEmpty(_settings.AutoBroadcast.StreamStartMessage)
                && !string.IsNullOrEmpty(_channel))
            {
                _messenger.Send(_channel, _settings.AutoBroadcast.StreamStartMessage);
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
                && !string.IsNullOrEmpty(_settings.AutoBroadcast.StreamStopMessage)
                && !string.IsNullOrEmpty(_channel))
            {
                _messenger.Send(_channel, _settings.AutoBroadcast.StreamStopMessage);
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
            && !string.IsNullOrWhiteSpace(_settings.Messages.Connection))
        {
            _messenger.Send(e.Channel, _settings.Messages.Connection);
        }

        _channel = e.Channel;

        if (!_settings.AutoBroadcast.AutoBroadcastEnabled)
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
        _messageHandler.HandleMessage(e);
        LogMessage?.Invoke(e.ChatMessage.DisplayName + ": " + e.ChatMessage.Message);
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
        if (!_streamHandlersAttached)
        {
            return;
        }

        _streamStatusManager.StreamStarted -= OnStreamStarted;
        _streamStatusManager.StreamStopped -= OnStreamStopped;
        _streamStatusManager.StatusChanged -= OnStreamStatusChanged;
        _streamStatusManager.ErrorOccurred -= OnStreamErrorOccurred;
        _streamHandlersAttached = false;
    }

    private void SendFarewellMessages()
    {
        var messages = new List<string>();

        var collectiveFarewell = _audienceTracker.CreateCollectiveFarewell();
        if (!string.IsNullOrWhiteSpace(collectiveFarewell))
        {
            messages.Add(collectiveFarewell);
        }

        if (_settings.Messages.DisconnectionEnabled
            && !string.IsNullOrWhiteSpace(_settings.Messages.Disconnection))
        {
            messages.Add(_settings.Messages.Disconnection);
        }

        if (messages.Count > 0 && !string.IsNullOrWhiteSpace(_channel))
        {
            var combinedMessage = string.Join(" ", messages);
            _messenger.Send(_channel, combinedMessage);
        }
    }
}
