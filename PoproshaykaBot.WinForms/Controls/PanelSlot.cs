namespace PoproshaykaBot.WinForms.Controls;

public partial class PanelSlot : UserControl
{
    public PanelSlot()
    {
        InitializeComponent();
    }

    public void SetBody(Control? body)
    {
        _body.Controls.Clear();

        if (body == null)
        {
            return;
        }

        body.Dock = DockStyle.Fill;
        _body.Controls.Add(body);
    }
}
