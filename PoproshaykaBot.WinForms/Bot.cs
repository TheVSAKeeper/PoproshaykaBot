using System.Text.Json;
using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Interfaces;
using TwitchLib.Communication.Models;
using static PoproshaykaBot.WinForms.Bot;

namespace PoproshaykaBot.WinForms;

public class Bot : IDisposable
{
    private readonly TwitchClient _client;
    private readonly TwitchSettings _settings;
    private bool _disposed;

    public Bot(string accessToken) : this(accessToken, new())
    {
    }

    public Bot(string accessToken, TwitchSettings settings)
    {
        _settings = settings;

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

        var x = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PoproshaykaBot", "counters.txt");

        if (File.Exists(x))
        {

            string json = File.ReadAllText(x);
            var persons = JsonSerializer.Deserialize<List<Person>>(json);
            _persons = persons.ToDictionary(x => x.UserId, x => x);
        }
        else
        {
            _persons = new Dictionary<string, Person>();
        }

        _timer2 = new System.Timers.Timer();
        _timer2.Interval = 30_000;
        _timer2.Elapsed += Timer_Elapsed;
        _timer2.Start();
    }

    private void Timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        SaveCounters();
    }

    bool hasChanges;
    private void SaveCounters()
    {
        if (!hasChanges)
        {
            return;
        }
        hasChanges = false;

        var x = _persons.Values.ToList();
        string json = JsonSerializer.Serialize(x, new JsonSerializerOptions { WriteIndented = true });
        var x2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PoproshaykaBot", "counters.txt");
        File.WriteAllText(x2, json);
    }

    public class Person
    {
        public string UserId { get; set; }
        public string Name { get; set; }
        public int MessageCount { get; set; }
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
        }
        catch (OperationCanceledException)
        {
            ConnectionProgress?.Invoke("Подключение отменено пользователем");
            throw;
        }
        catch (Exception ex)
        {
            ConnectionProgress?.Invoke($"Ошибка подключения: {ex.Message}");
            throw;
        }
    }

    public void Disconnect()
    {
        _timer?.Dispose();
        if (_client.IsConnected)
        {
            _client.Disconnect();
        }

        SaveCounters();
        _timer2.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed || disposing == false)
        {
            return;
        }

        Disconnect();
        _disposed = true;
    }

    private void Client_OnLog(object sender, OnLogArgs e)
    {
        var logMessage = $"{e.DateTime}: {e.BotUsername} - {e.Data}";
        Console.WriteLine(logMessage);
        LogMessage?.Invoke(logMessage);
    }

    private void Client_OnConnected(object sender, OnConnectedArgs e)
    {
        var connectionMessage = $"Подключен!";
        Console.WriteLine(connectionMessage);
        Connected?.Invoke(connectionMessage);
    }

    private string _channel;
    private System.Timers.Timer _timer;
    private System.Timers.Timer _timer2;

    private void Сlient_OnJoinedChannel(object? sender, OnJoinedChannelArgs e)
    {
        var connectionMessage = $"Подключен к каналу {e.Channel}";
        Console.WriteLine(connectionMessage);
        Connected?.Invoke(connectionMessage);

        _client.SendMessage(e.Channel, "ЭШКЕРЕ");
        _channel = e.Channel;
        _timer = new System.Timers.Timer();
        _timer.Interval = 60_000;
        _timer.Elapsed += _timer_Elapsed;
        _timer.Start();
    }

    private int X1 = 0;
    private Dictionary<string, Person> _persons;

    private void _timer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        X1++;
        _client.SendMessage(_channel, "Присылайте деняк, пожалуйста, " + X1 + " раз прошу");
    }

    private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
    {
        if (!_persons.ContainsKey(e.ChatMessage.UserId))
        {
            _persons[e.ChatMessage.UserId] = new Person { UserId = e.ChatMessage.UserId, Name = e.ChatMessage.Username, MessageCount = 0 };
        }
        _persons[e.ChatMessage.UserId].MessageCount++;
        hasChanges = true;

        if (e.ChatMessage.Message.ToLower() == "!привет")
        {
            _client.SendMessage(e.ChatMessage.Channel, $"Привет, {e.ChatMessage.Username}!");
        }
        if (e.ChatMessage.Message.ToLower() == "!деньги")
        {
            _client.SendMessage(e.ChatMessage.Channel, $"Принимаем криптой, СБП, куаркод справа снизу, подробнее можно узнать в телеге https://t.me/bobito217");
        }
        if (e.ChatMessage.Message.ToLower() == "!сколькосообщений")
        {
            _client.SendReply(e.ChatMessage.Channel, e.ChatMessage.Id, $"У тебя " + _persons[e.ChatMessage.UserId].MessageCount + " сообщений");
        }
        LogMessage?.Invoke(e.ChatMessage.DisplayName + ": " + e.ChatMessage.Message);
    }
}
