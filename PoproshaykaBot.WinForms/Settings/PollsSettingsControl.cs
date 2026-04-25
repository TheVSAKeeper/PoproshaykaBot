using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Polls;

namespace PoproshaykaBot.WinForms.Settings;

public partial class PollsSettingsControl : UserControl
{
    private PollProfile? _current;
    private bool _suppressEditorEvents;

    public PollsSettingsControl()
    {
        InitializeComponent();

        _autoTriggerComboBox.Items.Clear();
        _autoTriggerComboBox.Items.AddRange([
            new AutoTriggerChoice(PollAutoTriggerEvent.None, "— нет —"),
            new AutoTriggerChoice(PollAutoTriggerEvent.StreamOnline, "При начале стрима"),
            new AutoTriggerChoice(PollAutoTriggerEvent.BroadcastProfileApplied, "При применении профиля трансляции"),
        ]);

        _autoTriggerComboBox.SelectedIndex = 0;
    }

    public event EventHandler? SettingChanged;

    [Inject]
    public PollProfilesManager Manager { get; internal init; } = null!;

    [Inject]
    public IPollController Controller { get; internal init; } = null!;

    [Inject]
    public TimeProvider TimeProvider { get; internal init; } = null!;

    public void LoadSettings(PollsSettings settings)
    {
        _suppressEditorEvents = true;
        try
        {
            ReloadList();
            LoadTemplates(settings.ChatTemplates);
            _progressIntervalNumeric.Value = Math.Clamp(settings.ChatTemplates.ProgressAnnounceIntervalSeconds,
                _progressIntervalNumeric.Minimum, _progressIntervalNumeric.Maximum);

            _historyMaxNumeric.Value = Math.Clamp(settings.HistoryMaxItems,
                _historyMaxNumeric.Minimum, _historyMaxNumeric.Maximum);

            _killSwitchCheckBox.Checked = settings.AutoTriggerKillSwitchDateUtc?.Date == TimeProvider.GetUtcNow().UtcDateTime.Date;
        }
        finally
        {
            _suppressEditorEvents = false;
        }
    }

