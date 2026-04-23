using PoproshaykaBot.WinForms.Broadcast.Profiles;

namespace PoproshaykaBot.WinForms.Broadcast;

public partial class BroadcastProfileEditDialog : Form
{
    private string _originalLanguage = string.Empty;

    public BroadcastProfileEditDialog()
    {
        InitializeComponent();
        _languageComboBox.Items.AddRange(["ru", "en"]);
        _nameTextBox.TextChanged += (_, _) => _okButton.Enabled = _nameTextBox.Text.Trim().Length > 0;
    }

    public static DialogResult Edit(IWin32Window owner, BroadcastProfile profile, IGameCategoryResolver resolver)
    {
        using var dialog = new BroadcastProfileEditDialog();
        dialog.Setup(profile, resolver);
        var result = dialog.ShowDialog(owner);
        if (result == DialogResult.OK)
        {
            dialog.SaveTo(profile);
        }

        return result;
    }

    private void Setup(BroadcastProfile profile, IGameCategoryResolver resolver)
    {
        _gameBox.Setup(resolver);
        _nameTextBox.Text = profile.Name;
        _titleTextBox.Text = profile.Title;
        _gameBox.SetSelected(profile.GameId, profile.GameName);
        _tagsTextBox.Text = string.Join(", ", profile.Tags);
        _originalLanguage = profile.BroadcasterLanguage ?? string.Empty;
        _languageComboBox.SelectedItem = _languageComboBox.Items
            .Cast<string>()
            .FirstOrDefault(item => string.Equals(item, _originalLanguage, StringComparison.OrdinalIgnoreCase));
        _okButton.Enabled = !string.IsNullOrWhiteSpace(profile.Name);
    }

    private void SaveTo(BroadcastProfile profile)
    {
        profile.Name = _nameTextBox.Text.Trim();
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
}
