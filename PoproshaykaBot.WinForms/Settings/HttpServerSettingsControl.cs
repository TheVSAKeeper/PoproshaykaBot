using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Server;

namespace PoproshaykaBot.WinForms.Settings;

public partial class HttpServerSettingsControl : UserControl
{
    private bool _initialized;
    private int _port;

    public HttpServerSettingsControl()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingChanged;

    [Inject]
    public KestrelHttpServer Server { get; internal init; } = null!;

    public void LoadSettings(TwitchSettings settings)
    {
        _port = settings.HttpServerPort;
        _httpServerPortValueLabel.Text = _port.ToString();

        UpdateServerStatus();
        UpdateObsUrl();
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

        UpdateServerStatus();
    }

    private void OnCopyUrlButtonClicked(object sender, EventArgs e)
    {
        try
        {
            var url = $"http://localhost:{_port}/chat";
            Clipboard.SetText(url);

            MessageBox.Show("URL скопирован в буфер обмена!", "Информация",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка копирования URL: {ex.Message}", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void OnRestartServerButtonClicked(object sender, EventArgs e)
    {
        try
        {
            if (Server.IsRunning)
            {
                await Server.StopAsync();
            }

            await Server.StartAsync();
            UpdateServerStatus();
            MessageBox.Show("HTTP сервер перезапущен.", "Информация",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка перезапуска сервера: {ex.Message}", "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateServerStatus()
    {
        if (Server.IsRunning)
        {
            _serverStatusLabel.Text = "Статус сервера: ● Запущен";
            _serverStatusLabel.ForeColor = Color.Green;
        }
        else
        {
            _serverStatusLabel.Text = "Статус сервера: ● Готов к запуску";
            _serverStatusLabel.ForeColor = Color.Orange;
        }
    }

    private void UpdateObsUrl()
    {
        var url = $"http://localhost:{_port}/chat";
        _obsUrlLabel.Text = $"URL для OBS: {url}";
    }
}