    public void SaveSettings(PollsSettings settings)
    {
        FlushEditorToCurrent();

        try
        {
            Manager.ReplaceAll(_profilesListBox.Items.Cast<PollProfile>());
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SaveTemplates(settings.ChatTemplates);
        settings.ChatTemplates.ProgressAnnounceIntervalSeconds = (int)_progressIntervalNumeric.Value;
        settings.HistoryMaxItems = (int)_historyMaxNumeric.Value;
        settings.AutoTriggerKillSwitchDateUtc = _killSwitchCheckBox.Checked ? TimeProvider.GetUtcNow().UtcDateTime.Date : null;
    }

    private void OnProfileSelectionChanged(object? sender, EventArgs e)
    {
        _current = _profilesListBox.SelectedItem as PollProfile;
        LoadCurrentIntoEditor();
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (_suppressEditorEvents)
        {
            return;
        }

        FlushEditorToCurrent();
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnChannelPointsCheckedChanged(object? sender, EventArgs e)
    {
        _channelPointsPerVoteNumeric.Enabled = _channelPointsCheckBox.Checked;
        OnEditorChanged(sender, e);
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        var profile = new PollProfile
        {
            Name = $"Голосование {DateTime.Now:HHmmss}",
            Title = "Вопрос?",
            Choices = ["Да", "Нет"],
            DurationSeconds = 60,
        };

        _profilesListBox.Items.Add(profile);
        _profilesListBox.SelectedItem = profile;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (_current == null)
        {
            return;
        }

        var result = MessageBox.Show(this, $"Удалить профиль '{_current.Name}'?", "Подтверждение",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        var id = _current.Id;
        var index = _profilesListBox.Items.IndexOf(_current);
        _profilesListBox.Items.RemoveAt(index);
        Manager.Remove(id);

        if (_profilesListBox.Items.Count > 0)
        {
            _profilesListBox.SelectedIndex = Math.Min(index, _profilesListBox.Items.Count - 1);
        }
        else
        {
            _current = null;
            LoadCurrentIntoEditor();
        }

        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDuplicateClicked(object? sender, EventArgs e)
    {
        if (_current == null)
        {
            return;
        }

        FlushEditorToCurrent();

        var copy = new PollProfile
        {
            Id = Guid.NewGuid(),
            Name = $"{_current.Name} (копия)",
            Title = _current.Title,
            Choices = [.._current.Choices],
            DurationSeconds = _current.DurationSeconds,
            ChannelPointsVotingEnabled = _current.ChannelPointsVotingEnabled,
            ChannelPointsPerVote = _current.ChannelPointsPerVote,
            AutoTrigger = new()
            {
                Event = _current.AutoTrigger.Event,
                BroadcastProfileId = _current.AutoTrigger.BroadcastProfileId,
                CooldownMinutes = _current.AutoTrigger.CooldownMinutes,
            },
        };

        _profilesListBox.Items.Add(copy);
        _profilesListBox.SelectedItem = copy;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void OnStartClicked(object? sender, EventArgs e)
    {
        if (_current == null)
        {
            return;
        }

        FlushEditorToCurrent();

        try
        {
            Manager.Upsert(_current);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _startButton.Enabled = false;

        try
        {
            var snapshot = await Controller.StartAsync(_current, CancellationToken.None);

            if (snapshot is not null)
            {
                MessageBox.Show(this, $"Голосование «{snapshot.Title}» запущено.",
                    "Успешно", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _startButton.Enabled = true;
        }
    }

    private void ReloadList()
    {
        _profilesListBox.BeginUpdate();
        try
        {
            _profilesListBox.Items.Clear();
            foreach (var profile in Manager.GetAll())
            {
                _profilesListBox.Items.Add(profile);
            }

            _profilesListBox.DisplayMember = nameof(PollProfile.Name);
        }
        finally
        {
            _profilesListBox.EndUpdate();
        }

        if (_profilesListBox.Items.Count > 0)
        {
            _profilesListBox.SelectedIndex = 0;
        }
        else
        {
            _current = null;
            LoadCurrentIntoEditor();
        }
    }

    private void LoadCurrentIntoEditor()
    {
        _suppressEditorEvents = true;
        try
        {
            if (_current == null)
            {
                _nameTextBox.Text = string.Empty;
                _titleTextBox.Text = string.Empty;
                _choicesTextBox.Text = string.Empty;
                _durationNumeric.Value = _durationNumeric.Minimum;
                _channelPointsCheckBox.Checked = false;
                _channelPointsPerVoteNumeric.Value = _channelPointsPerVoteNumeric.Minimum;
                _autoTriggerComboBox.SelectedIndex = 0;
                _cooldownNumeric.Value = 0;
                return;
            }

            _nameTextBox.Text = _current.Name;
            _titleTextBox.Text = _current.Title;
            _choicesTextBox.Text = string.Join(Environment.NewLine, _current.Choices);
            _durationNumeric.Value = Math.Clamp(_current.DurationSeconds,
                (int)_durationNumeric.Minimum, (int)_durationNumeric.Maximum);

            _channelPointsCheckBox.Checked = _current.ChannelPointsVotingEnabled;
            _channelPointsPerVoteNumeric.Enabled = _current.ChannelPointsVotingEnabled;
            _channelPointsPerVoteNumeric.Value = Math.Clamp(_current.ChannelPointsPerVote,
                (int)_channelPointsPerVoteNumeric.Minimum, (int)_channelPointsPerVoteNumeric.Maximum);

            var triggerIndex = _current.AutoTrigger.Event switch
            {
                PollAutoTriggerEvent.StreamOnline => 1,
                PollAutoTriggerEvent.BroadcastProfileApplied => 2,
                _ => 0,
            };

            _autoTriggerComboBox.SelectedIndex = triggerIndex;

            _cooldownNumeric.Value = Math.Clamp(_current.AutoTrigger.CooldownMinutes,
                (int)_cooldownNumeric.Minimum, (int)_cooldownNumeric.Maximum);
        }
        finally
        {
            _suppressEditorEvents = false;
        }
    }

    private void FlushEditorToCurrent()
    {
        if (_current == null)
        {
            return;
        }

        _current.Name = _nameTextBox.Text.Trim();
        _current.Title = _titleTextBox.Text;
        _current.Choices = _choicesTextBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _current.DurationSeconds = (int)_durationNumeric.Value;
        _current.ChannelPointsVotingEnabled = _channelPointsCheckBox.Checked;
        _current.ChannelPointsPerVote = (int)_channelPointsPerVoteNumeric.Value;

        _current.AutoTrigger ??= new();
        _current.AutoTrigger.Event = ((AutoTriggerChoice)_autoTriggerComboBox.SelectedItem!).Event;
        _current.AutoTrigger.CooldownMinutes = (int)_cooldownNumeric.Value;

        var index = _profilesListBox.Items.IndexOf(_current);
        if (index >= 0)
        {
            _profilesListBox.Items[index] = _current;
            _profilesListBox.SelectedIndex = index;
        }
    }

    private void LoadTemplates(PollChatTemplatesSettings templates)
    {
        _startEnabledCheckBox.Checked = templates.StartEnabled;
        _startTemplateTextBox.Text = templates.StartTemplate;
        _progressEnabledCheckBox.Checked = templates.ProgressEnabled;
        _progressTemplateTextBox.Text = templates.ProgressTemplate;
        _endEnabledCheckBox.Checked = templates.EndEnabled;
        _endTemplateTextBox.Text = templates.EndTemplate;
        _terminatedEnabledCheckBox.Checked = templates.TerminatedEnabled;
        _terminatedTemplateTextBox.Text = templates.TerminatedTemplate;
        _archivedEnabledCheckBox.Checked = templates.ArchivedEnabled;
        _archivedTemplateTextBox.Text = templates.ArchivedTemplate;
    }

    private void SaveTemplates(PollChatTemplatesSettings templates)
    {
        templates.StartEnabled = _startEnabledCheckBox.Checked;
        templates.StartTemplate = _startTemplateTextBox.Text;
        templates.ProgressEnabled = _progressEnabledCheckBox.Checked;
        templates.ProgressTemplate = _progressTemplateTextBox.Text;
        templates.EndEnabled = _endEnabledCheckBox.Checked;
        templates.EndTemplate = _endTemplateTextBox.Text;
        templates.TerminatedEnabled = _terminatedEnabledCheckBox.Checked;
        templates.TerminatedTemplate = _terminatedTemplateTextBox.Text;
        templates.ArchivedEnabled = _archivedEnabledCheckBox.Checked;
        templates.ArchivedTemplate = _archivedTemplateTextBox.Text;
    }

    private sealed record AutoTriggerChoice(PollAutoTriggerEvent Event, string DisplayName)
    {
        public override string ToString()
        {
            return DisplayName;
        }
    }
}
