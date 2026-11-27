using KeepShell.Services.Platform;
using KeepShell.Updates;
using KeepShell.Updates.Wpf;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed class UpdateBannerViewModel : UpdateBannerViewModelBase
{
    private readonly IUpdateEnvironment _environment;
    private readonly IDialogService _dialogService;
    private readonly ILogger<UpdateBannerViewModel> _logger;
    private readonly IMainWindowController _mainWindow;

    public UpdateBannerViewModel(
        IUpdateCoordinator coordinator,
        IUpdateEnvironment environment,
        IDialogService dialogService,
        ILogger<UpdateBannerViewModel> logger,
        IUiDispatcher uiDispatcher,
        IShellLauncher launcher,
        IMainWindowController mainWindow)
        : base(coordinator, uiDispatcher, launcher)
    {
        _environment = environment;
        _dialogService = dialogService;
        _logger = logger;
        _mainWindow = mainWindow;
    }

    protected override bool SupportsApplyNow => true;

    public async Task CancelInstallAsync()
    {
        CancelDownload();
        await Coordinator.CancelPendingDownloadsAsync();

        if (DownloadCommand.ExecutionTask is { } downloadTask)
        {
            await downloadTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    protected override Task<bool> ConfirmDownloadAsync(UpdateCandidate candidate, CancellationToken cancellationToken)
    {
        var confirmed = _dialogService.Confirm(
            "Установка обновления",
            $"Загрузить и установить версию {candidate.Version}?\n\nПриложение перезапустится автоматически.");
        return Task.FromResult(confirmed);
    }

    protected override Task<bool> ConfirmApplyAsync(PendingUpdate pending, CancellationToken cancellationToken)
    {
        var confirmed = _dialogService.Confirm(
            "Установка обновления",
            $"Установить загруженную версию {pending.Version} сейчас?\n\nПриложение перезапустится автоматически.");
        return Task.FromResult(confirmed);
    }

    protected override void OnDownloadReady(PendingUpdate pending)
    {
        if (Version.TryParse(pending.Version, out var version))
        {
            _logger.UpdateDownloaded(version);
        }

        _mainWindow.RequestClose();
    }

    protected override string DescribeAvailable(UpdateCandidate candidate)
    {
        return _environment is { } environment
            ? $"Доступна новая версия {candidate.Version} (установлена {environment.CurrentVersion})."
            : $"Доступна новая версия {candidate.Version}.";
    }

    protected override string DescribeReady(PendingUpdate pending)
    {
        return _environment is { } environment
            ? $"Версия {pending.Version} загружена и установится при выходе из приложения (установлена {environment.CurrentVersion})."
            : $"Версия {pending.Version} загружена и установится при выходе из приложения.";
    }
}
