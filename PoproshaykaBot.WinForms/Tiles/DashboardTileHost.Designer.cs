namespace PoproshaykaBot.WinForms.Tiles;

sealed partial class DashboardTileHost
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
        _rootPanel = new Panel();
        _bodyPanel = new Panel();
        _emptyLabel = new Label();
        _headerPanel = new Panel();
        _titleLabel = new Label();
        _rootPanel.SuspendLayout();
        _bodyPanel.SuspendLayout();
        _headerPanel.SuspendLayout();
        SuspendLayout();
        // 
        // _rootPanel
        // 
        _rootPanel.BackColor = Color.White;
        _rootPanel.BorderStyle = BorderStyle.FixedSingle;
        _rootPanel.Controls.Add(_bodyPanel);
        _rootPanel.Controls.Add(_headerPanel);
        _rootPanel.Dock = DockStyle.Fill;
        _rootPanel.Location = new Point(0, 0);
        _rootPanel.Name = "_rootPanel";
        _rootPanel.Size = new Size(387, 309);
        _rootPanel.TabIndex = 0;
        // 
        // _bodyPanel
        // 
        _bodyPanel.Controls.Add(_emptyLabel);
        _bodyPanel.Dock = DockStyle.Fill;
        _bodyPanel.Location = new Point(0, 32);
        _bodyPanel.Name = "_bodyPanel";
        _bodyPanel.Padding = new Padding(4);
        _bodyPanel.Size = new Size(385, 275);
        _bodyPanel.TabIndex = 1;
        // 
        // _emptyLabel
        // 
        _emptyLabel.Dock = DockStyle.Fill;
        _emptyLabel.ForeColor = SystemColors.GrayText;
        _emptyLabel.Location = new Point(4, 4);
        _emptyLabel.Name = "_emptyLabel";
        _emptyLabel.Size = new Size(377, 267);
        _emptyLabel.TabIndex = 0;
        _emptyLabel.Text = "Нет данных";
        _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // _headerPanel
        // 
        _headerPanel.BackColor = SystemColors.Control;
        _headerPanel.Controls.Add(_titleLabel);
        _headerPanel.Dock = DockStyle.Top;
        _headerPanel.Location = new Point(0, 0);
        _headerPanel.Name = "_headerPanel";
        _headerPanel.Padding = new Padding(8, 0, 8, 0);
        _headerPanel.Size = new Size(385, 32);
        _headerPanel.TabIndex = 0;
        // 
        // _titleLabel
        // 
        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _titleLabel.Location = new Point(8, 0);
        _titleLabel.Name = "_titleLabel";
        _titleLabel.Size = new Size(369, 32);
        _titleLabel.TabIndex = 0;
        _titleLabel.Text = "Тайл";
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // DashboardTileHost
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_rootPanel);
        Name = "DashboardTileHost";
        Size = new Size(387, 309);
        _rootPanel.ResumeLayout(false);
        _bodyPanel.ResumeLayout(false);
        _headerPanel.ResumeLayout(false);
        ResumeLayout(false);
    }

    private Panel _rootPanel;
    private Panel _headerPanel;
    private Label _titleLabel;
    private Panel _bodyPanel;
    private Label _emptyLabel;
}
