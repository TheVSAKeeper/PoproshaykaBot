using KeepShell.Services.Platform;

namespace PoproshaykaBot.Wpf.Infrastructure;

public static class UiAsync
{
    public static IUiTimer CreateAsyncTimer(this IUiDispatcher dispatcher, TimeSpan interval, Func<Task> tick, Action<Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentNullException.ThrowIfNull(onFailure);

        return dispatcher.CreateTimer(interval, () => _ = RunGuardedAsync(tick, onFailure));
    }

    public static async Task RunGuardedAsync(Func<Task> action, Action<Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(onFailure);

        try
        {
            await action();
        }
        catch (OperationCanceledException exception) when (exception.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            onFailure(exception);
        }
    }
}
