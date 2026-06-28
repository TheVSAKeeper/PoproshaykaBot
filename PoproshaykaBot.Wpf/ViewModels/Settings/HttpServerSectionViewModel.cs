using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using System.Windows;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class HttpServerSectionViewModel : ObservableObject, IDisposable
{
    private readonly KestrelHttpServer _server;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ObsUrl))]
    private int _port;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ServerStatusText))]
    [NotifyPropertyChangedFor(nameof(ServerStatusSeverity))]
    private bool _isRunning;

    public HttpServerSectionViewModel(KestrelHttpServer server)
    {
        _server = server;
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
            StyledMessageBox.Show("URL скопирован в буфер обмена!", "Информация",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StyledMessageBox.Show($"Ошибка копирования URL: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
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

            StyledMessageBox.Show("HTTP сервер перезапущен.", "Информация",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            StyledMessageBox.Show($"Ошибка перезапуска сервера: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public void Dispose()
    {
    }
}
