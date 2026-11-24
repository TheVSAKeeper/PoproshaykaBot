using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Wpf.ViewModels.Dialogs;

public interface IEmbeddedOAuthWindowViewModel
{
    event Action<string>? NavigateRequested;

    event EventHandler? CloseRequested;

    ILogger Logger { get; }

    string UserDataFolder { get; }

    string BrowserProfileName { get; }

    bool ShouldKeepBrowserProfile { get; }

    Task RunFlowAsync();

    void Cancel();

    void ShowRuntimeMissing();

    void ShowInitializationFailure();
}
