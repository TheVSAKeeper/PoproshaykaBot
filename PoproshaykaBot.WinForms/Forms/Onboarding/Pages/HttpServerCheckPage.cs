using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using System.Net.Sockets;

namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

public sealed partial class HttpServerCheckPage : UserControl, IOnboardingWizardPage
{
    private OnboardingContext? _context;
    private bool _checkInProgress;

    public HttpServerCheckPage()
    {
        InitializeComponent();
    }

    public event EventHandler? CanAdvanceChanged;

    [Inject]
    public SettingsManager SettingsManager { get; internal init; } = null!;

    [Inject]
    public KestrelHttpServer KestrelHttpServer { get; internal init; } = null!;

    [Inject]
    public ILogger<HttpServerCheckPage> Logger { get; internal init; } = null!;

    public string PageTitle => "Проверка HTTP сервера";

    public bool CanAdvance { get; private set; }

    public void OnEnter(OnboardingContext context)
    {
        _context = context;
        SetCanAdvance(false);
        _ = RunCheckAsync();
    }

    public Task<bool> OnLeavingAsync(OnboardingContext context)
    {
        return Task.FromResult(CanAdvance);
    }

    private async void OnRetryButtonClicked(object? sender, EventArgs e)
    {
        await RunCheckAsync();
    }

    private static async Task<bool> TryConnectAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync("127.0.0.1", port, cts.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private async Task RunCheckAsync()
    {
        if (_checkInProgress || _context == null)
        {
            return;
        }

        _checkInProgress = true;
        _retryButton.Visible = false;

        try
        {
            ShowStatus("Проверка...", Color.Blue, "");

            if (!RedirectUriPortResolver.TryResolve(_context.Settings.Twitch.RedirectUri, out var port))
            {
                ShowStatus("Некорректный Redirect URI", Color.Red, "Вернитесь на предыдущий шаг и исправьте URI.");
                _retryButton.Visible = true;
                return;
            }

            _context.Settings.Twitch.HttpServerPort = port;
            SettingsManager.SaveSettings(_context.Settings);
            (FindForm() as OnboardingWizardForm)?.NotifySettingsCommittedToDisk();

            ShowStatus($"Перезапуск HTTP сервера на порту {port}...", Color.Blue, "");

            try
            {
                if (KestrelHttpServer.IsRunning)
                {
                    await KestrelHttpServer.StopAsync();
                }

                await KestrelHttpServer.StartAsync();
            }
            catch (Exception exception)
            {
                Logger.LogError(exception, "Не удалось перезапустить HTTP сервер на порту {Port}", port);
                ShowStatus($"Не удалось запустить сервер на порту {port}",
                    Color.Red,
                    $"{exception.Message}\nПорт может быть занят другим приложением. Поменяйте Redirect URI или закройте конфликтующее приложение.");

                _retryButton.Visible = true;
                return;
            }

            ShowStatus($"Проверка соединения с 127.0.0.1:{port}...", Color.Blue, "");

            var listening = await TryConnectAsync(port);
            if (!listening)
            {
                ShowStatus($"Сервер не отвечает на порту {port}",
                    Color.Red,
                    "Сервер запущен, но соединение установить не удалось. Возможно, файервол блокирует loopback.");

                _retryButton.Visible = true;
                return;
            }

            ShowStatus($"HTTP сервер слушает порт {port}",
                Color.Green,
                "Можно переходить к авторизации.");

            SetCanAdvance(true);
        }
        finally
        {
            _checkInProgress = false;
        }
    }

    private void ShowStatus(string status, Color color, string details)
    {
        _statusLabel.Text = status;
        _statusLabel.ForeColor = color;
        _detailsLabel.Text = details;
    }

    private void SetCanAdvance(bool value)
    {
        if (CanAdvance == value)
        {
            return;
        }

        CanAdvance = value;
        CanAdvanceChanged?.Invoke(this, EventArgs.Empty);
    }
}
