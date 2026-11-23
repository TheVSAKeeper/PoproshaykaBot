using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Dialogs;

public sealed partial class EmbeddedYouTubeChatLoginDialogViewModel(
    IShellLauncher shellLauncher,
    ILogger<EmbeddedYouTubeChatLoginDialogViewModel> logger) : ObservableObject
{
    public const string StartUrl = "https://accounts.google.com/ServiceLogin?service=youtube&continue=https%3A%2F%2Fwww.youtube.com%2F";

    private const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    [ObservableProperty]
    private string _title = "Вход в YouTube";

    [ObservableProperty]
    private string _statusMessage = "Войдите в нужный аккаунт Google и выберите канал YouTube, затем нажмите «Готово».";

    [ObservableProperty]
    private StatusSeverity _statusSeverity = StatusSeverity.Info;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _hasError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isBrowserReady;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private bool _isSignedIn;

    private string _accountDisplayLabel = string.Empty;

    [ObservableProperty]
    private bool _canDownloadRuntime;

    [ObservableProperty]
    private string _errorHeading = string.Empty;

    [ObservableProperty]
    private string _errorDescription = string.Empty;

    public ILogger Logger { get; } = logger;

    public string UserDataFolder => WebView2UserDataFolders.ResolveYouTubeAccounts();

    public string BrowserProfileName { get; private set; } = Guid.NewGuid().ToString("N");

    public EmbeddedYouTubeChatLoginResult Result { get; private set; } = EmbeddedYouTubeChatLoginResult.Canceled();

    public event EventHandler? CloseRequested;

    public void Configure(EmbeddedYouTubeChatLoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        BrowserProfileName = Guid.NewGuid().ToString("N");
        Result = EmbeddedYouTubeChatLoginResult.Canceled();
        HasError = false;
        IsBrowserReady = false;
        IsSignedIn = false;
        CanDownloadRuntime = false;
        ErrorHeading = string.Empty;
        ErrorDescription = string.Empty;

        var label = string.IsNullOrWhiteSpace(request.AccountLabel) ? request.AccountId : request.AccountLabel.Trim();
        _accountDisplayLabel = label;
        Title = $"Вход в YouTube – {label}";
        StatusMessage = $"Войдите в Google для «{label}» и дождитесь загрузки YouTube – после входа кнопка «Готово» станет активной.";
        StatusSeverity = StatusSeverity.Info;
    }

    public void SetBrowserReady()
    {
        if (HasError)
        {
            return;
        }

        IsBrowserReady = true;
    }

    public void SetSignedIn(bool signedIn)
    {
        if (HasError)
        {
            return;
        }

        IsSignedIn = signedIn;
        if (signedIn)
        {
            StatusMessage = $"Вход в YouTube выполнен. Выберите нужный канал для «{_accountDisplayLabel}» и нажмите «Готово».";
            StatusSeverity = StatusSeverity.Success;
        }
        else
        {
            StatusMessage = $"Войдите в Google для «{_accountDisplayLabel}» и дождитесь загрузки YouTube – после входа кнопка «Готово» станет активной.";
            StatusSeverity = StatusSeverity.Info;
        }
    }

    public void ShowRuntimeMissing()
    {
        HasError = true;
        IsBrowserReady = false;
        IsSignedIn = false;
        CanDownloadRuntime = true;
        ErrorHeading = "Требуется Microsoft Edge WebView2 Runtime";
        ErrorDescription = "Установите его для встроенного входа в YouTube либо отправляйте сообщения через API.";
        StatusMessage = "WebView2 Runtime не найден.";
        StatusSeverity = StatusSeverity.Warning;
        Result = EmbeddedYouTubeChatLoginResult.Unavailable("WebView2 Runtime не установлен.");
    }

    public void ShowInitializationFailure()
    {
        HasError = true;
        IsBrowserReady = false;
        IsSignedIn = false;
        CanDownloadRuntime = false;
        ErrorHeading = "Не удалось открыть встроенный браузер";
        ErrorDescription = "Попробуйте повторить вход позже либо используйте отправку через API. Подробности – в журнале.";
        StatusMessage = "Ошибка инициализации встроенного браузера.";
        StatusSeverity = StatusSeverity.Error;
        Result = EmbeddedYouTubeChatLoginResult.Failed("Не удалось открыть встроенный браузер для входа в YouTube.");
    }

    public void Cancel()
    {
        if (Result.Outcome == EmbeddedYouTubeChatLoginOutcome.None)
        {
            Result = EmbeddedYouTubeChatLoginResult.Canceled();
        }
    }

    private bool CanConfirm() => !HasError && IsBrowserReady && IsSignedIn;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = EmbeddedYouTubeChatLoginResult.Completed(BrowserProfileName);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CloseDialog()
    {
        if (Result.Outcome != EmbeddedYouTubeChatLoginOutcome.Completed)
        {
            Result = EmbeddedYouTubeChatLoginResult.Canceled();
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void DownloadRuntime()
    {
        shellLauncher.Open(RuntimeDownloadUrl);
    }
}
