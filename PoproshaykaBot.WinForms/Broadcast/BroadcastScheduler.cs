using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Broadcast;

public sealed class BroadcastScheduler(TwitchChatMessenger messenger, SettingsManager settingsManager, Func<int, string> messageProvider)
    : IAsyncDisposable
{
    private TimeSpan _interval = TimeSpan.FromMinutes(15);
    private PeriodicTimer? _timer;
    private string? _channel;
    private int _counter;
    private bool _disposed;
    private Task? _runnerTask;

    public bool IsActive => _runnerTask is { IsCompleted: false };

    public void Start(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        _channel = channel;
        _counter = 0;

        var minutes = Math.Max(1, settingsManager.Current.Twitch.AutoBroadcast.BroadcastIntervalMinutes);
        _interval = TimeSpan.FromMinutes(minutes);
        _timer ??= new(_interval);
        _runnerTask ??= RunLoopAsync();
    }

    public void Stop()
    {
        _channel = null;
        _counter = 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        Stop();
        _timer?.Dispose();
        _timer = null;

        if (_runnerTask != null)
        {
            try
            {
                await _runnerTask;
            }
            catch
            {
            }
        }

        _disposed = true;
    }

    private async Task RunLoopAsync()
    {
        try
        {
            while (_channel != null && _timer != null && await _timer.WaitForNextTickAsync())
            {
                if (string.IsNullOrWhiteSpace(_channel))
                {
                    continue;
                }

                _counter++;
                var message = messageProvider(_counter);

                if (string.IsNullOrWhiteSpace(message) == false)
                {
                    messenger.Send(_channel, message);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception)
        {
            // TODO: логирование ошибок рассылки
        }
    }
}
