using PoproshaykaBot.WinForms.Broadcast.Profiles;

namespace PoproshaykaBot.WinForms.Broadcast;

public partial class BroadcastProfileEditDialog : Form
{
    private string _originalLanguage = string.Empty;
    private bool _nameRequired = true;

    public BroadcastProfileEditDialog()
    {
        InitializeComponent();
    }

    public void LoadFrom(BroadcastProfile profile)
    {
        _nameTextBox.Text = profile.Name;
        _titleTextBox.Text = profile.Title;
        _gameBox.SetSelected(profile.GameId, profile.GameName);
        _tagsTextBox.Text = string.Join(", ", profile.Tags);
        _originalLanguage = profile.BroadcasterLanguage ?? string.Empty;
        _languageComboBox.SelectedItem = _languageComboBox.Items
            .Cast<string>()
            .FirstOrDefault(item => string.Equals(item, _originalLanguage, StringComparison.OrdinalIgnoreCase));

        UpdateOkButtonState();
    }

    public void ConfigureCurrentSettingsMode()
    {
        Text = "Текущие настройки эфира";
        _nameRequired = false;
        _nameLbl.Visible = false;
        _nameTextBox.Visible = false;
        _mainLayout.RowStyles[0].SizeType = SizeType.Absolute;
        _mainLayout.RowStyles[0].Height = 0;
        UpdateOkButtonState();
    }

    public void SaveTo(BroadcastProfile profile)
    {
        if (_nameRequired)
        {
            profile.Name = _nameTextBox.Text.Trim();
        }

        profile.Title = _titleTextBox.Text.Trim();
        if (_gameBox.Selected != null)
        {
            profile.GameId = _gameBox.Selected.Id;
            profile.GameName = _gameBox.Selected.Name;
        }

        profile.Tags = _tagsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        profile.BroadcasterLanguage = _languageComboBox.SelectedItem?.ToString() ?? _originalLanguage;
    }

    private void OnNameTextChanged(object? sender, EventArgs e)
    {
        UpdateOkButtonState();
    }

    private void UpdateOkButtonState()
    {
        _okButton.Enabled = !_nameRequired || _nameTextBox.Text.Trim().Length > 0;
    }
}
