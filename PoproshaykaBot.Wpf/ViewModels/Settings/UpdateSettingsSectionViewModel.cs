using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using KeepShell.Updates;
using KeepShell.Updates.Wpf;
using PoproshaykaBot.Wpf.Infrastructure;
using System.ComponentModel;
using System.Globalization;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class UpdateSettingsSectionViewModel : UpdateSettingsSectionViewModelBase
{
    private readonly IDialogService _dialogService;
    private readonly IShellLauncher _launcher;
    private readonly IMainWindowController _mainWindow;
    private readonly Lock _installLock = new();
    private readonly CancellationTokenSource _closeCts = new();

    private Task<bool>? _activeInstallTask;
    private bool _isInstalling;
    private bool _isClosePending;
    private bool _disposed;

    [ObservableProperty]
    private bool _autoInstall;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowChangelogCommand))]
    [NotifyPropertyChangedFor(nameof(CanCheckNow))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowChangelogCommand))]
    private bool _isLoadingChangelog;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _currentVersionText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private StatusSeverity _statusSeverity;

    [ObservableProperty]
    private string _repositoryHintText = string.Empty;

    [ObservableProperty]
    private StatusSeverity _repositoryHintSeverity;

    public UpdateSettingsSectionViewModel(
        IUpdateSettings updateSettings,
        IUpdateCoordinator coordinator,
        UpdateOptions options,
        IUpdateEnvironment environment,
        IDialogService dialogService,
        IUiDispatcher uiDispatcher,
        IShellLauncher launcher,
        IMainWindowController mainWindow)
        : base(updateSettings, coordinator, options, environment, uiDispatcher)
    {
        _dialogService = dialogService;
        _launcher = launcher;
        _mainWindow = mainWindow;

        _autoInstall = ApplyMode == UpdateApplyMode.Automatic;
        CurrentVersionText = $"Текущая версия: {Environment.CurrentVersion} (.NET {System.Environment.Version.Major})";
        RefreshRepositoryHint();
        RefreshStatus();
    }

    protected override bool AutoSave => false;

    public bool CanCheckNow => !IsBusy && Coordinator.IsUpdatable;

    public void Reload()
    {
        ReloadFromSettings();
        AutoInstall = ApplyMode == UpdateApplyMode.Automatic;
        RefreshRepositoryHint();
        RefreshStatus();
    }

    public void Save()
    {
        ApplyToSettings();
        OnPropertyChanged(nameof(CanCheckNow));
        RefreshRepositoryHint();
        RefreshStatus();
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    public void ResetInstallState()
    {
        lock (_installLock)
        {
            if (_activeInstallTask is { IsCompleted: false })
            {
                return;
            }
        }

        if (!_isClosePending && !IsBusy)
        {
            return;
        }

        _isClosePending = false;
        UpdateBusyState();
        RefreshStatus();
    }

    public async Task CancelInstallAsync()
    {
        Task? active;

        lock (_installLock)
        {
            if (_disposed)
            {
                return;
            }

            active = _activeInstallTask;
        }

        try
        {
            await _closeCts.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        await Coordinator.CancelPendingDownloadsAsync();

        if (active is not null)
        {
            await active.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            bool canDisposeCts;

            lock (_installLock)
            {
                if (_disposed)
                {
                    base.Dispose(disposing);
                    return;
                }

                _disposed = true;
                canDisposeCts = _activeInstallTask is null;
            }

            try
            {
                _closeCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            if (canDisposeCts)
            {
                _closeCts.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    protected override void OnCoordinatorSynced()
    {
        DownloadProgress = IsDownloading ? Progress : 0;
        UpdateBusyState();
        RefreshStatus();
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        switch (e.PropertyName)
        {
            case nameof(AllowFrameworkDependentUpdate):
            case nameof(IsUpdatable):
                OnPropertyChanged(nameof(CanCheckNow));
                RefreshStatus();
                InstallUpdateCommand.NotifyCanExecuteChanged();
                break;
            case nameof(RepositoryOverride):
                RefreshRepositoryHint();
                break;
            case nameof(IsChecking):
            case nameof(IsDownloading):
                UpdateBusyState();
                break;
        }
    }

    partial void OnAutoInstallChanged(bool value)
    {
        ApplyMode = value ? UpdateApplyMode.Automatic : UpdateApplyMode.Notify;
    }

    [RelayCommand]
    private void IncreaseInterval() => CheckIntervalHours = Math.Min(168, CheckIntervalHours + 1);

    [RelayCommand]
    private void DecreaseInterval() => CheckIntervalHours = Math.Max(1, CheckIntervalHours - 1);

    [RelayCommand(CanExecute = nameof(CanShowChangelog))]
    private async Task ShowChangelogAsync(CancellationToken cancellationToken)
    {
        if (IsLoadingChangelog)
        {
            return;
        }

        IsLoadingChangelog = true;

        try
        {
            var entries = await Coordinator.LoadChangelogAsync(cancellationToken).ConfigureAwait(true);
            var slug = UpdateRepository.Resolve(RepositoryOverride, DefaultRepository);
            var releasesUrl = $"https://github.com/{slug}/releases";
            var dialog = UpdateDetailsDialogViewModel.ForHistory(entries, releasesUrl);

            if (await _dialogService.ShowAsync(dialog).ConfigureAwait(true)
                && dialog.ChosenAction == UpdateDetailsAction.OpenReleasePage)
            {
                _launcher.Open(releasesUrl);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (UpdateException exception)
        {
            _dialogService.Error("История изменений", exception.Message);
        }
        catch (Exception)
        {
            _dialogService.Error("История изменений", "Не удалось загрузить историю изменений.");
        }
        finally
        {
            IsLoadingChangelog = false;
        }
    }

    private bool CanShowChangelog() => !IsLoadingChangelog;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallUpdateAsync()
    {
        var candidate = Candidate;
        var pending = Pending;

        if (candidate is null && pending is null)
        {
            return;
        }

        var versionText = pending?.Version ?? candidate!.Version.ToString();
        var isPrepared = pending is not null
            && (candidate is null || string.Equals(pending.Version, candidate.Version.ToString(), StringComparison.Ordinal));

        if (candidate is not null)
        {
            var dialog = UpdateDetailsDialogViewModel.ForCandidate(
                candidate,
                Environment.CurrentVersion,
                UpdateDeliveryMode.SingleFileSwap,
                isPrepared);

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
        }
        else if (!_dialogService.Confirm("Установка обновления", $"Установить загруженную версию {versionText} сейчас?\n\nПриложение перезапустится автоматически."))
        {
            return;
        }

        if (isPrepared)
        {
            _isClosePending = true;
            UpdateBusyState();
            _dialogService.Info("Установка обновления", "Обновление загружено. Приложение закроется и установит новую версию.");
            _mainWindow.RequestClose();
            return;
        }

        CancellationToken token;

        lock (_installLock)
        {
            if (_disposed || _closeCts.IsCancellationRequested)
            {
                return;
            }

            token = _closeCts.Token;
        }

        _isInstalling = true;
        UpdateBusyState();
        DownloadProgress = 0;

        Task<bool>? downloadTask = null;

        try
        {
            downloadTask = Coordinator.DownloadAsync(token);

            lock (_installLock)
            {
                _activeInstallTask = downloadTask;
            }

            var succeeded = await downloadTask;

            if (token.IsCancellationRequested || IsDisposed())
            {
                _isInstalling = false;
                UpdateBusyState();
                RefreshStatus();
                return;
            }

            if (!succeeded)
            {
                var error = Coordinator.Error ?? "Не удалось загрузить обновление.";
                _isInstalling = false;
                UpdateBusyState();
                (StatusText, StatusSeverity) = ($"Ошибка загрузки: {error}", StatusSeverity.Error);
                _dialogService.Error("Ошибка обновления", $"Не удалось установить обновление: {error}");
                return;
            }

            _isInstalling = false;
            _isClosePending = true;
            UpdateBusyState();

            _dialogService.Info("Установка обновления", "Обновление загружено. Приложение закроется и установит новую версию.");

            _mainWindow.RequestClose();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || IsDisposed())
        {
            _isInstalling = false;
            UpdateBusyState();
            RefreshStatus();
        }
        catch (Exception exception)
        {
            _isInstalling = false;
            UpdateBusyState();
            (StatusText, StatusSeverity) = ($"Ошибка загрузки: {exception.Message}", StatusSeverity.Error);
            _dialogService.Error("Ошибка обновления", $"Не удалось установить обновление: {exception.Message}");
        }
        finally
        {
            var disposeCts = false;

            lock (_installLock)
            {
                if (ReferenceEquals(_activeInstallTask, downloadTask))
                {
                    _activeInstallTask = null;
                }

                disposeCts = _disposed && _activeInstallTask is null;
            }

            if (disposeCts)
            {
                _closeCts.Dispose();
            }
        }
    }

    private bool CanInstall() => !IsBusy && Coordinator.IsUpdatable && (Candidate is not null || Pending is not null);

    private bool IsDisposed()
    {
        lock (_installLock)
        {
            return _disposed;
        }
    }

    private void UpdateBusyState()
    {
        IsBusy = _isInstalling || _isClosePending || IsChecking || IsDownloading;
    }

    private void RefreshRepositoryHint()
    {
        var text = RepositoryOverride.Trim();

        if (text.Length == 0)
        {
            RepositoryHintText = $"Пусто – используется {DefaultRepository} (из сборки).";
            RepositoryHintSeverity = StatusSeverity.None;
        }
        else if (UpdateRepository.IsValidSlug(text))
        {
            RepositoryHintText = $"✓ {text}";
            RepositoryHintSeverity = StatusSeverity.Success;
        }
        else
        {
            RepositoryHintText = "Неверный формат. Ожидается owner/repo.";
            RepositoryHintSeverity = StatusSeverity.Error;
        }
    }

    private void RefreshStatus()
    {
        if (Environment.Kind == UpdateKind.None)
        {
            (StatusText, StatusSeverity) = ("Автообновление недоступно для этой сборки (запуск из среды разработки).", StatusSeverity.None);
            return;
        }

        if (!Coordinator.IsUpdatable)
        {
            var message = AllowFrameworkDependentUpdate
                ? "Сохраните настройки, чтобы разрешить автообновление для сборки, требующей .NET."
                : "Автообновление выключено для сборки, требующей .NET. Включите опцию ниже и сохраните настройки.";

            (StatusText, StatusSeverity) = (message, StatusSeverity.None);
            return;
        }

        if (IsChecking)
        {
            (StatusText, StatusSeverity) = ("Проверка обновлений…", StatusSeverity.Info);
            return;
        }

        if (IsDownloading)
        {
            (StatusText, StatusSeverity) = ($"Загрузка обновления… {Progress}%", StatusSeverity.Info);
            return;
        }

        if (Stage == UpdateStage.Failed)
        {
            (StatusText, StatusSeverity) = ($"Ошибка: {Error ?? "Не удалось выполнить операцию обновления."}", StatusSeverity.Error);
            return;
        }

        if (Pending is { } pending
            && (Candidate is null || string.Equals(pending.Version, Candidate.Version.ToString(), StringComparison.Ordinal)))
        {
            (StatusText, StatusSeverity) = ($"Обновление {pending.Version} загружено и установится при выходе из приложения.", StatusSeverity.Success);
            InstallUpdateCommand.NotifyCanExecuteChanged();
        }
        else if (Candidate is { } candidate)
        {
            (StatusText, StatusSeverity) = ($"Доступно обновление: {candidate.Version}", StatusSeverity.Success);
            InstallUpdateCommand.NotifyCanExecuteChanged();
        }
        else
        {
            (StatusText, StatusSeverity) = (FormatLastCheck(Settings.LastCheckUtc), StatusSeverity.None);
        }
    }

    private static string FormatLastCheck(DateTimeOffset? lastCheckUtc)
    {
        if (lastCheckUtc is { } lastCheck)
        {
            var local = lastCheck.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            return $"Последняя проверка: {local}. Обновлений не найдено.";
        }

        return "Проверка обновлений ещё не выполнялась.";
    }
}
