namespace PoproshaykaBot.WinForms.Infrastructure.Events;

public sealed class UiEventDispatcher
{
    private SynchronizationContext? _uiContext;

    public bool IsInitialized => _uiContext is not null;

    public void CaptureCurrent()
    {
        _uiContext = SynchronizationContext.Current;
    }

    public Task PostAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var context = _uiContext;

        if (context is null)
        {
            action();
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        context.Post(_ =>
        {
            try
            {
                action();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }, null);

        return completion.Task;
    }
}
