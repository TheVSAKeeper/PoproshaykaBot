using System.Globalization;
using System.Timers;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using Timer = System.Timers.Timer;

namespace PoproshaykaBot.WinForms;

public class Bot : IAsyncDisposable
{
    private readonly TwitchClient _client;
    private readonly TwitchSettings _settings;
    private readonly StatisticsCollector _statisticsCollector;
    private bool _disposed;

    private string _channel;
    private Timer _timer;

    private int X1;

    public Bot(string accessToken, TwitchSettings settings, StatisticsCollector statisticsCollector)
    {
        _settings = settings;
        _statisticsCollector = statisticsCollector;

        ConnectionCredentials credentials = new(_settings.BotUsername, accessToken);

        ClientOptions clientOptions = new()
        {
            MessagesAllowedInPeriod = _settings.MessagesAllowedInPeriod,
            ThrottlingPeriod = TimeSpan.FromSeconds(_settings.ThrottlingPeriodSeconds),
        };

        WebSocketClient customClient = new(clientOptions);
        _client = new(customClient);
        _client.Initialize(credentials, _settings.Channel);

        _client.OnLog += Client_OnLog;
        _client.OnMessageReceived += Client_OnMessageReceived;
        _client.OnConnected += Client_OnConnected;
        _client.OnJoinedChannel += Сlient_OnJoinedChannel;
    }

    public event Action<string>? Connected;

    public event Action<string>? ConnectionProgress;

    public event Action<string>? LogMessage;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_client.IsConnected)
        {
            ConnectionProgress?.Invoke("Бот уже подключен");
            return;
        }

        ConnectionProgress?.Invoke("Инициализация подключения...");

        try
        {
            // TODO: background worker
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ConnectionProgress?.Invoke("Подключение к серверу Twitch...");

                _client.Connect();

                var timeout = TimeSpan.FromSeconds(30);
                var startTime = DateTime.UtcNow;

                while (_client.IsConnected == false && DateTime.UtcNow - startTime < timeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ConnectionProgress?.Invoke("Ожидание подтверждения подключения...");
                    Thread.Sleep(500);
                }

                if (_client.IsConnected)
                {
                    ConnectionProgress?.Invoke("Подключение установлено успешно");
                }
                else
                {
                    throw new TimeoutException("Превышено время ожидания подключения к Twitch");
                }
            }, cancellationToken);

            ConnectionProgress?.Invoke("Инициализация статистики...");
            await _statisticsCollector.StartAsync();
            _statisticsCollector.ResetBotStartTime();
        }
        catch (OperationCanceledException)
        {
            ConnectionProgress?.Invoke("Подключение отменено пользователем");
            throw;
        }
        catch (Exception exception)
        {
            ConnectionProgress?.Invoke($"Ошибка подключения: {exception.Message}");
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        _timer?.Dispose();

        if (_client.IsConnected)
        {
            if (string.IsNullOrWhiteSpace(_channel) == false)
            {
                _client.SendMessage(_channel, "Пока-пока! ❤️");
            }

            _client.Disconnect();
        }

        await _statisticsCollector.StopAsync();
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
        await _statisticsCollector.DisposeAsync();
        _disposed = true;
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
    }

    private void Сlient_OnJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        var connectionMessage = $"Подключен к каналу {e.Channel}";
        Console.WriteLine(connectionMessage);
        Connected?.Invoke(connectionMessage);

        _client.SendMessage(e.Channel, "ЭЩКЕРЕ");
        _channel = e.Channel;
        X1 = 0;
        _timer = new();
        _timer.Interval = 60_000;
        _timer.Elapsed += _timer_Elapsed;
        _timer.Start();
    }

    private void _timer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        X1++;
        _client.SendMessage(_channel, "Присылайте деняк, пожалуйста, " + X1 + " раз прошу");
    }

    private void Client_OnMessageReceived(object? sender, OnMessageReceivedArgs e)
    {
        _statisticsCollector.TrackMessage(e.ChatMessage.UserId, e.ChatMessage.Username);

        switch (e.ChatMessage.Message.ToLower())
        {
            case "!привет":
                _client.SendMessage(e.ChatMessage.Channel, $"Привет, {e.ChatMessage.Username}!");
                break;

            case "!деньги":
                _client.SendMessage(e.ChatMessage.Channel, "Принимаем криптой, СБП, куаркод справа снизу, подробнее можно узнать в телеге https://t.me/bobito217");
                break;

            case "!сколькосообщений":
                {
                    var userStats = _statisticsCollector.GetUserStatistics(e.ChatMessage.UserId);
                    var messageCount = userStats?.MessageCount ?? 0;
                    _client.SendReply(e.ChatMessage.Channel, e.ChatMessage.Id, $"У тебя {FormatNumber(messageCount)} сообщений");
                    break;
                }

            case "!статистикабота":
                {
                    var botStats = _statisticsCollector.GetBotStatistics();
                    var uptime = FormatTimeSpan(botStats.TotalUptime);
                    var totalMessages = FormatNumber(botStats.TotalMessagesProcessed);
                    var startTime = FormatDateTime(botStats.BotStartTime);

                    var response = $"📊 Статистика бота: Обработано {totalMessages} сообщений | Время работы: {uptime} | Запущен: {startTime}";

                    _client.SendMessage(e.ChatMessage.Channel, response);
                    break;
                }

            case "!топпользователи":
                {
                    var topUsers = _statisticsCollector.GetTopUsers(5);

                    if (topUsers.Count == 0)
                    {
                        _client.SendMessage(e.ChatMessage.Channel, "Пока нет данных о пользователях");
                        return;
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

                    _client.SendMessage(e.ChatMessage.Channel, response);
                    break;
                }

            case "!мойпрофиль":
                {
                    var userStats = _statisticsCollector.GetUserStatistics(e.ChatMessage.UserId);

                    if (userStats == null)
                    {
                        _client.SendReply(e.ChatMessage.Channel, e.ChatMessage.Id, "У тебя пока нет статистики");
                        return;
                    }

                    var messageCount = FormatNumber(userStats.MessageCount);
                    var firstSeen = FormatDateTime(userStats.FirstSeen);
                    var lastSeen = FormatDateTime(userStats.LastSeen);

                    var response = $"👤 Твой профиль: {messageCount} сообщений | Впервые: {firstSeen} | Последний раз: {lastSeen}";
                    _client.SendReply(e.ChatMessage.Channel, e.ChatMessage.Id, response);
                    break;
                }
        }

        LogMessage?.Invoke(e.ChatMessage.DisplayName + ": " + e.ChatMessage.Message);
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
}
