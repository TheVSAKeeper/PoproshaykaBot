using TwitchLib.Client;
using TwitchLib.Client.Events;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;

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
        if (_client.IsConnected)
        {
            _client.Disconnect();
        }
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
        var connectionMessage = $"Подключен к каналу {e.AutoJoinChannel}";
        Console.WriteLine(connectionMessage);
        Connected?.Invoke(connectionMessage);

        _client.SendMessage(e.AutoJoinChannel, "ЭШКЕРЕ");
    }

    private void Client_OnMessageReceived(object sender, OnMessageReceivedArgs e)
    {
        if (e.ChatMessage.Message.ToLower() == "!привет")
        {
            _client.SendMessage(e.ChatMessage.Channel, $"Привет, {e.ChatMessage.Username}!");
        }
    }
}
