namespace PoproshaykaBot.WinForms;

sealed partial class StreamInfoWidget
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
        _mainTableLayoutPanel = new TableLayoutPanel();
        _statusIconLabel = new Label();
        _statusTextLabel = new Label();
        _titleLabel = new Label();
        _gameLabel = new Label();
        _viewersLabel = new Label();
        _uptimeLabel = new Label();
        _mainTableLayoutPanel.SuspendLayout();
        SuspendLayout();
        // 
        // _mainTableLayoutPanel
        // 
        _mainTableLayoutPanel.ColumnCount = 2;
        _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40F));
        _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Controls.Add(_statusIconLabel, 0, 0);
        _mainTableLayoutPanel.Controls.Add(_statusTextLabel, 1, 0);
        _mainTableLayoutPanel.Controls.Add(_titleLabel, 0, 1);
        _mainTableLayoutPanel.Controls.Add(_gameLabel, 0, 2);
        _mainTableLayoutPanel.Controls.Add(_viewersLabel, 0, 3);
        _mainTableLayoutPanel.Controls.Add(_uptimeLabel, 1, 3);
        _mainTableLayoutPanel.Dock = DockStyle.Fill;
        _mainTableLayoutPanel.Location = new Point(0, 0);
        _mainTableLayoutPanel.Name = "_mainTableLayoutPanel";
        _mainTableLayoutPanel.Padding = new Padding(5);
        _mainTableLayoutPanel.RowCount = 4;
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Size = new Size(256, 108);
        _mainTableLayoutPanel.TabIndex = 0;
        // 
        // _statusIconLabel
        // 
        _statusIconLabel.Dock = DockStyle.Fill;
        _statusIconLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _statusIconLabel.Location = new Point(8, 5);
        _statusIconLabel.Name = "_statusIconLabel";
        _statusIconLabel.Size = new Size(34, 30);
        _statusIconLabel.TabIndex = 0;
        _statusIconLabel.Text = "⚪";
        _statusIconLabel.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // _statusTextLabel
        // 
        _statusTextLabel.Dock = DockStyle.Fill;
        _statusTextLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _statusTextLabel.Location = new Point(48, 5);
        _statusTextLabel.Name = "_statusTextLabel";
        _statusTextLabel.Size = new Size(200, 30);
        _statusTextLabel.TabIndex = 1;
        _statusTextLabel.Text = "UNKNOWN";
        _statusTextLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // _titleLabel
        // 
        _titleLabel.AutoEllipsis = true;
        _mainTableLayoutPanel.SetColumnSpan(_titleLabel, 2);
        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _titleLabel.Location = new Point(8, 35);
        _titleLabel.Name = "_titleLabel";
        _titleLabel.Size = new Size(240, 25);
        _titleLabel.TabIndex = 2;
        _titleLabel.Text = "Статус неизвестен";
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // _gameLabel
        // 
        _gameLabel.AutoEllipsis = true;
        _mainTableLayoutPanel.SetColumnSpan(_gameLabel, 2);
        _gameLabel.Dock = DockStyle.Fill;
        _gameLabel.Font = new Font("Segoe UI", 9F);
        _gameLabel.ForeColor = Color.DimGray;
        _gameLabel.Location = new Point(8, 60);
        _gameLabel.Name = "_gameLabel";
        _gameLabel.Size = new Size(240, 20);
        _gameLabel.TabIndex = 3;
        _gameLabel.Text = "—";
        _gameLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // _viewersLabel
        // 
        _viewersLabel.AutoSize = true;
        _viewersLabel.Dock = DockStyle.Fill;
        _viewersLabel.Font = new Font("Segoe UI", 9F);
        _viewersLabel.Location = new Point(8, 80);
        _viewersLabel.Name = "_viewersLabel";
        _viewersLabel.Size = new Size(34, 23);
        _viewersLabel.TabIndex = 4;
        _viewersLabel.Text = "👥 —";
        _viewersLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // _uptimeLabel
        // 
        _uptimeLabel.AutoSize = true;
        _uptimeLabel.Dock = DockStyle.Fill;
        _uptimeLabel.Font = new Font("Segoe UI", 9F);
        _uptimeLabel.Location = new Point(48, 80);
        _uptimeLabel.Name = "_uptimeLabel";
        _uptimeLabel.Size = new Size(200, 23);
        _uptimeLabel.TabIndex = 5;
        _uptimeLabel.Text = "⏱️ —";
        _uptimeLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // StreamInfoWidget
        // 
        BackColor = Color.White;
        BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_mainTableLayoutPanel);
        Name = "StreamInfoWidget";
        Size = new Size(256, 108);
        _mainTableLayoutPanel.ResumeLayout(false);
        _mainTableLayoutPanel.PerformLayout();
        ResumeLayout(false);
    }

    private Label _statusIconLabel;
    private Label _statusTextLabel;
    private Label _titleLabel;
    private Label _gameLabel;
    private Label _viewersLabel;
    private Label _uptimeLabel;
    private TableLayoutPanel _mainTableLayoutPanel;
}
