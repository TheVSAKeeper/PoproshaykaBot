using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms;

public sealed partial class BroadcastInfoWidget : UserControl
{
    private Bot? _bot;
    private SettingsManager? _settingsManager;

    public BroadcastInfoWidget()
    {
        InitializeComponent();
    }

    public void Setup(SettingsManager settingsManager, Bot? bot = null)
    {
        _settingsManager = settingsManager;
        _bot = bot;
        UpdateState();
    }

    public void UpdateState()
    {
        if (InvokeRequired)
        {
            Invoke(UpdateState);
            return;
        }

        if (_bot == null)
        {
            _statusLabel.Text = "Бот не подключен";
            _statusLabel.ForeColor = Color.Gray;
            _sentCountLabel.Text = "Отправлено: 0";
            _nextTimeLabel.Text = "Следующая: —";
            _toggleButton.Enabled = false;
            _sendNowButton.Enabled = false;

            if (_settingsManager is not null)
            {
                var initialIsAuto = _settingsManager.Current.Twitch.AutoBroadcast.AutoBroadcastEnabled;
                _modeLabel.Text = $"Режим: {(initialIsAuto ? "Авто" : "Ручной")}";
                _modeToggleButton.Text = initialIsAuto ? "В ручной" : "В авто";
                _modeToggleButton.Enabled = true;
            }
            else
            {
                _modeToggleButton.Enabled = false;
            }

            return;
        }

        var isActive = _bot.IsBroadcastActive;
        _statusLabel.Text = isActive ? "✅ Активна" : "❌ Неактивна";
        _statusLabel.ForeColor = isActive ? Color.Green : Color.Red;

        var isAuto = _bot.IsAutoBroadcastEnabled;
        _modeLabel.Text = $"Режим: {(isAuto ? "Авто" : "Ручной")}";
        _modeToggleButton.Text = isAuto ? "В ручной" : "В авто";

        _sentCountLabel.Text = $"Отправлено: {_bot.BroadcastSentMessagesCount}";

        var nextTime = _bot.NextBroadcastTime;
        _nextTimeLabel.Text = nextTime.HasValue
            ? $"Следующая: {nextTime.Value:HH:mm:ss}"
            : "Следующая: —";

        _toggleButton.Enabled = true;
        _modeToggleButton.Enabled = true;
        _toggleButton.Text = isActive ? "Стоп" : "Старт";
        _sendNowButton.Enabled = isActive;
    }

    private void OnModeToggleClick(object sender, EventArgs e)
    {
        if (_bot != null)
        {
            _bot.IsAutoBroadcastEnabled = !_bot.IsAutoBroadcastEnabled;
        }
        else if (_settingsManager is not null)
        {
            var settings = _settingsManager.Current;
            settings.Twitch.AutoBroadcast.AutoBroadcastEnabled = !settings.Twitch.AutoBroadcast.AutoBroadcastEnabled;
            _settingsManager.SaveSettings(settings);
            UpdateState();
        }
    }

    private void OnToggleButtonClick(object sender, EventArgs e)
    {
        if (_bot == null)
        {
            return;
        }

        if (_bot.IsBroadcastActive)
        {
            _bot.StopBroadcast();
        }
        else
        {
            _bot.StartBroadcast();
        }

        UpdateState();
    }

    private async void OnSendNowButtonClick(object sender, EventArgs e)
    {
        if (_bot == null)
        {
            return;
        }

        _sendNowButton.Enabled = false;
        await _bot.ManualBroadcastSendAsync();
        _sendNowButton.Enabled = true;
    }
}
