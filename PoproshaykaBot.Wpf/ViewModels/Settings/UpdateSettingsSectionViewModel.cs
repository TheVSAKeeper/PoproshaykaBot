using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KeepShell.Services.Platform;
using KeepShell.Updates;
using KeepShell.Updates.Wpf;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Wpf.Infrastructure;
using System.ComponentModel;
using System.Globalization;
using CoreUpdateApplyMode = PoproshaykaBot.Core.Settings.Update.UpdateApplyMode;
using CoreUpdateKind = PoproshaykaBot.Core.Update.UpdateKind;
using CoreUpdateSettings = PoproshaykaBot.Core.Settings.Update.UpdateSettings;
using ICoreUpdateEnvironment = PoproshaykaBot.Core.Update.IUpdateEnvironment;
using KeepUpdateApplyMode = KeepShell.Updates.UpdateApplyMode;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class UpdateSettingsSectionViewModel : UpdateSettingsSectionViewModelBase
{
    private readonly ICoreUpdateEnvironment _coreEnvironment;
    private readonly UpdateStore _store;
    private readonly IDialogService _dialogService;
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
    [NotifyPropertyChangedFor(nameof(CanCheckNow))]
    private bool _isBusy;

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
        ICoreUpdateEnvironment environment,
        UpdateStore store,
        IDialogService dialogService,
        IUiDispatcher uiDispatcher,
        IMainWindowController mainWindow)
        : base(updateSettings, coordinator, options, environment, uiDispatcher)
    {
        _coreEnvironment = environment;
        _store = store;
        _dialogService = dialogService;
        _mainWindow = mainWindow;

        _autoInstall = ApplyMode == KeepUpdateApplyMode.Automatic;
        CurrentVersionText = $"Текущая версия: {_coreEnvironment.CurrentVersion} (.NET {System.Environment.Version.Major})";
        RefreshRepositoryHint();
        RefreshStatus();
    }

    protected override bool AutoSave => false;

    public bool CanCheckNow => !IsBusy && IsUpdatableNow();

    public void LoadSettings(CoreUpdateSettings settings)
    {
        AutomaticCheckEnabled = settings.AutoCheckEnabled;
        CheckIntervalHours = Math.Clamp(settings.CheckIntervalHours, 1, 168);
        AutoInstall = settings.ApplyMode == CoreUpdateApplyMode.SilentOnExit;
        AllowFrameworkDependentUpdate = settings.AllowFrameworkDependentUpdate;
        RepositoryOverride = settings.RepositoryOverride ?? string.Empty;
        RefreshRepositoryHint();
        RefreshStatus();
    }

    public void SaveSettings(CoreUpdateSettings settings)
    {
        settings.AutoCheckEnabled = AutomaticCheckEnabled;
        settings.CheckIntervalHours = Math.Clamp((int)Math.Round(CheckIntervalHours), 1, 168);
        settings.ApplyMode = AutoInstall ? CoreUpdateApplyMode.SilentOnExit : CoreUpdateApplyMode.NotifyAndConfirm;
        settings.AllowFrameworkDependentUpdate = AllowFrameworkDependentUpdate;
        var slug = RepositoryOverride.Trim();
        settings.RepositoryOverride = slug.Length == 0 ? null : slug;
    }

    public Task OnSavedAsync(CoreUpdateApplyMode previousMode)
    {
        if (previousMode == CoreUpdateApplyMode.SilentOnExit && !AutoInstall)
        {
            Coordinator.DiscardPending();
        }

        return Task.CompletedTask;
    }

    public new void Dispose()
    {
        base.Dispose();

        bool canDisposeCts;

        lock (_installLock)
        {
            if (_disposed)
            {
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
        ApplyMode = value ? KeepUpdateApplyMode.Automatic : KeepUpdateApplyMode.Notify;
    }

    [RelayCommand]
    private void IncreaseInterval() => CheckIntervalHours = Math.Min(168, CheckIntervalHours + 1);

    [RelayCommand]
    private void DecreaseInterval() => CheckIntervalHours = Math.Max(1, CheckIntervalHours - 1);

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

        var question = isPrepared
            ? $"Установить загруженную версию {versionText} сейчас?"
            : $"Загрузить и установить версию {versionText}?";

        if (!_dialogService.Confirm("Установка обновления", question + "\n\nПриложение перезапустится автоматически."))
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

    private bool CanInstall() => !IsBusy && IsUpdatableNow() && (Candidate is not null || Pending is not null);

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

    private bool IsUpdatableNow() => _coreEnvironment.Kind switch
    {
        CoreUpdateKind.Portable => true,
        CoreUpdateKind.FrameworkDependent => AllowFrameworkDependentUpdate,
        _ => false,
    };

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
        if (_coreEnvironment.Kind == CoreUpdateKind.Unsupported)
        {
            (StatusText, StatusSeverity) = ("Автообновление недоступно для этой сборки (запуск из среды разработки).", StatusSeverity.None);
            return;
        }

        if (!IsUpdatableNow())
        {
            (StatusText, StatusSeverity) = ("Автообновление выключено для сборки, требующей .NET. Включите опцию ниже.", StatusSeverity.None);
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
            (StatusText, StatusSeverity) = (FormatLastCheck(_store.Load()), StatusSeverity.None);
        }
    }

    private static string FormatLastCheck(CoreUpdateSettings settings)
    {
        if (settings.LastCheckUtc is { } lastCheck)
        {
            var local = lastCheck.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
            return $"Последняя проверка: {local}. Обновлений не найдено.";
        }

        return "Проверка обновлений ещё не выполнялась.";
    }
}
