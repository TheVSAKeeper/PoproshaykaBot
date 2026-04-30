using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Logging;
using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Tiles;

public sealed partial class ChatOverlayPreviewTileControl : UserControl, IDashboardTileHeaderProvider
{
    private bool _initialized;
    private bool _previewMode = true;
    private ToolStripButton? _modeToggleButton;

    public ChatOverlayPreviewTileControl()
    {
        InitializeComponent();
    }

    [Inject]
    public SettingsManager Settings { get; internal init; } = null!;

    [Inject]
    public IEventBus Bus { get; internal init; } = null!;

    public IReadOnlyList<ToolStripItem> CreateHeaderItems()
    {
        _modeToggleButton = new()
        {
            AutoToolTip = false,
            CheckOnClick = true,
            Checked = _previewMode,
            DisplayStyle = ToolStripItemDisplayStyle.Text,
        };

        _modeToggleButton.CheckedChanged += OnModeToggleChanged;
        ApplyModeButtonAppearance();

        return [_modeToggleButton];
    }

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

    private void OnModeToggleChanged(object? sender, EventArgs e)
    {
        _previewMode = _modeToggleButton?.Checked ?? true;
        ApplyModeButtonAppearance();
        UpdateOverlayUrl();
    }

    private void ApplyModeButtonAppearance()
    {
        if (_modeToggleButton == null)
        {
            return;
        }

        if (_previewMode)
        {
            _modeToggleButton.Text = "👁 Превью";
            _modeToggleButton.ToolTipText = "Превью: сообщения не затухают. Нажми, чтобы переключиться на фактическое отображение.";
        }
        else
        {
            _modeToggleButton.Text = "📺 Как у зрителей";
            _modeToggleButton.ToolTipText = "Фактическое отображение: с затуханиями по настройкам OBS. Нажми, чтобы вернуться к превью.";
        }
    }

    private void UpdateOverlayUrl()
    {
        if (_webView.CoreWebView2 == null)
        {
            return;
        }

        var port = Settings.Current.Twitch.HttpServerPort;
        var url = _previewMode
            ? $"http://localhost:{port}/chat?preview=true"
            : $"http://localhost:{port}/chat";

        if (_webView.Source?.ToString() != url)
        {
            _webView.CoreWebView2.Navigate(url);
        }
    }
}
