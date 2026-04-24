using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Settings;
using System.Diagnostics;
using System.Globalization;

namespace PoproshaykaBot.WinForms.Chat;

public sealed partial class ChatDisplay : UserControl
{
    private const string WebView2RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private const double DefaultZoom = 0.75;

    private const string HideClutterScript = """
                                             (function () {
                                                 const selectors = [
                                                     '[data-a-target="consent-banner"]',
                                                     '.consent-banner',
                                                     '.tw-callout-message',
                                                     '[class*="channelLeaderboardHeader"]',
                                                     '[class*="channelLeaderboardBottomIconContainer"]',
                                                     '[class*="community-highlight"]',
                                                 ];
                                                 const wrapperClasses = ['tw-transition', 'tw-callout', 'consent-banner'];
                                                 const findWrapper = (el) => {
                                                     let node = el;
                                                     for (let i = 0; i < 15 && node; i++) {
                                                         if (node.classList && wrapperClasses.some((c) => node.classList.contains(c))) {
                                                             return node;
                                                         }
                                                         node = node.parentElement;
                                                     }
                                                     return el;
                                                 };
                                                 const hideAll = () => {
                                                     const consentAccept = document.querySelector('[data-a-target="consent-banner-accept"]');
                                                     if (consentAccept) {
                                                         consentAccept.click();
                                                     }
                                                     for (const selector of selectors) {
                                                         document.querySelectorAll(selector).forEach((el) => {
                                                             const wrapper = findWrapper(el);
                                                             if (wrapper.dataset.poproshaykaHidden !== '1') {
                                                                 wrapper.dataset.poproshaykaHidden = '1';
                                                                 wrapper.style.setProperty('display', 'none', 'important');
                                                             }
                                                         });
                                                     }
                                                 };
                                                 hideAll();
                                                 if (document.readyState === 'loading') {
                                                     document.addEventListener('DOMContentLoaded', hideAll, { once: true });
                                                 }
                                                 const startObserver = () => {
                                                     if (document.documentElement) {
                                                         new MutationObserver(hideAll).observe(document.documentElement, { childList: true, subtree: true });
                                                     } else {
                                                         setTimeout(startObserver, 0);
                                                     }
                                                 };
                                                 startObserver();
                                             })();
                                             """;

    private static readonly string ZoomFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PoproshaykaBot",
        "chat-zoom.txt");

    private bool _initialized;
    private bool _reloadAttempted;

    public ChatDisplay()
    {
        InitializeComponent();
    }

    [Inject]
    public SettingsManager Settings { get; internal init; } = null!;

    [Inject]
    public ILogger<ChatDisplay> Logger { get; internal init; } = null!;

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

        _ = InitializeWebViewAsync();
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Logger.LogError("WebView2 процесс упал: {Reason}", e.ProcessFailedKind);

        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (_reloadAttempted)
                {
                    return;
                }

                _reloadAttempted = true;
                _webView.Reload();
            });
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (IsDisposed)
        {
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _reloadAttempted = false;
            return;
        }

        Logger.LogWarning("Twitch-чат не загрузился: status={HttpStatusCode}, error={WebErrorStatus}", e.HttpStatusCode, e.WebErrorStatus);
    }

    private void OnZoomFactorChanged(object? sender, EventArgs e)
    {
        var zoom = _webView.ZoomFactor;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ZoomFilePath)!);
            File.WriteAllText(ZoomFilePath, zoom.ToString("F3", CultureInfo.InvariantCulture));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Не удалось сохранить масштаб чата");
        }
    }

    private void OnReloadClicked(object? sender, EventArgs e)
    {
        if (_webView.CoreWebView2 == null)
        {
            return;
        }

        _webView.Reload();
    }

    private void OnResetZoomClicked(object? sender, EventArgs e)
    {
        _webView.ZoomFactor = DefaultZoom;
    }

    private void OnOpenInBrowserClicked(object? sender, EventArgs e)
    {
        var url = _webView.Source?.ToString();

        if (string.IsNullOrEmpty(url))
        {
            var channel = Settings.Current.Twitch.Channel?.Trim();

            if (string.IsNullOrEmpty(channel))
            {
                return;
            }

            url = $"https://www.twitch.tv/popout/{Uri.EscapeDataString(channel)}/chat?popout=";
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Не удалось открыть ссылку в браузере");
        }
    }

    private async void OnResetSessionClicked(object? sender, EventArgs e)
    {
        if (_webView.CoreWebView2?.Profile == null)
        {
            return;
        }

        var result = MessageBox.Show(this,
            "Это разлогинит бота из Twitch-чата и очистит куки профиля. Продолжить?",
            "Сброс сессии Twitch",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            await _webView.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);
            _webView.Reload();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Не удалось очистить данные сессии WebView2");
        }
    }

    private void OnFallbackLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = WebView2RuntimeDownloadUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Не удалось открыть ссылку на WebView2 Runtime");
        }
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PoproshaykaBot",
                "WebView2");

            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await _webView.EnsureCoreWebView2Async(env);

            if (IsDisposed || Disposing)
            {
                return;
            }

            _webView.CoreWebView2.ProcessFailed += OnProcessFailed;
            _webView.NavigationCompleted += OnNavigationCompleted;
            _webView.ZoomFactorChanged += OnZoomFactorChanged;

            await _webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(HideClutterScript);

            _webView.ZoomFactor = LoadSavedZoom();

            NavigateToChannel();
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            Logger.LogError(ex, "WebView2 Runtime не установлен");
            ShowRuntimeMissingFallback();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Не удалось инициализировать WebView2 для Twitch-чата");
            ShowGenericFallback();
        }
    }

    private void NavigateToChannel()
    {
        if (_webView.CoreWebView2 == null)
        {
            return;
        }

        var channel = Settings.Current.Twitch.Channel?.Trim();

        if (string.IsNullOrEmpty(channel))
        {
            _webView.Visible = false;
            _fallbackPanel.Visible = true;
            _fallbackLabel.Text = "Укажите канал в настройках Twitch, чтобы открыть чат.";
            _fallbackLink.Visible = false;
            return;
        }

        _webView.Visible = true;
        _fallbackPanel.Visible = false;

        var url = $"https://www.twitch.tv/popout/{Uri.EscapeDataString(channel)}/chat?popout=";
        _webView.CoreWebView2.Navigate(url);
    }

    private void ShowRuntimeMissingFallback()
    {
        _webView.Visible = false;
        _toolStrip.Visible = false;
        _fallbackPanel.Visible = true;
        _fallbackLabel.Text = "Требуется Microsoft Edge WebView2 Runtime.\nУстановите его, чтобы увидеть чат Twitch.";
        _fallbackLink.Text = "Скачать WebView2 Runtime";
        _fallbackLink.Visible = true;
    }

    private void ShowGenericFallback()
    {
        _webView.Visible = false;
        _toolStrip.Visible = false;
        _fallbackPanel.Visible = true;
        _fallbackLabel.Text = "Не удалось открыть чат Twitch. Подробности — в логах.";
        _fallbackLink.Visible = false;
    }

    private double LoadSavedZoom()
    {
        try
        {
            if (!File.Exists(ZoomFilePath))
            {
                return DefaultZoom;
            }

            var text = File.ReadAllText(ZoomFilePath).Trim();

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom)
                && zoom is >= 0.25 and <= 5.0)
            {
                return zoom;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Не удалось прочитать сохранённый масштаб чата");
        }

        return DefaultZoom;
    }
}
