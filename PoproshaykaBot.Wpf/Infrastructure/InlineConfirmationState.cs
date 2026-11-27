using KeepShell.Services.Platform;

namespace PoproshaykaBot.Wpf.Infrastructure;

public sealed class InlineConfirmationState : IDisposable
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    private readonly IUiTimer? _timer;
    private readonly Action? _onChanged;

    public InlineConfirmationState(IUiDispatcher? uiDispatcher, Action? onChanged = null, TimeSpan? timeout = null)
    {
        _onChanged = onChanged;
        if (uiDispatcher is not null)
        {
            _timer = uiDispatcher.CreateTimer(timeout ?? DefaultTimeout, OnTimerTick);
        }
    }

    public bool IsArmed { get; private set; }

    public void Arm()
    {
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
