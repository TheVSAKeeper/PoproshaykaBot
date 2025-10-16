using PoproshaykaBot.Core.Application.Chat;
using PoproshaykaBot.Core.Application.Statistics;
using PoproshaykaBot.Core.Application.Streaming;
using PoproshaykaBot.Core.Domain.Models.Settings;
using TwitchLib.Api;
using TwitchLib.Client;

namespace PoproshaykaBot.Core.Application.Bot;

/// <summary>
/// Менеджер жизненного цикла бота.
/// Отвечает за подключение, отключение и инициализацию компонентов.
/// </summary>
public class BotLifecycleManager
{
    private readonly TwitchClient _client;
    private readonly StatisticsCollector _statisticsCollector;
    private readonly ChatDecorationsProvider _chatDecorations;
    private readonly StreamStatusManager _streamStatusManager;
    private readonly TwitchSettings _settings;
    private readonly TwitchAPI _twitchApi;

    public BotLifecycleManager(
        TwitchClient client,
        StatisticsCollector statisticsCollector,
        ChatDecorationsProvider chatDecorations,
        StreamStatusManager streamStatusManager,
        TwitchSettings settings,
        TwitchAPI twitchApi)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _statisticsCollector = statisticsCollector ?? throw new ArgumentNullException(nameof(statisticsCollector));
        _chatDecorations = chatDecorations ?? throw new ArgumentNullException(nameof(chatDecorations));
        _streamStatusManager = streamStatusManager ?? throw new ArgumentNullException(nameof(streamStatusManager));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _twitchApi = twitchApi ?? throw new ArgumentNullException(nameof(twitchApi));
    }

    public event Action<string>? ConnectionProgress;
    public event Action<string>? LogMessage;

    /// <summary>
    /// Подключает бота к серверу Twitch и инициализирует все компоненты.
    /// </summary>
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
            await ConnectToTwitchAsync(cancellationToken);
            await InitializeStatisticsAsync();
            await InitializeDecorationsAsync();

            if (_settings.AutoBroadcast.AutoBroadcastEnabled)
            {
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

    /// <summary>
    /// Отключает бота от сервера Twitch.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_client.IsConnected)
        {
            _client.Disconnect();
        }

        if (_settings.AutoBroadcast.AutoBroadcastEnabled)
        {
            await _streamStatusManager.StopMonitoringAsync();
        }

        await _statisticsCollector.StopAsync();
    }

    private async Task ConnectToTwitchAsync(CancellationToken cancellationToken)
    {
        var connectingMessage = "Подключение к серверу Twitch...";
        ConnectionProgress?.Invoke(connectingMessage);
        LogMessage?.Invoke(connectingMessage);

        _client.Connect();

        var timeout = TimeSpan.FromSeconds(30);
        var startTime = DateTime.UtcNow;

        while (!_client.IsConnected && DateTime.UtcNow - startTime < timeout)
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
    }

    private async Task InitializeStatisticsAsync()
    {
        var statsMessage = "Инициализация статистики...";
        ConnectionProgress?.Invoke(statsMessage);
        LogMessage?.Invoke(statsMessage);

        await _statisticsCollector.StartAsync();
        _statisticsCollector.ResetBotStartTime();
    }

    private async Task InitializeDecorationsAsync()
    {
        var emotesMessage = "Загрузка эмодзи и бэйджей...";
        ConnectionProgress?.Invoke(emotesMessage);
        LogMessage?.Invoke(emotesMessage);

        await _chatDecorations.LoadAsync();

        LogMessage?.Invoke($"Загружено {_chatDecorations.GlobalEmotesCount} глобальных эмодзи и {_chatDecorations.GlobalBadgeSetsCount} типов глобальных бэйджей");
    }

    private async Task InitializeStreamMonitoringAsync()
    {
        var streamMessage = "Инициализация мониторинга стрима...";
        ConnectionProgress?.Invoke(streamMessage);
        LogMessage?.Invoke(streamMessage);

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
}
