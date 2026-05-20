namespace PoproshaykaBot.WinForms.Widgets;

sealed partial class ObsOutputStatusCard
{
    private System.ComponentModel.IContainer components = null;

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
        _layoutTableLayoutPanel = new TableLayoutPanel();
        _titleLabel = new Label();
        _stateLabel = new Label();
        _timecodeLabel = new Label();
        _layoutTableLayoutPanel.SuspendLayout();
        SuspendLayout();
        //
        // _layoutTableLayoutPanel
        //
        _layoutTableLayoutPanel.ColumnCount = 1;
        _layoutTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layoutTableLayoutPanel.Controls.Add(_titleLabel, 0, 0);
        _layoutTableLayoutPanel.Controls.Add(_stateLabel, 0, 1);
        _layoutTableLayoutPanel.Controls.Add(_timecodeLabel, 0, 2);
        _layoutTableLayoutPanel.Dock = DockStyle.Fill;
        _layoutTableLayoutPanel.Location = new Point(0, 0);
        _layoutTableLayoutPanel.Margin = new Padding(0);
        _layoutTableLayoutPanel.Name = "_layoutTableLayoutPanel";
        _layoutTableLayoutPanel.RowCount = 3;
        _layoutTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
        _layoutTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _layoutTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
        _layoutTableLayoutPanel.Size = new Size(180, 76);
        _layoutTableLayoutPanel.TabIndex = 0;
        //
        // _titleLabel
        //
        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.ForeColor = Color.DimGray;
        _titleLabel.Location = new Point(0, 0);
        _titleLabel.Margin = new Padding(0);
        _titleLabel.Name = "_titleLabel";
        _titleLabel.Size = new Size(180, 20);
        _titleLabel.TabIndex = 0;
        _titleLabel.Text = "—";
        _titleLabel.TextAlign = ContentAlignment.MiddleCenter;
        //
        // _stateLabel
        //
        _stateLabel.AutoEllipsis = true;
        _stateLabel.Dock = DockStyle.Fill;
        _stateLabel.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _stateLabel.ForeColor = Color.Gray;
        _stateLabel.Location = new Point(0, 20);
        _stateLabel.Margin = new Padding(0);
        _stateLabel.Name = "_stateLabel";
        _stateLabel.Size = new Size(180, 38);
        _stateLabel.TabIndex = 1;
        _stateLabel.Text = "—";
        _stateLabel.TextAlign = ContentAlignment.MiddleCenter;
        //
        // _timecodeLabel
        //
        _timecodeLabel.Dock = DockStyle.Fill;
        _timecodeLabel.ForeColor = Color.DimGray;
        _timecodeLabel.Location = new Point(0, 58);
        _timecodeLabel.Margin = new Padding(0);
        _timecodeLabel.Name = "_timecodeLabel";
        _timecodeLabel.Size = new Size(180, 18);
        _timecodeLabel.TabIndex = 2;
        _timecodeLabel.Text = string.Empty;
        _timecodeLabel.TextAlign = ContentAlignment.MiddleCenter;
        _timecodeLabel.Visible = false;
        //
        // ObsOutputStatusCard
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_layoutTableLayoutPanel);
        Margin = new Padding(0);
        Name = "ObsOutputStatusCard";
        Size = new Size(180, 76);
        _layoutTableLayoutPanel.ResumeLayout(false);
        ResumeLayout(false);
    }

    private TableLayoutPanel _layoutTableLayoutPanel;
    private Label _titleLabel;
    private Label _stateLabel;
    private Label _timecodeLabel;
}
