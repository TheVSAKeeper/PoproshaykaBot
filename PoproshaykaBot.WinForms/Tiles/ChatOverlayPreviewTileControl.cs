using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed partial class ChatOverlayPreviewTileControl : UserControl
{
    private bool _initialized;

    public ChatOverlayPreviewTileControl()
    {
        InitializeComponent();
    }

    [Inject]
    public SettingsManager Settings { get; internal init; } = null!;

    [Inject]
    public IEventBus Bus { get; internal init; } = null!;

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
        InitializeWebViewAsync();
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            await _webView.EnsureCoreWebView2Async(null);
            UpdateOverlayUrl();
        }
        catch (Exception ex)
        {
            _webView.Visible = false;
            _fallbackLabel.Visible = true;
            _fallbackLabel.Text = "Не удалось открыть OBS-превью чата. Подробности — в логах.";
            _fallbackLabel.BringToFront();

            _ = Bus.PublishAsync(new BotLogEntry(BotLogLevel.Error, "Ui", $"Ошибка инициализации WebView2: {ex.Message}"));
        }
    }

    private void UpdateOverlayUrl()
    {
        if (_webView.CoreWebView2 == null)
        {
            return;
        }

        var port = Settings.Current.Twitch.HttpServerPort;
        var url = $"http://localhost:{port}/chat?preview=true";

        if (_webView.Source?.ToString() != url)
        {
            _webView.CoreWebView2.Navigate(url);
        }
    }
}
