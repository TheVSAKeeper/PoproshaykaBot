using PoproshaykaBot.WinForms.Models;
using System.Diagnostics;

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

        _lastUpdateLabel.Text = $"Обновлено: {DateTime.Now:HH:mm:ss}";

        switch (status)
        {
            case StreamStatus.Online:
                _statusIconLabel.Text = "🔴";
                _statusTextLabel.Text = "В ЭФИРЕ";
                _statusTextLabel.ForeColor = Color.Red;
                if (info != null)
                {
                    _titleLabel.Text = info.Title;
                    _gameLabel.Text = info.GameName;
                    _viewersLabel.Text = $"👥 {info.ViewerCount:N0}";

                    var duration = DateTime.UtcNow - info.StartedAt;
                    _uptimeLabel.Text = $"⏱️ {(int)duration.TotalHours}ч {duration.Minutes:00}м";

                    LoadThumbnail(info.ThumbnailUrl);
                    _openChannelButton.Visible = true;
                    _openChannelButton.Tag = info.UserLogin;
                    _openChannelButton.Click -= OnOpenChannelClick;
                    _openChannelButton.Click += OnOpenChannelClick;
                }

                break;

            case StreamStatus.Offline:
                _statusIconLabel.Text = "⚫";
                _statusTextLabel.Text = "ОФЛАЙН";
                _statusTextLabel.ForeColor = Color.Gray;
                _titleLabel.Text = "Стрим завершен";
                _gameLabel.Text = "—";
                _viewersLabel.Text = "👥 0";
                _uptimeLabel.Text = "⏱️ 0ч 00м";
                _thumbnailPictureBox.Image = null;
                _openChannelButton.Visible = false;
                break;

            case StreamStatus.Unknown:
            default:
                _statusIconLabel.Text = "⚪";
                _statusTextLabel.Text = "НЕИЗВЕСТНО";
                _statusTextLabel.ForeColor = Color.DarkGray;
                _titleLabel.Text = "Статус не определен";
                _gameLabel.Text = "—";
                _viewersLabel.Text = "👥 —";
                _uptimeLabel.Text = "⏱️ —";
                _thumbnailPictureBox.Image = null;
                _openChannelButton.Visible = false;
                break;
        }
    }

    private void OnOpenChannelClick(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: string login })
        {
            return;
        }

        var url = $"https://twitch.tv/{login}";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }

    private void LoadThumbnail(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        try
        {
            var resolvedUrl = url
                .Replace("{width}", _thumbnailPictureBox.Width.ToString())
                .Replace("{height}", _thumbnailPictureBox.Height.ToString());

            _thumbnailPictureBox.LoadAsync(resolvedUrl);
        }
        catch
        {
            _thumbnailPictureBox.Image = null;
        }
    }
}
