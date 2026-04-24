using PoproshaykaBot.WinForms.Infrastructure;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Streaming;

namespace PoproshaykaBot.WinForms.Broadcast;

public sealed partial class BroadcastInfoWidget : UserControl
{
    private readonly List<IDisposable> _subs = [];
    private bool _initialized;

    public BroadcastInfoWidget()
    {
        InitializeComponent();
    }

    [Inject]
    public SettingsManager Settings { get; init; } = null!;

    [Inject]
    public IStreamStatus Stream { get; init; } = null!;

    [Inject]
    public BroadcastScheduler Scheduler { get; init; } = null!;

    [Inject]
    public IChannelProvider ChannelProvider { get; init; } = null!;

    [Inject]
    public IEventBus Bus { get; init; } = null!;

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

        _subs.Add(Bus.SubscribeOnUi<BroadcastSchedulerStateChanged>(this, _ => UpdateState()));
        _subs.Add(Bus.SubscribeOnUi<StreamWentOnline>(this, _ => UpdateState()));
        _subs.Add(Bus.SubscribeOnUi<StreamWentOffline>(this, _ => UpdateState()));
        _subs.Add(Bus.SubscribeOnUi<BotLifecyclePhaseChanged>(this, _ => UpdateState()));
        _subs.DisposeOnClose(this);

        UpdateState();
    }

    private void OnModeToggleClick(object sender, EventArgs e)
    {
        var settings = Settings.Current;
        var willEnableAuto = !settings.Twitch.AutoBroadcast.AutoBroadcastEnabled;

        if (willEnableAuto && Scheduler.IsActive && Stream.CurrentStatus != StreamStatus.Online)
        {
            var result = MessageBox.Show("""
                                         При переключении в автоматический режим активная рассылка будет остановлена, так как стрим сейчас оффлайн.

                                         Продолжить?
                                         """,
                "Переключение режима",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }
        }

        settings.Twitch.AutoBroadcast.AutoBroadcastEnabled = willEnableAuto;
        Settings.SaveSettings(settings);

        if (willEnableAuto)
        {
            if (Stream.CurrentStatus == StreamStatus.Online)
            {
                if (!Scheduler.IsActive && !string.IsNullOrEmpty(ChannelProvider.Channel))
                {
                    Scheduler.Start(ChannelProvider.Channel);
                }
            }
            else
            {
                Scheduler.Stop();
            }
        }

        UpdateState();
    }

    private void OnToggleButtonClick(object sender, EventArgs e)
    {
        if (Scheduler.IsActive)
        {
            Scheduler.Stop();
        }
        else
        {
            if (Settings.Current.Twitch.AutoBroadcast.AutoBroadcastEnabled)
            {
                MessageBox.Show("""
                                В автоматическом режиме рассылка запускается сама при начале стрима.

                                Для ручного запуска переключитесь в ручной режим.
                                """,
                    "Автоматический режим",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return;
            }

            if (!string.IsNullOrEmpty(ChannelProvider.Channel))
            {
                Scheduler.Start(ChannelProvider.Channel);
            }
        }

        UpdateState();
    }

    private async void OnSendNowButtonClick(object sender, EventArgs e)
    {
        _sendNowButton.Enabled = false;
        await Scheduler.ManualSendAsync();
        _sendNowButton.Enabled = true;
    }

    private void UpdateState()
    {
        var isActive = Scheduler.IsActive;
        var isAuto = Settings.Current.Twitch.AutoBroadcast.AutoBroadcastEnabled;
        var streamOnline = Stream.CurrentStatus == StreamStatus.Online;

        _statusLabel.Text = isActive ? "✅ Активна" : "❌ Неактивна";
        _statusLabel.ForeColor = isActive ? Color.Green : Color.Red;

        if (isAuto && !isActive)
        {
            _statusLabel.Text = streamOnline ? "❌ Неактивна (Авто)" : "⏳ Ожидание стрима";
            _statusLabel.ForeColor = Color.Orange;
        }

        _modeLabel.Text = $"Режим: {(isAuto ? "Авто" : "Ручной")}";
        _modeToggleButton.Text = isAuto ? "В ручной" : "В авто";

        _sentCountLabel.Text = $"Отправлено: {Scheduler.SentMessagesCount}";

        var nextTime = Scheduler.NextBroadcastTime;
        _nextTimeLabel.Text = nextTime.HasValue
            ? $"Следующая: {nextTime.Value:HH:mm:ss}"
            : "Следующая: —";

        _toggleButton.Enabled = true;
        _modeToggleButton.Enabled = true;
        _toggleButton.Text = isActive ? "Стоп" : "Старт";
        _sendNowButton.Enabled = isActive;

        if (isAuto && !isActive)
        {
            _toggleButton.Enabled = false;
            _toolTip.SetToolTip(_toggleButton, """
                                               В автоматическом режиме рассылка запускается сама при начале стрима.
                                               Переключитесь в ручной режим для ручного запуска.
                                               """);
        }
        else
        {
            _toolTip.SetToolTip(_toggleButton, isActive ? "Остановить рассылку" : "Запустить рассылку");
        }

        _toolTip.SetToolTip(_modeToggleButton,
            isAuto
                ? "Перейти в ручное управление рассылкой"
                : "Включить автоматический запуск рассылки при начале стрима");

        _toolTip.SetToolTip(_sendNowButton,
            isActive
                ? "Отправить сообщение из рассылки прямо сейчас"
                : "Для отправки рассылка должна быть активна");
    }
}
