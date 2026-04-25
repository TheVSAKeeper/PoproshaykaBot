namespace PoproshaykaBot.WinForms.Settings;

partial class DashboardTileRowControl
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
        _rootTableLayoutPanel = new TableLayoutPanel();
        _visibleCheckBox = new CheckBox();
        _titleLabel = new Label();
        _columnSpanNumeric = new NumericUpDown();
        _rowSpanNumeric = new NumericUpDown();
        _movePanel = new FlowLayoutPanel();
        _upButton = new Button();
        _downButton = new Button();
        _rootTableLayoutPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_columnSpanNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_rowSpanNumeric).BeginInit();
        _movePanel.SuspendLayout();
        SuspendLayout();
        //
        // _rootTableLayoutPanel
        //
        _rootTableLayoutPanel.ColumnCount = 5;
        _rootTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        _rootTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _rootTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        _rootTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        _rootTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        _rootTableLayoutPanel.Controls.Add(_visibleCheckBox, 0, 0);
        _rootTableLayoutPanel.Controls.Add(_titleLabel, 1, 0);
        _rootTableLayoutPanel.Controls.Add(_columnSpanNumeric, 2, 0);
        _rootTableLayoutPanel.Controls.Add(_rowSpanNumeric, 3, 0);
        _rootTableLayoutPanel.Controls.Add(_movePanel, 4, 0);
        _rootTableLayoutPanel.Dock = DockStyle.Fill;
        _rootTableLayoutPanel.Location = new Point(0, 0);
        _rootTableLayoutPanel.Name = "_rootTableLayoutPanel";
        _rootTableLayoutPanel.RowCount = 1;
        _rootTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _rootTableLayoutPanel.Size = new Size(440, 30);
        _rootTableLayoutPanel.TabIndex = 0;
        //
        // _visibleCheckBox
        //
        _visibleCheckBox.Anchor = AnchorStyles.Left;
        _visibleCheckBox.AutoSize = true;
        _visibleCheckBox.Margin = new Padding(3);
        _visibleCheckBox.Name = "_visibleCheckBox";
        _visibleCheckBox.Size = new Size(15, 14);
        _visibleCheckBox.TabIndex = 0;
        _visibleCheckBox.UseVisualStyleBackColor = true;
        _visibleCheckBox.CheckedChanged += OnInputChanged;
        //
        // _titleLabel
        //
        _titleLabel.Anchor = AnchorStyles.Left;
        _titleLabel.AutoSize = true;
        _titleLabel.Margin = new Padding(3);
        _titleLabel.Name = "_titleLabel";
        _titleLabel.Size = new Size(38, 15);
        _titleLabel.TabIndex = 1;
        _titleLabel.Text = "Плитка";
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _columnSpanNumeric
        //
        _columnSpanNumeric.Anchor = AnchorStyles.Left;
        _columnSpanNumeric.Margin = new Padding(3);
        _columnSpanNumeric.Maximum = new decimal(new int[] { 4, 0, 0, 0 });
        _columnSpanNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _columnSpanNumeric.Name = "_columnSpanNumeric";
        _columnSpanNumeric.Size = new Size(60, 23);
        _columnSpanNumeric.TabIndex = 2;
        _columnSpanNumeric.Value = new decimal(new int[] { 1, 0, 0, 0 });
        _columnSpanNumeric.ValueChanged += OnInputChanged;
        //
        // _rowSpanNumeric
        //
        _rowSpanNumeric.Anchor = AnchorStyles.Left;
        _rowSpanNumeric.Margin = new Padding(3);
        _rowSpanNumeric.Maximum = new decimal(new int[] { 3, 0, 0, 0 });
        _rowSpanNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _rowSpanNumeric.Name = "_rowSpanNumeric";
        _rowSpanNumeric.Size = new Size(60, 23);
        _rowSpanNumeric.TabIndex = 3;
        _rowSpanNumeric.Value = new decimal(new int[] { 1, 0, 0, 0 });
        _rowSpanNumeric.ValueChanged += OnInputChanged;
        //
        // _movePanel
        //
        _movePanel.Anchor = AnchorStyles.Left;
        _movePanel.AutoSize = true;
        _movePanel.Controls.Add(_upButton);
        _movePanel.Controls.Add(_downButton);
        _movePanel.FlowDirection = FlowDirection.LeftToRight;
        _movePanel.Margin = new Padding(3);
        _movePanel.Name = "_movePanel";
        _movePanel.Size = new Size(74, 28);
        _movePanel.TabIndex = 4;
        //
        // _upButton
        //
        _upButton.AutoSize = true;
        _upButton.Margin = new Padding(0, 0, 3, 0);
        _upButton.MinimumSize = new Size(32, 0);
        _upButton.Name = "_upButton";
        _upButton.Size = new Size(35, 25);
        _upButton.TabIndex = 0;
        _upButton.Text = "↑";
        _upButton.UseVisualStyleBackColor = true;
        _upButton.Click += OnUpButtonClicked;
        //
        // _downButton
        //
        _downButton.AutoSize = true;
        _downButton.Margin = new Padding(0);
        _downButton.MinimumSize = new Size(32, 0);
        _downButton.Name = "_downButton";
        _downButton.Size = new Size(35, 25);
        _downButton.TabIndex = 1;
        _downButton.Text = "↓";
        _downButton.UseVisualStyleBackColor = true;
        _downButton.Click += OnDownButtonClicked;
        //
        // DashboardTileRowControl
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_rootTableLayoutPanel);
        Name = "DashboardTileRowControl";
        Size = new Size(440, 30);
        _rootTableLayoutPanel.ResumeLayout(false);
        _rootTableLayoutPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_columnSpanNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)_rowSpanNumeric).EndInit();
        _movePanel.ResumeLayout(false);
        _movePanel.PerformLayout();
        ResumeLayout(false);
    }

    private TableLayoutPanel _rootTableLayoutPanel;
    private CheckBox _visibleCheckBox;
    private Label _titleLabel;
    private NumericUpDown _columnSpanNumeric;
    private NumericUpDown _rowSpanNumeric;
    private FlowLayoutPanel _movePanel;
    private Button _upButton;
    private Button _downButton;
}
