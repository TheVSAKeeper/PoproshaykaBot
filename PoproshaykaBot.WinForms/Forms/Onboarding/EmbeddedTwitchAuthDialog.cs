using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using System.Diagnostics;

namespace PoproshaykaBot.WinForms.Forms.Onboarding;

public partial class EmbeddedTwitchAuthDialog : Form
{
    private const string WebView2RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private readonly ITwitchOAuthService _oauthService;
    private readonly ILogger<EmbeddedTwitchAuthDialog> _logger;

    private CancellationTokenSource? _flowCts;
    private bool _initialized;
    private bool _statusSubscribed;
    private bool _flowCompleted;
    private bool _webViewReady;

    public EmbeddedTwitchAuthDialog(
        ITwitchOAuthService oauthService,
        ILogger<EmbeddedTwitchAuthDialog> logger)
    {
        _oauthService = oauthService;
        _logger = logger;

        InitializeComponent();
        Disposed += OnDialogDisposed;
    }

    public TwitchOAuthRole Role { get; set; } = TwitchOAuthRole.Bot;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string[]? Scopes { get; set; }

    public string? RedirectUri { get; set; }

    public bool CheckBroadcasterChannel { get; set; } = true;

    public OAuthFlowResult? AuthResult { get; private set; }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized)
        {
            return;
        }

        if (this.IsInDesignMode())
        {
            return;
        }

        _initialized = true;

        Text = Role == TwitchOAuthRole.Broadcaster
            ? "Авторизация стримера в Twitch"
            : "Авторизация бота в Twitch";

        _statusLabel.Text = "Готовим встроенный браузер...";
        _statusLabel.ForeColor = Color.Gray;

        _oauthService.StatusChanged += OnOAuthStatusChanged;
        _statusSubscribed = true;

        _ = InitializeAndStartAsync();
    }

    private void OnOAuthStatusChanged(TwitchOAuthRole role, string message)
    {
        if (role != Role || IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() => UpdateStatus(message, Color.Black));
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (IsDisposed)
        {
        }
    }

    private void OnCancelButtonClicked(object? sender, EventArgs e)
    {
        if (_flowCts is { IsCancellationRequested: false } cts)
        {
            _ = CancelFlowAsync(cts);
            return;
        }

        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (sender is CoreWebView2 core)
        {
            e.NewWindow = core;
            e.Handled = true;
        }
    }

    private void OnFallbackLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = WebView2RuntimeDownloadUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось открыть ссылку на WebView2 Runtime");
        }
    }

    private void OnDialogDisposed(object? sender, EventArgs e)
    {
        if (_statusSubscribed)
        {
            _oauthService.StatusChanged -= OnOAuthStatusChanged;
            _statusSubscribed = false;
        }

        if (_flowCts is { } cts)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static async Task CancelFlowAsync(CancellationTokenSource cts)
    {
        try
        {
            await cts.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private static string SafeMessage(Exception exception)
    {
        return exception switch
        {
            OperationCanceledException => "Авторизация отменена",
            TimeoutException => "Истекло время ожидания авторизации",
            HttpRequestException => "Ошибка сети при запросе токена",
            ArgumentException => "Не заполнены Client ID или Client Secret",
            InvalidOperationException invalid => invalid.Message,
            _ => "Ошибка авторизации",
        };
    }

    private async Task InitializeAndStartAsync()
    {
        try
        {
            var udf = WebView2UserDataFolders.Resolve(Role);
            Directory.CreateDirectory(udf);

            var environment = await CoreWebView2Environment.CreateAsync(null, udf);
            await _webView.EnsureCoreWebView2Async(environment);

            if (IsDisposed || Disposing)
            {
                return;
            }

            _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            _webViewReady = true;

            await StartAuthorizationFlowAsync();
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            _logger.LogError(exception, "WebView2 Runtime отсутствует, встроенный логин невозможен");
            ShowFallback("Требуется Microsoft Edge WebView2 Runtime, чтобы использовать встроенный логин.", true);
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Авторизация отменена", Color.Gray);
            CloseWith(DialogResult.Cancel);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Не удалось инициализировать встроенный браузер для OAuth");
            ShowFallback("Не удалось открыть встроенный браузер. Используйте кнопку «Открыть в браузере».", false);
        }
    }

    private async Task StartAuthorizationFlowAsync()
    {
        if (_flowCompleted)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _flowCts = cts;

        try
        {
            var result = await _oauthService.StartOAuthFlowToDraftAsync(Role,
                ClientId,
                ClientSecret,
                Scopes,
                RedirectUri,
                authUrl => MarshalNavigate(authUrl),
                CheckBroadcasterChannel,
                cts.Token);

            if (IsDisposed || Disposing)
            {
                return;
            }

            AuthResult = result;
            _flowCompleted = true;
            CloseWith(DialogResult.OK);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            UpdateStatus("Авторизация отменена", Color.Gray);
            _flowCompleted = true;
            CloseWith(DialogResult.Cancel);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "OAuth-поток упал во встроенном диалоге для роли {Role}", Role);
            UpdateStatus(SafeMessage(exception), Color.DarkRed);
            _cancelButton.Text = "Закрыть";
        }
        finally
        {
            if (ReferenceEquals(_flowCts, cts))
            {
                _flowCts = null;
            }

            cts.Dispose();
        }
    }

    private void MarshalNavigate(string authUrl)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed || !_webViewReady)
                {
                    return;
                }

                _webView.CoreWebView2?.Navigate(authUrl);
            });
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (IsDisposed)
        {
        }
    }

    private void UpdateStatus(string text, Color color)
    {
        if (IsDisposed)
        {
            return;
        }

        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    private void ShowFallback(string message, bool offerRuntimeLink)
    {
        if (IsDisposed)
        {
            return;
        }

        UpdateStatus(message, Color.DarkRed);
        _webView.Visible = false;
        _fallbackPanel.Visible = true;
        _fallbackLabel.Text = message;
        _fallbackLink.Visible = offerRuntimeLink;
    }

    private void CloseWith(DialogResult result)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (IsDisposed)
                {
                    return;
                }

                DialogResult = result;
                Close();
            });
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (IsDisposed)
        {
        }
    }
}
