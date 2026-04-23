using PoproshaykaBot.WinForms.Broadcast.Profiles;

namespace PoproshaykaBot.WinForms.Broadcast;

public partial class BroadcastProfileCard : UserControl
{
    public BroadcastProfileCard()
    {
        InitializeComponent();

        _nameLabel.Font = new(Font.FontFamily, Font.SizeInPoints + 1, FontStyle.Bold);
        _activeBadge.Font = new(Font, FontStyle.Bold);
        _titleLabel.Font = new(Font, FontStyle.Italic);

        _applyButton.Click += (_, e) => ApplyRequested?.Invoke(this, e);
        _editButton.Click += (_, e) => EditRequested?.Invoke(this, e);
        _menuButton.Click += OnMenuButtonClick;

        _duplicateMenuItem.Click += (_, e) => DuplicateRequested?.Invoke(this, e);
        _deleteMenuItem.Click += (_, e) => DeleteRequested?.Invoke(this, e);

        _toolTip.SetToolTip(_applyButton, "Применить");
        _toolTip.SetToolTip(_editButton, "Редактировать");
        _toolTip.SetToolTip(_menuButton, "Действия");
    }

    public event EventHandler? ApplyRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? DuplicateRequested;
    public event EventHandler? EditRequested;

    public BroadcastProfile? Profile { get; private set; }
    public bool IsActive { get; private set; }

    public void UpdateFrom(BroadcastProfile profile, bool isActive, bool hasDrift)
    {
        Profile = profile;
        IsActive = isActive;

        _nameLabel.Text = profile.Name;

        if (string.IsNullOrWhiteSpace(profile.Title))
        {
            _titleLabel.Visible = false;
            _titleLabel.Text = string.Empty;
        }
        else
        {
            _titleLabel.Text = $"«{profile.Title}»";
            _titleLabel.Visible = true;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.GameName))
        {
            parts.Add("🎮 " + profile.GameName);
        }

        if (profile.Tags.Count > 0)
        {
            parts.Add(string.Join(" ", profile.Tags.Select(t => "#" + t)));
        }

        if (!string.IsNullOrWhiteSpace(profile.BroadcasterLanguage))
        {
            parts.Add(profile.BroadcasterLanguage.ToUpperInvariant());
        }

        _metaLabel.Text = string.Join("  •  ", parts);
        _metaLabel.Visible = _metaLabel.Text.Length > 0;

        _driftLabel.Visible = hasDrift;
        _activeBadge.Visible = isActive;

        BackColor = isActive
            ? Color.FromArgb(240, 255, 240)
            : SystemColors.Window;
    }

    public void SetApplyInFlight(bool inFlight)
    {
        _applyButton.Enabled = !inFlight;
    }

    private void OnMenuButtonClick(object? sender, EventArgs e)
    {
        _menu.Show(_menuButton, new(0, _menuButton.Height));
    }
}
