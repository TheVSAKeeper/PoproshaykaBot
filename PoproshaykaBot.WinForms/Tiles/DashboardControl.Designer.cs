namespace PoproshaykaBot.WinForms.Tiles;

sealed partial class DashboardControl
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
        _tilesTableLayoutPanel = new TableLayoutPanel();
        SuspendLayout();
        // 
        // _tilesTableLayoutPanel
        // 
        _tilesTableLayoutPanel.AutoScroll = true;
        _tilesTableLayoutPanel.ColumnCount = 4;
        _tilesTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        _tilesTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        _tilesTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        _tilesTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
        _tilesTableLayoutPanel.Dock = DockStyle.Fill;
        _tilesTableLayoutPanel.Location = new Point(0, 0);
        _tilesTableLayoutPanel.Name = "_tilesTableLayoutPanel";
        _tilesTableLayoutPanel.RowCount = 1;
        _tilesTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _tilesTableLayoutPanel.Size = new Size(766, 501);
        _tilesTableLayoutPanel.TabIndex = 0;
        // 
        // DashboardControl
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_tilesTableLayoutPanel);
        Name = "DashboardControl";
        Size = new Size(766, 501);
        ResumeLayout(false);
    }

    private TableLayoutPanel _tilesTableLayoutPanel;
}
