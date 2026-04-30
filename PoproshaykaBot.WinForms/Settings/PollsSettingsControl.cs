using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Polls;

namespace PoproshaykaBot.WinForms.Settings;

public partial class PollsSettingsControl : UserControl
{
    private bool _suppressEditorEvents;

    public PollsSettingsControl()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingChanged;

    [Inject]
    public TimeProvider TimeProvider { get; internal init; } = null!;

    public void LoadSettings(PollsSettings settings)
    {
        _suppressEditorEvents = true;
        try
        {
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
        SaveTemplates(settings.ChatTemplates);
        settings.ChatTemplates.ProgressAnnounceIntervalSeconds = (int)_progressIntervalNumeric.Value;
        settings.HistoryMaxItems = (int)_historyMaxNumeric.Value;
        settings.AutoTriggerKillSwitchDateUtc = _killSwitchCheckBox.Checked ? TimeProvider.GetUtcNow().UtcDateTime.Date : null;
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        if (_suppressEditorEvents)
        {
            return;
        }

        SettingChanged?.Invoke(this, EventArgs.Empty);
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
}
