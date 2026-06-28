using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class HttpServerSectionViewModel : ObservableObject, IDisposable
{
    private readonly KestrelHttpServer _server;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ObsUrl))]
    private int _port;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerStatusText))]
    [NotifyPropertyChangedFor(nameof(ServerStatusSeverity))]
    private bool _isRunning;

    public HttpServerSectionViewModel(KestrelHttpServer server, IDialogService dialogService)
    {
        _server = server;
        _dialogService = dialogService;
    }

    public string ObsUrl => $"http://localhost:{Port}/chat";

    public string ServerStatusText => IsRunning ? "Запущен" : "Готов к запуску";

    public StatusSeverity ServerStatusSeverity => IsRunning ? StatusSeverity.Success : StatusSeverity.Warning;

    public void LoadSettings(TwitchSettings settings)
    {
        Port = settings.HttpServerPort;
        IsRunning = _server.IsRunning;
    }

    [RelayCommand]
    private void CopyUrl()
    {
        try
        {
            Clipboard.SetText(ObsUrl);
            _dialogService.Info("Информация", "URL скопирован в буфер обмена!");
        }
        catch (Exception ex)
        {
            _dialogService.Error("Ошибка", $"Ошибка копирования URL: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RestartServerAsync()
    {
        try
        {
            if (_server.IsRunning)
            {
                await _server.StopAsync();
            }

            await _server.StartAsync();
            IsRunning = _server.IsRunning;

            _dialogService.Info("Информация", "HTTP сервер перезапущен.");
        }
        catch (Exception ex)
        {
            _dialogService.Error("Ошибка", $"Ошибка перезапуска сервера: {ex.Message}");
        }
    }

    public void Dispose()
    {
    }
}
