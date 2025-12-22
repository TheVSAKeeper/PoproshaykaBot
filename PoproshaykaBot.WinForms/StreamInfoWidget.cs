using PoproshaykaBot.WinForms.Models;

namespace PoproshaykaBot.WinForms;

public sealed partial class StreamInfoWidget : UserControl
{
    public StreamInfoWidget()
    {
        InitializeComponent();
    }

    public void UpdateStatus(StreamStatus status, StreamInfo? info)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateStatus(status, info));
            return;
        }

        switch (status)
        {
            case StreamStatus.Online:
                _statusIconLabel.Text = "🔴";
                _statusTextLabel.Text = "LIVE";
                _statusTextLabel.ForeColor = Color.Red;
                if (info != null)
                {
                    _titleLabel.Text = info.Title;
                    _gameLabel.Text = info.GameName;
                    _viewersLabel.Text = $"👥 {info.ViewerCount}";

                    var duration = DateTime.UtcNow - info.StartedAt;
                    _uptimeLabel.Text = $"⏱️ {(int)duration.TotalHours:0}ч {duration.Minutes:00}м";
                }

                break;

            case StreamStatus.Offline:
                _statusIconLabel.Text = "⚫";
                _statusTextLabel.Text = "OFFLINE";
                _statusTextLabel.ForeColor = Color.Gray;
                _titleLabel.Text = "Стрим офлайн";
                _gameLabel.Text = "—";
                _viewersLabel.Text = "👥 0";
                _uptimeLabel.Text = "⏱️ 0ч 00м";
                break;

            case StreamStatus.Unknown:
            default:
                _statusIconLabel.Text = "⚪";
                _statusTextLabel.Text = "UNKNOWN";
                _statusTextLabel.ForeColor = Color.DarkGray;
                _titleLabel.Text = "Статус неизвестен";
                _gameLabel.Text = "—";
                _viewersLabel.Text = "👥 —";
                _uptimeLabel.Text = "⏱️ —";
                break;
        }
    }
}
