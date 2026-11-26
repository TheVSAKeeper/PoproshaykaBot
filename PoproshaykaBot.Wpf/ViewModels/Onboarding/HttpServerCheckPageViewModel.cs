using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Wpf.Bootstrap;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed partial class HttpServerCheckPageViewModel : OnboardingPageViewModelBase
{
    private const int DefaultPort = 8080;

    private readonly SettingsManager _settingsManager;
    private readonly KestrelHttpServer _kestrelHttpServer;
    private readonly ILogger<HttpServerCheckPageViewModel> _logger;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _statusSeverity = string.Empty;

    [ObservableProperty]
    private string _detailsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPortEditable))]
    private bool _isTwitchEnabled = true;

    [ObservableProperty]
    private string _portText = DefaultPort.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RetryCommand))]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isRetryVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPortError))]
    private string _portErrorText = string.Empty;

    private OnboardingContext? _context;
    private bool _suppressPortTextChanged;
    private int? _verifiedPort;
    private int _checkGeneration;

    internal Func<int, int, Task<bool>>? RestartServerAsyncOverride { get; init; }

    public HttpServerCheckPageViewModel(
        SettingsManager settingsManager,
        KestrelHttpServer kestrelHttpServer,
        ILogger<HttpServerCheckPageViewModel> logger)
    {
        _settingsManager = settingsManager;
        _kestrelHttpServer = kestrelHttpServer;
        _logger = logger;
    }

    public override string PageTitle => "Локальный HTTP-сервер";

    public bool IsPortEditable => !IsTwitchEnabled;

    public bool HasPortError => !string.IsNullOrEmpty(PortErrorText);

    public override void OnEnter(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _checkGeneration++;
        _verifiedPort = null;
        PortErrorText = string.Empty;
        IsTwitchEnabled = context.Settings.Twitch.Enabled;

        if (!IsTwitchEnabled)
        {
            var initialPort = context.Settings.Twitch.HttpServerPort > 0
                ? context.Settings.Twitch.HttpServerPort
                : DefaultPort;
            _suppressPortTextChanged = true;
            try
            {
                PortText = initialPort.ToString(CultureInfo.InvariantCulture);
            }
            finally
            {
                _suppressPortTextChanged = false;
            }
        }

        CanAdvance = false;
        _ = RunCheckAsync();
    }

    public override Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (IsTwitchEnabled)
        {
            var twitchValid = CanAdvance
                && RedirectUriPortResolver.TryResolve(context.Settings.Twitch.RedirectUri, out var twitchPort)
                && _verifiedPort == twitchPort;
            return Task.FromResult(twitchValid);
        }

        if (!TryParsePort(PortText, out var currentPort))
        {
            PortErrorText = "Укажите целое число от 1 до 65535.";
            ShowStatus("Некорректный номер порта", "Error", "Исправьте значение выше и нажмите «Проверить порт».");
            IsRetryVisible = false;
            CanAdvance = false;
            return Task.FromResult(false);
        }

        if (!CanAdvance || _verifiedPort != currentPort)
        {
            ShowStatus("Порт изменён – требуется проверка", "Info", "Нажмите «Проверить порт», чтобы проверить и запустить HTTP-сервер.");
            CanAdvance = false;
            return Task.FromResult(false);
        }

        context.Settings.Twitch.HttpServerPort = currentPort;
        return Task.FromResult(true);
    }

    partial void OnPortTextChanged(string value)
    {
        if (_suppressPortTextChanged || _context is null || !IsPortEditable)
        {
            return;
        }

        _checkGeneration++;
        _verifiedPort = null;
        CanAdvance = false;
        IsRetryVisible = false;

        if (!TryParsePort(value, out _))
        {
            PortErrorText = "Укажите целое число от 1 до 65535.";
            ShowStatus("Некорректный номер порта", "Error", "Исправьте значение выше и нажмите «Проверить порт».");
            return;
        }

        PortErrorText = string.Empty;
        ShowStatus("Порт изменён – требуется проверка", "Info", "Нажмите «Проверить порт», чтобы проверить и запустить HTTP-сервер.");
    }

    [RelayCommand(CanExecute = nameof(CanRetry))]
    private async Task RetryAsync()
    {
        await RunCheckAsync();
    }

    private bool CanRetry() => !IsChecking;

    private async Task RunCheckAsync()
    {
        if (IsChecking || _context is null)
        {
            return;
        }

        var generation = ++_checkGeneration;
        IsChecking = true;
        IsRetryVisible = false;
        CanAdvance = false;
        PortErrorText = string.Empty;

        try
        {
            ShowStatus("Проверка…", "Info", string.Empty);

            int checkedPort;
            if (IsTwitchEnabled)
            {
                if (!RedirectUriPortResolver.TryResolve(_context.Settings.Twitch.RedirectUri, out checkedPort))
                {
                    ShowStatus("Некорректный Redirect URI", "Error", "Вернитесь на шаг «Настройка Twitch» и исправьте URI.");
                    IsRetryVisible = true;
                    return;
                }
            }
            else if (!TryParsePort(PortText, out checkedPort))
            {
                PortErrorText = "Укажите целое число от 1 до 65535.";
                ShowStatus("Некорректный номер порта", "Error", "Исправьте значение выше и нажмите «Проверить порт».");
                IsRetryVisible = false;
                return;
            }

            var livePort = _settingsManager.Current.Twitch.HttpServerPort;

            if (_kestrelHttpServer.IsRunning && livePort == checkedPort)
            {
                if (IsStaleCheck(generation, checkedPort))
                {
                    return;
                }

                _context.Settings.Twitch.HttpServerPort = checkedPort;
                _verifiedPort = checkedPort;
                ShowStatus($"HTTP-сервер уже слушает порт {checkedPort}", "Success", "Можно переходить дальше.");
                CanAdvance = true;
                return;
            }

            var restartingMessage = _kestrelHttpServer.IsRunning
                ? $"Перезапуск HTTP-сервера на порту {checkedPort}…"
                : $"Запуск HTTP-сервера на порту {checkedPort}…";

            ShowStatus(restartingMessage, "Info", string.Empty);

            if (!_kestrelHttpServer.IsRunning && !TryBindPort(checkedPort))
            {
                _logger.OnboardingPortUnavailable(checkedPort);
                ShowStatus(
                    $"Порт {checkedPort} занят другим приложением",
                    "Error",
                    IsTwitchEnabled
                        ? "Закройте конфликтующее приложение или измените Redirect URI на предыдущем шаге."
                        : "Закройте конфликтующее приложение или укажите другой порт.");
                IsRetryVisible = true;
                return;
            }

            var started = await (RestartServerAsyncOverride is { } restartOverride
                ? restartOverride(checkedPort, livePort)
                : TryRestartServerAsync(checkedPort, livePort));

            if (IsStaleCheck(generation, checkedPort))
            {
                return;
            }

            if (!started)
            {
                ShowStatus(
                    $"Не удалось запустить HTTP-сервер на порту {checkedPort}",
                    "Error",
                    IsTwitchEnabled
                        ? "Закройте конфликтующее приложение или измените Redirect URI на предыдущем шаге."
                        : "Закройте конфликтующее приложение или укажите другой порт.");
                IsRetryVisible = true;
                return;
            }

            _context.Settings.Twitch.HttpServerPort = checkedPort;
            _verifiedPort = checkedPort;
            ShowStatus($"HTTP-сервер слушает порт {checkedPort}", "Success", "Можно переходить дальше.");
            CanAdvance = true;
        }
        finally
        {
            IsChecking = false;
        }
    }

    private bool IsStaleCheck(int generation, int checkedPort)
    {
        if (generation != _checkGeneration)
        {
            return true;
        }

        return !IsTwitchEnabled && (!TryParsePort(PortText, out var currentPort) || currentPort != checkedPort);
    }

    private async Task<bool> TryRestartServerAsync(int newPort, int previousLivePort)
    {
        try
        {
            if (_kestrelHttpServer.IsRunning)
            {
                await _kestrelHttpServer.StopAsync();
            }

            _settingsManager.UpdateCurrent(settings => settings.Twitch.HttpServerPort = newPort);

            await _kestrelHttpServer.StartAsync();

            _logger.OnboardingHttpServerRestarted(newPort);
            return true;
        }
        catch (Exception exception)
        {
            _logger.OnboardingHttpServerStartFailed(exception, newPort);

            _settingsManager.UpdateCurrent(settings => settings.Twitch.HttpServerPort = previousLivePort);

            try
            {
                if (_kestrelHttpServer.IsRunning)
                {
                    await _kestrelHttpServer.StopAsync();
                }

                await _kestrelHttpServer.StartAsync();
            }
            catch (Exception restoreException)
            {
                _logger.OnboardingHttpServerRestoreFailed(restoreException, previousLivePort);
            }

            return false;
        }
    }

    private void ShowStatus(string status, string severity, string details)
    {
        StatusText = status;
        StatusSeverity = severity;
        DetailsText = details;
    }

    private static bool TryParsePort(string? raw, out int port)
    {
        if (int.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
            && port is >= 1 and <= 65535)
        {
            return true;
        }

        port = 0;
        return false;
    }

    private static bool TryBindPort(int port)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);

        try
        {
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}
