namespace PoproshaykaBot.WinForms.Polls;

public partial class ActivePollCard : UserControl
{
    public ActivePollCard()
    {
        InitializeComponent();
    }

    public event EventHandler? StopRequested;

    public string? PollId { get; private set; }

    public PollSnapshotStatus CurrentStatus { get; private set; }

    public void Bind(PollSnapshot snapshot)
    {
        PollId = snapshot.PollId;
        CurrentStatus = snapshot.Status;

        _titleLabel.Text = string.IsNullOrWhiteSpace(snapshot.Title)
            ? "Без вопроса"
            : $"«{snapshot.Title}»";

        var leaderVotes = snapshot.Choices.Count > 0 ? snapshot.Choices.Max(c => c.Votes) : 0;

        if (_choicesPanel.RowCount != snapshot.Choices.Count)
        {
            RebuildChoicesPanel(snapshot.Choices.Count);
        }

        for (var i = 0; i < snapshot.Choices.Count; i++)
        {
            UpdateChoiceRow(i, snapshot.Choices[i], snapshot.TotalVotes, leaderVotes);
        }

        UpdateLive(snapshot);

        _stopButton.Visible = snapshot.Status == PollSnapshotStatus.Active;
        _stopButton.Enabled = snapshot.Status == PollSnapshotStatus.Active;

        BackColor = snapshot.Status switch
        {
            PollSnapshotStatus.Active => Color.FromArgb(255, 248, 230),
            PollSnapshotStatus.Completed => Color.FromArgb(240, 255, 240),
            PollSnapshotStatus.Invalid => Color.FromArgb(255, 240, 240),
            _ => SystemColors.Window,
        };
    }

    public void UpdateLive(PollSnapshot snapshot)
    {
        switch (snapshot.Status)
        {
            case PollSnapshotStatus.Active:
                {
                    var remaining = snapshot.EndsAtUtc - DateTime.UtcNow;

                    if (remaining < TimeSpan.Zero)
                    {
                        remaining = TimeSpan.Zero;
                    }

                    _footerLabel.Text =
                        $"🔴 идёт • ⏱ {(int)remaining.TotalMinutes}:{remaining.Seconds:D2} • {snapshot.TotalVotes} голос.";

                    _footerLabel.ForeColor = Color.Firebrick;
                    break;
                }

            case PollSnapshotStatus.Completed:
                _footerLabel.Text = snapshot.LeaderIsTie
                    ? $"✓ завершено (ничья) • {snapshot.TotalVotes} голос."
                    : snapshot.Leader != null
                        ? $"✓ завершено: «{snapshot.Leader.Title}» • {snapshot.TotalVotes} голос."
                        : $"✓ завершено • {snapshot.TotalVotes} голос.";

                _footerLabel.ForeColor = Color.SeaGreen;
                break;

            case PollSnapshotStatus.Terminated:
                _footerLabel.Text = $"■ прервано • {snapshot.TotalVotes} голос.";
                _footerLabel.ForeColor = Color.Gray;
                break;

            case PollSnapshotStatus.Archived:
                _footerLabel.Text = $"архив • {snapshot.TotalVotes} голос.";
                _footerLabel.ForeColor = Color.Gray;
                break;

            case PollSnapshotStatus.Moderated:
                _footerLabel.Text = "⚠ на модерации";
                _footerLabel.ForeColor = Color.DarkOrange;
                break;

            case PollSnapshotStatus.Invalid:
                _footerLabel.Text = "✗ некорректное состояние";
                _footerLabel.ForeColor = Color.Red;
                break;
        }
    }

    private void OnStopButtonClick(object? sender, EventArgs e)
    {
        StopRequested?.Invoke(this, e);
    }

    private void RebuildChoicesPanel(int count)
    {
        _choicesPanel.SuspendLayout();

        try
        {
            foreach (var control in _choicesPanel.Controls.Cast<Control>().ToList())
            {
                _choicesPanel.Controls.Remove(control);
                control.Dispose();
            }

            _choicesPanel.RowStyles.Clear();
            _choicesPanel.RowCount = Math.Max(1, count);

            var verticalMargin = LogicalToDeviceUnits(3);
            var rightGap = LogicalToDeviceUnits(6);

            for (var i = 0; i < count; i++)
            {
                _choicesPanel.RowStyles.Add(new(SizeType.AutoSize));

                var nameLabel = new Label
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    AutoEllipsis = true,
                    Margin = new(0, verticalMargin, rightGap, verticalMargin),
                    Name = $"_choiceName{i}",
                    TextAlign = ContentAlignment.MiddleLeft,
                };

                var bar = new ProgressBar
                {
                    Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Margin = new(0, verticalMargin, rightGap, verticalMargin),
                    Maximum = 100,
                    Name = $"_choiceBar{i}",
                    Style = ProgressBarStyle.Continuous,
                };

                var votesLabel = new Label
                {
                    Anchor = AnchorStyles.Right,
                    AutoSize = true,
                    Margin = new(0, verticalMargin, 0, verticalMargin),
                    Name = $"_choiceVotes{i}",
                    TextAlign = ContentAlignment.MiddleRight,
                };

                _choicesPanel.Controls.Add(nameLabel, 0, i);
                _choicesPanel.Controls.Add(bar, 1, i);
                _choicesPanel.Controls.Add(votesLabel, 2, i);
            }
        }
        finally
        {
            _choicesPanel.ResumeLayout();
        }
    }

    private void UpdateChoiceRow(int rowIndex, PollChoiceSnapshot choice, int totalVotes, int leaderVotes)
    {
        var nameLabel = (Label?)_choicesPanel.GetControlFromPosition(0, rowIndex);
        var bar = (ProgressBar?)_choicesPanel.GetControlFromPosition(1, rowIndex);
        var votesLabel = (Label?)_choicesPanel.GetControlFromPosition(2, rowIndex);

        if (nameLabel == null || bar == null || votesLabel == null)
        {
            return;
        }

        var percent = totalVotes > 0
            ? (int)Math.Round(choice.Votes * 100.0 / totalVotes)
            : 0;

        var isLeader = choice.Votes == leaderVotes && leaderVotes > 0;

        nameLabel.Text = choice.Title;
        nameLabel.Font = isLeader
            ? new(_choicesPanel.Font, FontStyle.Bold)
            : _choicesPanel.Font;

        bar.Value = Math.Clamp(percent, 0, 100);

        votesLabel.Text = $"{percent}% ({choice.Votes})";
        votesLabel.Font = isLeader
            ? new(_choicesPanel.Font, FontStyle.Bold)
            : _choicesPanel.Font;
    }
}
