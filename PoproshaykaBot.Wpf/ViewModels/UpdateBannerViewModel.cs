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
    private readonly IShellLauncher _launcher;
    private readonly IMainWindowController _mainWindow;

    private bool _skipNextConfirm;

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
        _launcher = launcher;
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

    protected override async Task ShowNotesAsync(UpdateCandidate candidate, CancellationToken cancellationToken)
    {
        var isReady = Stage == UpdateStage.ReadyToApply && Coordinator.Pending is not null;
        var dialog = UpdateDetailsDialogViewModel.ForCandidate(
            candidate,
            _environment.CurrentVersion,
            UpdateDeliveryMode.SingleFileSwap,
            isReady);

        if (!await _dialogService.ShowAsync(dialog).ConfigureAwait(true))
        {
            return;
        }

        if (dialog.ChosenAction == UpdateDetailsAction.OpenReleasePage)
        {
            _launcher.Open(candidate.NotesUrl);
            return;
        }

        if (dialog.ChosenAction != UpdateDetailsAction.Install)
        {
            return;
        }

        _skipNextConfirm = true;

        try
        {
            if (isReady)
            {
                await ApplyCommand.ExecuteAsync(null).ConfigureAwait(true);
            }
            else
            {
                await DownloadCommand.ExecuteAsync(null).ConfigureAwait(true);
            }
        }
        finally
        {
            _skipNextConfirm = false;
        }
    }

    protected override async Task<bool> ConfirmDownloadAsync(UpdateCandidate candidate, CancellationToken cancellationToken)
    {
        if (_skipNextConfirm)
        {
            return true;
        }

        var dialog = UpdateDetailsDialogViewModel.ForCandidate(
            candidate,
            _environment.CurrentVersion,
            UpdateDeliveryMode.SingleFileSwap);

        if (!await _dialogService.ShowAsync(dialog).ConfigureAwait(true))
        {
            return false;
        }

        if (dialog.ChosenAction == UpdateDetailsAction.OpenReleasePage)
        {
            _launcher.Open(candidate.NotesUrl);
            return false;
        }

        return dialog.ChosenAction == UpdateDetailsAction.Install;
    }

    protected override Task<bool> ConfirmApplyAsync(PendingUpdate pending, CancellationToken cancellationToken)
    {
        if (_skipNextConfirm)
        {
            return Task.FromResult(true);
        }

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
