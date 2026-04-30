namespace PoproshaykaBot.WinForms.Controls;

partial class PanelSlot
{
    private System.ComponentModel.IContainer components = null;
    private System.Windows.Forms.Panel _body;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _body = new Panel();
        SuspendLayout();
        // 
        // _body
        // 
        _body.Dock = DockStyle.Fill;
        _body.Location = new Point(0, 0);
        _body.Name = "_body";
        _body.Size = new Size(360, 250);
        _body.TabIndex = 0;
        // 
        // PanelSlot
        // 
        Controls.Add(_body);
        Name = "PanelSlot";
        Size = new Size(360, 250);
        ResumeLayout(false);
    }
}
