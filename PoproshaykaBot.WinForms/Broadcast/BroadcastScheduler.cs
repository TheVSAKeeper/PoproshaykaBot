using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Broadcast;

public sealed class BroadcastScheduler(TwitchChatMessenger messenger, SettingsManager settingsManager, Func<int, string> messageProvider)
    : IAsyncDisposable
{
    private TimeSpan _interval = TimeSpan.FromMinutes(15);
    private PeriodicTimer? _timer;
    private string? _channel;
    private bool _disposed;
    private Task? _runnerTask;
    private CancellationTokenSource? _stopCts;

    public event Action? StateChanged;

    public bool IsActive => _runnerTask is { IsCompleted: false } && _stopCts is { IsCancellationRequested: false };
    public int SentMessagesCount { get; private set; }

    public DateTime? NextBroadcastTime { get; private set; }

    public void Start(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            return;
        }

        _channel = channel;
        SentMessagesCount = 0;

        var minutes = Math.Max(1, settingsManager.Current.Twitch.AutoBroadcast.BroadcastIntervalMinutes);
        _interval = TimeSpan.FromMinutes(minutes);

        _stopCts?.Cancel();
        _stopCts?.Dispose();
        _stopCts = new();

        _timer?.Dispose();
        _timer = new(_interval);

        NextBroadcastTime = DateTime.Now + _interval;
        _runnerTask = RunLoopAsync(_stopCts.Token);

        StateChanged?.Invoke();
    }

    public void Stop()
    {
        _channel = null;
        SentMessagesCount = 0;
        NextBroadcastTime = null;
        _stopCts?.Cancel();
        StateChanged?.Invoke();
    }

    public Task ManualSendAsync()
    {
        if (string.IsNullOrWhiteSpace(_channel))
        {
            return Task.CompletedTask;
        }

        SentMessagesCount++;
        var message = messageProvider(SentMessagesCount);

        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        messenger.Send(_channel, message);
        StateChanged?.Invoke();

        return Task.CompletedTask;
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
        _stopCts?.Dispose();
        _stopCts = null;

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

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _channel != null && _timer != null)
            {
                await _timer.WaitForNextTickAsync(cancellationToken);

                var channel = _channel;
                if (string.IsNullOrWhiteSpace(channel))
                {
                    break;
                }

                SentMessagesCount++;
                var message = messageProvider(SentMessagesCount);

                if (string.IsNullOrWhiteSpace(message))
                {
                    continue;
                }

                messenger.Send(channel, message);
                NextBroadcastTime = DateTime.Now + _interval;
                StateChanged?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception)
        {
            // TODO: логирование ошибок рассылки
        }
        finally
        {
            _ = Interlocked.CompareExchange(ref _runnerTask, null, _runnerTask);
        }
    }
}
