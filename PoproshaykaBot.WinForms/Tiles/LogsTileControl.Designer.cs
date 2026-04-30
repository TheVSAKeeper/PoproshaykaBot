namespace PoproshaykaBot.WinForms.Tiles;

sealed partial class LogsTileControl
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _logTextBox = new TextBox();
        SuspendLayout();
        // 
        // _logTextBox
        // 
        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Location = new Point(0, 0);
        _logTextBox.Multiline = true;
        _logTextBox.Name = "_logTextBox";
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.Size = new Size(438, 339);
        _logTextBox.TabIndex = 0;
        // 
        // LogsTileControl
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_logTextBox);
        Name = "LogsTileControl";
        Size = new Size(438, 339);
        ResumeLayout(false);
        PerformLayout();
    }

    private TextBox _logTextBox;
}
