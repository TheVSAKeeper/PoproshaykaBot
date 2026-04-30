namespace PoproshaykaBot.WinForms.Polls;

partial class ActivePollCard
{
    private System.ComponentModel.IContainer components = null;

    private TableLayoutPanel _outerLayout;
    private TableLayoutPanel _headerRow;
    private Label _titleLabel;
    private Button _stopButton;
    private TableLayoutPanel _choicesPanel;
    private Label _footerLabel;
    private ToolTip _toolTip;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _outerLayout = new TableLayoutPanel();
        _headerRow = new TableLayoutPanel();
        _titleLabel = new Label();
        _stopButton = new Button();
        _choicesPanel = new TableLayoutPanel();
        _footerLabel = new Label();
        _toolTip = new ToolTip(components);
        _outerLayout.SuspendLayout();
        _headerRow.SuspendLayout();
        SuspendLayout();
        // 
        // _outerLayout
        // 
        _outerLayout.ColumnCount = 1;
        _outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _outerLayout.Controls.Add(_headerRow, 0, 0);
        _outerLayout.Controls.Add(_choicesPanel, 0, 1);
        _outerLayout.Controls.Add(_footerLabel, 0, 2);
        _outerLayout.Dock = DockStyle.Fill;
        _outerLayout.Location = new Point(8, 6);
        _outerLayout.Margin = new Padding(0);
        _outerLayout.Name = "_outerLayout";
        _outerLayout.RowCount = 3;
        _outerLayout.RowStyles.Add(new RowStyle());
        _outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _outerLayout.RowStyles.Add(new RowStyle());
        _outerLayout.Size = new Size(411, 196);
        _outerLayout.TabIndex = 0;
        // 
        // _headerRow
        // 
        _headerRow.ColumnCount = 2;
        _headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _headerRow.ColumnStyles.Add(new ColumnStyle());
        _headerRow.Controls.Add(_titleLabel, 0, 0);
        _headerRow.Controls.Add(_stopButton, 1, 0);
        _headerRow.Dock = DockStyle.Top;
        _headerRow.Location = new Point(0, 0);
        _headerRow.Margin = new Padding(0, 0, 0, 4);
        _headerRow.Name = "_headerRow";
        _headerRow.RowCount = 1;
        _headerRow.RowStyles.Add(new RowStyle());
        _headerRow.Size = new Size(411, 30);
        _headerRow.TabIndex = 0;
        // 
        // _titleLabel
        // 
        _titleLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _titleLabel.AutoEllipsis = true;
        _titleLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _titleLabel.Location = new Point(0, 0);
        _titleLabel.Margin = new Padding(0);
        _titleLabel.Name = "_titleLabel";
        _titleLabel.Size = new Size(318, 30);
        _titleLabel.TabIndex = 0;
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // _stopButton
        // 
        _stopButton.Anchor = AnchorStyles.Right;
        _stopButton.AutoSize = true;
        _stopButton.FlatStyle = FlatStyle.Flat;
        _stopButton.Location = new Point(318, 1);
        _stopButton.Margin = new Padding(0);
        _stopButton.MinimumSize = new Size(80, 26);
        _stopButton.Name = "_stopButton";
        _stopButton.Size = new Size(93, 27);
        _stopButton.TabIndex = 1;
        _stopButton.Text = "■ Завершить";
        _toolTip.SetToolTip(_stopButton, "Завершить голосование и показать результат зрителям");
        _stopButton.UseVisualStyleBackColor = true;
        _stopButton.Visible = false;
        _stopButton.Click += OnStopButtonClick;
        // 
        // _choicesPanel
        // 
        _choicesPanel.ColumnCount = 3;
        _choicesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        _choicesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        _choicesPanel.ColumnStyles.Add(new ColumnStyle());
        _choicesPanel.Dock = DockStyle.Fill;
        _choicesPanel.Location = new Point(0, 34);
        _choicesPanel.Margin = new Padding(0);
        _choicesPanel.Name = "_choicesPanel";
        _choicesPanel.Size = new Size(411, 143);
        _choicesPanel.TabIndex = 1;
        // 
        // _footerLabel
        // 
        _footerLabel.AutoSize = true;
        _footerLabel.Dock = DockStyle.Top;
        _footerLabel.Location = new Point(0, 181);
        _footerLabel.Margin = new Padding(0, 4, 0, 0);
        _footerLabel.Name = "_footerLabel";
        _footerLabel.Size = new Size(411, 15);
        _footerLabel.TabIndex = 2;
        _footerLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // ActivePollCard
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = SystemColors.Window;
        BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_outerLayout);
        Margin = new Padding(4, 2, 4, 2);
        MinimumSize = new Size(320, 0);
        Name = "ActivePollCard";
        Padding = new Padding(8, 6, 8, 6);
        Size = new Size(427, 208);
        _outerLayout.ResumeLayout(false);
        _outerLayout.PerformLayout();
        _headerRow.ResumeLayout(false);
        _headerRow.PerformLayout();
        ResumeLayout(false);
    }
}
