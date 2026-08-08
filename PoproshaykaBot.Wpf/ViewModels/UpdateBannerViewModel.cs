using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Update;
using PoproshaykaBot.Core.Update;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed partial class UpdateBannerViewModel : ObservableObject, IDisposable
{
    private readonly IUpdateCoordinator _coordinator;
    private readonly IDialogService _dialogService;
    private readonly ILogger<UpdateBannerViewModel> _logger;
    private readonly List<IDisposable> _subscriptions = [];

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
        ILogger<UpdateBannerViewModel> logger)
    {
        _coordinator = coordinator;
        _dialogService = dialogService;
        _logger = logger;

        _subscriptions.Add(eventBus.SubscribeOnUi<UpdateAvailable>(_ => Refresh()));

        Refresh();
    }

    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void Refresh()
    {
        var candidate = _coordinator.LatestCandidate;

        if (candidate is null || !_coordinator.IsUpdatable)
        {
            IsVisible = false;
            return;
        }

        DownloadProgress = 0;
        Text = $"Доступна новая версия {candidate.Version} (установлена {_coordinator.CurrentVersion}).";
        IsVisible = true;
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task InstallAsync()
    {
        var candidate = _coordinator.LatestCandidate;

        if (candidate is null)
        {
            return;
        }

        if (!_dialogService.Confirm("Установка обновления",
                $"Загрузить и установить версию {candidate.Version}?\n\nПриложение перезапустится автоматически."))
        {
            return;
        }

        IsBusy = true;
        DownloadProgress = 0;

        var progress = new Progress<int>(percent =>
        {
            Text = $"Загрузка обновления {candidate.Version}… {percent}%";
            DownloadProgress = percent;
        });

        try
        {
            await _coordinator.PrepareAsync(candidate, progress, CancellationToken.None);

            _logger.UpdateDownloaded(candidate.Version.ToString());

            Application.Current.MainWindow?.Close();
        }
        catch (Exception exception)
        {
            _logger.UpdateDownloadFailed(exception);

            _dialogService.Error("Ошибка обновления", $"Не удалось установить обновление: {exception.Message}");

            IsBusy = false;
            Refresh();
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

        _logger.UpdateVersionSkipped(candidate.Version.ToString());
    }

    private bool CanAct()
    {
        return !IsBusy;
    }
}
