using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using System.Text.Json;

namespace PoproshaykaBot.WinForms.Settings;

public partial class BroadcastProfilesSettingsControl : UserControl
{
    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private BroadcastProfile? _current;

    public BroadcastProfilesSettingsControl()
    {
        InitializeComponent();
        _languageComboBox.Items.AddRange(["ru", "en"]);
    }

    public event EventHandler? SettingChanged;

    [Inject]
    public BroadcastProfilesManager Manager { get; internal init; } = null!;

    public void SaveSettings(TwitchSettings _)
    {
        FlushEditorToCurrent();

        var items = _profilesListBox.Items.Cast<BroadcastProfile>().ToList();
        var persistedIds = Manager.GetAll().Select(p => p.Id).ToHashSet();

        try
        {
            foreach (var item in items)
            {
                if (!persistedIds.Contains(item.Id) || ReferenceEquals(item, _current))
                {
                    Manager.Upsert(item);
                }
            }

            ReloadList();

            if (_current != null)
            {
                _profilesListBox.SelectedItem = _current;
            }
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void LoadSettings(TwitchSettings _)
    {
        ReloadList();
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        _current = _profilesListBox.SelectedItem as BroadcastProfile;
        LoadCurrentIntoEditor();
    }

    private void OnEditorChanged(object? sender, EventArgs e)
    {
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        var profile = new BroadcastProfile
        {
            Name = $"Профиль {DateTime.Now:HHmmss}",
        };

        _current = profile;
        _profilesListBox.Items.Add(profile);
        _profilesListBox.SelectedItem = profile;
        LoadCurrentIntoEditor();
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemoveClicked(object? sender, EventArgs e)
    {
        if (_current == null)
        {
            return;
        }

        Manager.Remove(_current.Id);
        _current = null;
        ReloadList();
        LoadCurrentIntoEditor();
    }

    private void OnDuplicateClicked(object? sender, EventArgs e)
    {
        if (_current == null)
        {
            return;
        }

        var copy = new BroadcastProfile
        {
            Name = _current.Name + " (копия)",
            Title = _current.Title,
            GameId = _current.GameId,
            GameName = _current.GameName,
            BroadcasterLanguage = _current.BroadcasterLanguage,
            Tags = _current.Tags.ToList(),
        };

        _current = copy;
        _profilesListBox.Items.Add(copy);
        _profilesListBox.SelectedItem = copy;
        SettingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnImportClicked(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog();
        dialog.Title = "Импорт профилей из JSON";
        dialog.Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*";
        dialog.CheckFileExists = true;

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        List<ExternalTwitchProfile>? incoming;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            incoming = JsonSerializer.Deserialize<List<ExternalTwitchProfile>>(json, ImportJsonOptions);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, $"Не удалось прочитать файл: {exception.Message}",
                "Ошибка импорта", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return;
        }

        if (incoming == null || incoming.Count == 0)
        {
            MessageBox.Show(this, "В файле нет профилей.",
                "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        var imported = 0;
        var skipped = 0;
        var existingNames = new HashSet<string>(Manager.GetAll().Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var external in incoming)
        {
            if (string.IsNullOrWhiteSpace(external.Name))
            {
                skipped++;
                continue;
            }

            var name = external.Name;
            var suffix = 2;

            while (existingNames.Contains(name))
            {
                name = $"{external.Name} ({suffix++})";
            }

            var profile = new BroadcastProfile
            {
                Name = name,
                Title = external.Title ?? string.Empty,
                GameId = external.CategoryId ?? string.Empty,
            };

            try
            {
                Manager.Upsert(profile);
                existingNames.Add(name);
                imported++;
            }
            catch (InvalidOperationException)
            {
                skipped++;
            }
        }

        ReloadList();

        MessageBox.Show(this,
            $"Импортировано: {imported}. Пропущено: {skipped}.",
            "Импорт профилей", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async void OnApplyNowClicked(object? sender, EventArgs e)
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

        _applyNowButton.Enabled = false;

        try
        {
            await Manager.ApplyAsync(_current.Id, CancellationToken.None);
        }
        finally
        {
            _applyNowButton.Enabled = true;
        }
    }

    private void ReloadList()
    {
        _profilesListBox.Items.Clear();

        foreach (var p in Manager.GetAll())
        {
            _profilesListBox.Items.Add(p);
        }

        _profilesListBox.DisplayMember = nameof(BroadcastProfile.Name);
    }

    private void LoadCurrentIntoEditor()
    {
        if (_current == null)
        {
            _nameTextBox.Text = string.Empty;
            _titleTextBox.Text = string.Empty;
            _gameBox.SetSelected(string.Empty, string.Empty);
            _tagsTextBox.Text = string.Empty;
            _languageComboBox.SelectedItem = null;
            return;
        }

        _nameTextBox.Text = _current.Name;
        _titleTextBox.Text = _current.Title;
        _gameBox.SetSelected(_current.GameId, _current.GameName);
        _tagsTextBox.Text = string.Join(", ", _current.Tags);
        _languageComboBox.SelectedItem = _current.BroadcasterLanguage;
    }

    private void FlushEditorToCurrent()
    {
        if (_current == null)
        {
            return;
        }

        _current.Name = _nameTextBox.Text.Trim();
        _current.Title = _titleTextBox.Text.Trim();

        if (_gameBox.Selected != null)
        {
            _current.GameId = _gameBox.Selected.Id;
            _current.GameName = _gameBox.Selected.Name;
        }

        _current.Tags = _tagsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        _current.BroadcasterLanguage = _languageComboBox.SelectedItem?.ToString() ?? "ru";
    }

    private sealed record ExternalTwitchProfile(string? Name, string? Title, string? CategoryId);
}
