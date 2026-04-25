namespace PoproshaykaBot.WinForms.Tiles;

public sealed partial class DashboardTileHost : UserControl
{
    public DashboardTileHost()
    {
        InitializeComponent();
    }

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
}
