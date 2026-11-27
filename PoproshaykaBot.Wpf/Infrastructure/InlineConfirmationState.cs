using KeepShell.Services.Platform;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class InlineConfirmationState : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly IUiTimer? _timer;
    private readonly Action? _onChanged;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _timeout;
    private long? _armedTimestamp;

    public InlineConfirmationState(
        IUiDispatcher? uiDispatcher,
        Action? onChanged = null,
        TimeSpan? timeout = null,
        TimeProvider? timeProvider = null)
    {
        _onChanged = onChanged;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timeout = timeout ?? DefaultTimeout;
        if (uiDispatcher is not null)
        {
            _timer = uiDispatcher.CreateTimer(_timeout, OnTimerTick);
        }
    }

    public bool IsArmed { get; private set; }

    public void Arm()
    {
        _armedTimestamp = _timeProvider.GetTimestamp();
        if (IsArmed)
        {
            _timer?.Stop();
            _timer?.Start();
            return;
        }

        IsArmed = true;
        _timer?.Start();
        _onChanged?.Invoke();
    }

    public void Disarm()
    {
        _timer?.Stop();
        _armedTimestamp = null;
        if (!IsArmed)
        {
            return;
        }

        IsArmed = false;
        _onChanged?.Invoke();
    }

    public bool TryConfirmOrArm()
    {
        if (IsArmed)
        {
            if (_armedTimestamp is { } armedAt && _timeProvider.GetElapsedTime(armedAt) >= _timeout)
            {
                Disarm();
                Arm();
                return false;
            }

            Disarm();
            return true;
        }

        Arm();
        return false;
    }

    public void Dispose()
    {
        _timer?.Stop();
    }

    private void OnTimerTick()
    {
        Disarm();
    }
}
