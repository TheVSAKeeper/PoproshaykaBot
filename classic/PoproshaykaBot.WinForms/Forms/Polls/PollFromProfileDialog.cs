using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using System.ComponentModel;

namespace PoproshaykaBot.WinForms.Forms.Polls;

public partial class PollFromProfileDialog : Form
{
    public PollFromProfileDialog()
    {
        InitializeComponent();
    }

    [Inject]
    public PollProfilesManager Manager { get; internal init; } = null!;

    [Inject]
    public IFormFactory Forms { get; internal init; } = null!;

    public PollProfile? SelectedProfile { get; private set; }

    public void LoadProfiles(IEnumerable<PollProfile> profiles)
    {
        ReloadList(profiles, null);
    }

    private void OnProfileSelectionChanged(object? sender, EventArgs e)
    {
        UpdateOkEnabled();
        UpdateDetails();
    }

    private void OnProfileDoubleClick(object? sender, EventArgs e)
    {
        if (_profilesListBox.SelectedItem is PollProfile profile)
        {
            SelectedProfile = profile;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void OnOkClicked(object? sender, EventArgs e)
    {
        SelectedProfile = _profilesListBox.SelectedItem as PollProfile;
    }

    private void OnProfileMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right)
        {
            return;
        }

        var index = _profilesListBox.IndexFromPoint(e.Location);

        if (index >= 0)
        {
            _profilesListBox.SelectedIndex = index;
        }
    }

    private void OnContextMenuOpening(object? sender, CancelEventArgs e)
    {
        if (_profilesListBox.SelectedItem is not PollProfile)
        {
            e.Cancel = true;
        }
    }

    private void OnEditMenuClicked(object? sender, EventArgs e)
    {
        if (_profilesListBox.SelectedItem is not PollProfile profile)
        {
            return;
        }

        var copy = Clone(profile);

        using var dialog = Forms.Create<PollProfileEditDialog>();
        dialog.LoadFrom(copy);

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result is null)
        {
            return;
        }

        if (dialog.ShouldStartPoll)
        {
            SelectedProfile = dialog.Result;
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        ReloadList(Manager.GetAll(), dialog.Result.Id);
    }

    private void OnDeleteMenuClicked(object? sender, EventArgs e)
    {
        if (_profilesListBox.SelectedItem is not PollProfile profile)
        {
            return;
        }

        var result = MessageBox.Show(this,
            $"Удалить профиль «{profile.Name}»?",
            "Удаление профиля",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        Manager.Remove(profile.Id);
        ReloadList(Manager.GetAll(), null);
    }

    private static PollProfile Clone(PollProfile source)
    {
        return new()
        {
            Id = source.Id,
            Name = source.Name,
            Title = source.Title,
            Choices = source.Choices.ToList(),
            DurationSeconds = source.DurationSeconds,
            ChannelPointsVotingEnabled = source.ChannelPointsVotingEnabled,
            ChannelPointsPerVote = source.ChannelPointsPerVote,
            AutoTrigger = new()
            {
                Event = source.AutoTrigger.Event,
                BroadcastProfileId = source.AutoTrigger.BroadcastProfileId,
                CooldownMinutes = source.AutoTrigger.CooldownMinutes,
            },
        };
    }

    private void ReloadList(IEnumerable<PollProfile> profiles, Guid? preserveId)
    {
        _profilesListBox.BeginUpdate();

        try
        {
            _profilesListBox.Items.Clear();
            _profilesListBox.DisplayMember = nameof(PollProfile.Name);

            foreach (var profile in profiles)
            {
                _profilesListBox.Items.Add(profile);
            }
        }
        finally
        {
            _profilesListBox.EndUpdate();
        }

        if (_profilesListBox.Items.Count == 0)
        {
            UpdateDetails();
            UpdateOkEnabled();
            return;
        }

        if (preserveId.HasValue)
        {
            for (var i = 0; i < _profilesListBox.Items.Count; i++)
            {
                if (_profilesListBox.Items[i] is PollProfile profile && profile.Id == preserveId.Value)
                {
                    _profilesListBox.SelectedIndex = i;
                    return;
                }
            }
        }

        _profilesListBox.SelectedIndex = 0;
    }

    private void UpdateOkEnabled()
    {
        _okButton.Enabled = _profilesListBox.SelectedItem is PollProfile;
    }

    private void UpdateDetails()
    {
        if (_profilesListBox.SelectedItem is not PollProfile profile)
        {
            _detailsLabel.Text = "Нет выбранного профиля.";
            return;
        }

        var lines = new List<string>
        {
            $"Вопрос: «{profile.Title}»",
            $"Варианты ({profile.Choices.Count}):",
        };

        lines.AddRange(profile.Choices.Select(c => $"  • {c}"));
        lines.Add($"Длительность: {profile.DurationSeconds} сек");

        if (profile.ChannelPointsVotingEnabled)
        {
            lines.Add($"Channel Points: {profile.ChannelPointsPerVote} за голос");
        }

        _detailsLabel.Text = string.Join(Environment.NewLine, lines);
    }
}
