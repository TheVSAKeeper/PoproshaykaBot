using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Update;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class UpdateBannerViewModel : ObservableObject, IDisposable
{
    private readonly IUpdateCoordinator _coordinator;
    private readonly IDialogService _dialogService;
    private readonly ILogger<UpdateBannerViewModel> _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IMainWindowController _mainWindow;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly Lock _installLock = new();
    private readonly CancellationTokenSource _closeCts = new();

    private Task? _activeInstallTask;
    private bool _disposed;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(SkipCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _downloadProgress;

    public UpdateBannerViewModel(
        IUpdateCoordinator coordinator,
        IEventBus eventBus,
        IDialogService dialogService,
        ILogger<UpdateBannerViewModel> logger,
        IUiDispatcher uiDispatcher,
        IMainWindowController mainWindow)
    {
        _coordinator = coordinator;
        _dialogService = dialogService;
        _logger = logger;
        _uiDispatcher = uiDispatcher;
        _mainWindow = mainWindow;

        _subscriptions.Add(eventBus.SubscribeOnUi<UpdateAvailable>(_ => Refresh()));
        _subscriptions.Add(eventBus.SubscribeOnUi<UpdatePrepared>(_ => Refresh()));
        _subscriptions.Add(eventBus.SubscribeOnUi<UpdateDiscarded>(_ => Refresh()));

        Refresh();
    }

    public void Dispose()
    {
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

        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
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

        if (active is not null)
        {
            await active.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
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

        if (!IsBusy)
        {
            return;
        }

        IsBusy = false;
        Refresh();
    }

    private void Refresh()
    {
        if (IsBusy)
        {
            return;
        }

        var candidate = _coordinator.LatestCandidate;

        if (candidate is null || !_coordinator.IsUpdatable)
        {
            IsVisible = false;
            return;
        }

        DownloadProgress = 0;
        Text = IsPrepared(candidate)
            ? $"Версия {candidate.Version} загружена и установится при выходе из приложения (установлена {_coordinator.CurrentVersion})."
            : $"Доступна новая версия {candidate.Version} (установлена {_coordinator.CurrentVersion}).";
        IsVisible = true;
    }

    private bool IsPrepared(UpdateCandidate candidate)
    {
        return string.Equals(_coordinator.PreparedVersion, candidate.Version.ToString(), StringComparison.Ordinal);
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task InstallAsync()
    {
        var candidate = _coordinator.LatestCandidate;

        if (candidate is null)
        {
            return;
        }

        var question = IsPrepared(candidate)
            ? $"Установить загруженную версию {candidate.Version} сейчас?"
            : $"Загрузить и установить версию {candidate.Version}?";

        if (!_dialogService.Confirm("Установка обновления", question + "\n\nПриложение перезапустится автоматически."))
        {
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

        IsBusy = true;
        DownloadProgress = 0;

        var progress = new UiProgressPoller<int>(_uiDispatcher, percent =>
        {
            Text = $"Загрузка обновления {candidate.Version}… {percent}%";
            DownloadProgress = percent;
        });

        Task? prepareTask = null;

        try
        {
            using (progress.StartScope())
            {
                prepareTask = _coordinator.PrepareAsync(candidate, progress.Progress, token);

                lock (_installLock)
                {
                    _activeInstallTask = prepareTask;
                }

                await prepareTask;
            }

            if (token.IsCancellationRequested)
            {
                IsBusy = false;
                Refresh();
                return;
            }

            _logger.UpdateDownloaded(candidate.Version);

            _mainWindow.RequestClose();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || IsDisposed())
        {
            IsBusy = false;
            Refresh();
        }
        catch (Exception exception)
        {
            _logger.UpdateDownloadFailed(exception);

            _dialogService.Error("Ошибка обновления", $"Не удалось установить обновление: {exception.Message}");

            IsBusy = false;
            Refresh();
        }
        finally
        {
            var disposeCts = false;

            lock (_installLock)
            {
                if (ReferenceEquals(_activeInstallTask, prepareTask))
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

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Skip()
    {
        var candidate = _coordinator.LatestCandidate;

        if (candidate is null)
        {
            return;
        }

        _coordinator.SkipVersion(candidate.Version.ToString());
        IsVisible = false;

        _logger.UpdateVersionSkipped(candidate.Version);
    }

    private bool CanAct()
    {
        return !IsBusy;
    }

    private bool IsDisposed()
    {
        lock (_installLock)
        {
            return _disposed;
        }
    }
}
