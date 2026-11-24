using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Dialogs;

public sealed partial class EmbeddedPlatformChatLoginDialogViewModel(
    IShellLauncher shellLauncher,
    ILogger<EmbeddedPlatformChatLoginDialogViewModel> logger) : ObservableObject
{
    private const string RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    [ObservableProperty]
    private string _title = "Вход в веб-чат";

    [ObservableProperty]
    private string _closeButtonName = "Закрыть окно входа";

    [ObservableProperty]
    private string _statusMessage = "Войдите в аккаунт и нажмите «Готово».";

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

    public PlatformWebChatLoginDescriptor Descriptor { get; private set; } = PlatformWebChatDescriptors.YouTube;

    public string BrowserProfileName { get; private set; } = Guid.NewGuid().ToString("N");

    public PlatformWebChatLoginResult Result { get; private set; } = PlatformWebChatLoginResult.Canceled();

    public event EventHandler? CloseRequested;

    public void Configure(PlatformWebChatLoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Descriptor);

        Descriptor = request.Descriptor;
        BrowserProfileName = Guid.NewGuid().ToString("N");
        Result = PlatformWebChatLoginResult.Canceled();
        HasError = false;
        IsBrowserReady = false;
        IsSignedIn = false;
        CanDownloadRuntime = false;
        ErrorHeading = string.Empty;
        ErrorDescription = string.Empty;

        var label = string.IsNullOrWhiteSpace(request.AccountLabel) ? request.AccountId : request.AccountLabel.Trim();
        _accountDisplayLabel = label;
        Title = $"Вход в {Descriptor.PlatformDisplayName} – {label}";
        CloseButtonName = $"Закрыть окно входа {Descriptor.PlatformDisplayName}";
        StatusMessage = string.Format(Descriptor.ReadyInstructionTemplate, label);
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
            StatusMessage = string.Format(Descriptor.SignedInInstructionTemplate, _accountDisplayLabel);
            StatusSeverity = StatusSeverity.Success;
        }
        else
        {
            StatusMessage = string.Format(Descriptor.ReadyInstructionTemplate, _accountDisplayLabel);
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
        ErrorDescription = $"Установите его для встроенного входа в веб-чат {Descriptor.PlatformDisplayName}.";
        StatusMessage = "WebView2 Runtime не найден.";
        StatusSeverity = StatusSeverity.Warning;
        Result = PlatformWebChatLoginResult.Unavailable("WebView2 Runtime не установлен.");
    }

    public void ShowInitializationFailure()
    {
        HasError = true;
        IsBrowserReady = false;
        IsSignedIn = false;
        CanDownloadRuntime = false;
        ErrorHeading = "Не удалось открыть встроенный браузер";
        ErrorDescription = "Попробуйте повторить вход позже. Подробности – в журнале.";
        StatusMessage = "Ошибка инициализации встроенного браузера.";
        StatusSeverity = StatusSeverity.Error;
        Result = PlatformWebChatLoginResult.Failed($"Не удалось открыть встроенный браузер для входа в {Descriptor.PlatformDisplayName}.");
    }

    public void Cancel()
    {
        if (Result.Outcome == PlatformWebChatLoginOutcome.None)
        {
            Result = PlatformWebChatLoginResult.Canceled();
        }
    }

    private bool CanConfirm() => !HasError && IsBrowserReady && IsSignedIn;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        Result = PlatformWebChatLoginResult.Completed(BrowserProfileName);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void CloseDialog()
    {
        if (Result.Outcome == PlatformWebChatLoginOutcome.None)
        {
            Result = PlatformWebChatLoginResult.Canceled();
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void DownloadRuntime()
    {
        shellLauncher.Open(RuntimeDownloadUrl);
    }
}
