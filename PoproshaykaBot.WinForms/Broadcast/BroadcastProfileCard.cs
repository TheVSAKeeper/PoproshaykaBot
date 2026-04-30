using PoproshaykaBot.WinForms.Broadcast.Profiles;

namespace PoproshaykaBot.WinForms.Broadcast;

public partial class BroadcastProfileCard : UserControl
{
    private const int LabelHeight = 18;
    private const int NameRowHeight = 22;

    public BroadcastProfileCard()
    {
        InitializeComponent();

        _nameLabel.Font = new(Font.FontFamily, Font.SizeInPoints + 1, FontStyle.Bold);
        _activeBadge.Font = new(Font, FontStyle.Bold);
        _titleLabel.Font = new(Font, FontStyle.Italic);

        _nameLabel.Height = NameRowHeight;
        _titleLabel.Height = LabelHeight;
        _metaLabel.Height = LabelHeight;
        _driftLabel.Height = LabelHeight;
        _applyingLabel.Height = LabelHeight;

        _applyMenuItem.Click += (_, e) => ApplyRequested?.Invoke(this, e);
        _editMenuItem.Click += (_, e) => EditRequested?.Invoke(this, e);
        _duplicateMenuItem.Click += (_, e) => DuplicateRequested?.Invoke(this, e);
        _deleteMenuItem.Click += (_, e) => DeleteRequested?.Invoke(this, e);

        DoubleClick += OnCardDoubleClick;
        SubscribeDoubleClickRecursively(this);

        _toolTip.SetToolTip(this, "Двойной клик — применить, ПКМ — действия");
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
        _applyingLabel.Visible = inFlight;
        _applyMenuItem.Enabled = !inFlight;
    }

    private void OnCardDoubleClick(object? sender, EventArgs e)
    {
        ApplyRequested?.Invoke(this, e);
    }

    private void SubscribeDoubleClickRecursively(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            child.DoubleClick += OnCardDoubleClick;
            SubscribeDoubleClickRecursively(child);
        }
    }
}
