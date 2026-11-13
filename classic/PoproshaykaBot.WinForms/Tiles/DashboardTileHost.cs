namespace PoproshaykaBot.WinForms.Tiles;

public sealed partial class DashboardTileHost : UserControl
{
    private readonly List<ToolStripItem> _customHeaderItems = [];

    public DashboardTileHost()
    {
        InitializeComponent();
    }

    public event EventHandler? CollapseToggled;

    public bool IsCollapsed { get; private set; }

    public int HeaderHeight => _headerPanel.Height;

    public void SetTitle(string title)
    {
        _titleLabel.Text = title;
    }

    public void SetBody(Control? body)
    {
        foreach (var control in _bodyPanel.Controls.Cast<Control>().Where(control => control != _emptyLabel).ToList())
        {
            _bodyPanel.Controls.Remove(control);
            control.Dispose();
        }

        if (body == null)
        {
            _emptyLabel.Visible = true;
            _emptyLabel.BringToFront();
            return;
        }

        _emptyLabel.Visible = false;
        body.Dock = DockStyle.Fill;
        _bodyPanel.Controls.Add(body);
        body.BringToFront();
    }

    public void SetHeaderActions(IReadOnlyList<ToolStripItem>? items)
    {
        _headerToolStrip.SuspendLayout();

        try
        {
            foreach (var existing in _customHeaderItems)
            {
                _headerToolStrip.Items.Remove(existing);
            }

            _customHeaderItems.Clear();

            if (items == null || items.Count == 0)
            {
                return;
            }

            var insertIndex = 0;

            foreach (var item in items)
            {
                _headerToolStrip.Items.Insert(insertIndex++, item);
                _customHeaderItems.Add(item);
            }
        }
        finally
        {
            _headerToolStrip.ResumeLayout();
        }
    }

    public void SetCollapsed(bool collapsed)
    {
        IsCollapsed = collapsed;
        _bodyPanel.Visible = !collapsed;

        foreach (var item in _customHeaderItems)
        {
            item.Visible = !collapsed;
        }

        _collapseButton.Text = collapsed ? "▸" : "▾";
        _collapseButton.ToolTipText = collapsed ? "Развернуть" : "Свернуть";
    }

    private void OnCollapseButtonClick(object? sender, EventArgs e)
    {
        CollapseToggled?.Invoke(this, EventArgs.Empty);
    }
}
