namespace PoproshaykaBot.WinForms.Settings;

partial class DashboardSettingsControl
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
        _toolbarPanel = new FlowLayoutPanel();
        _resetLayoutButton = new Button();
        _infoLabel = new Label();
        _rowsTableLayoutPanel = new TableLayoutPanel();
        _headerTableLayoutPanel = new TableLayoutPanel();
        _visibleHeaderLabel = new Label();
        _titleHeaderLabel = new Label();
        _columnSpanHeaderLabel = new Label();
        _rowSpanHeaderLabel = new Label();
        _orderHeaderLabel = new Label();
        _mainTableLayoutPanel.SuspendLayout();
        _toolbarPanel.SuspendLayout();
        _rowsTableLayoutPanel.SuspendLayout();
        _headerTableLayoutPanel.SuspendLayout();
        SuspendLayout();
        //
        // _mainTableLayoutPanel
        //
        _mainTableLayoutPanel.ColumnCount = 1;
        _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Controls.Add(_toolbarPanel, 0, 0);
        _mainTableLayoutPanel.Controls.Add(_infoLabel, 0, 1);
        _mainTableLayoutPanel.Controls.Add(_rowsTableLayoutPanel, 0, 2);
        _mainTableLayoutPanel.Dock = DockStyle.Fill;
        _mainTableLayoutPanel.Location = new Point(0, 0);
        _mainTableLayoutPanel.Name = "_mainTableLayoutPanel";
        _mainTableLayoutPanel.RowCount = 3;
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle());
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle());
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Size = new Size(593, 529);
        _mainTableLayoutPanel.TabIndex = 0;
        //
        // _toolbarPanel
        //
        _toolbarPanel.AutoSize = true;
        _toolbarPanel.Controls.Add(_resetLayoutButton);
        _toolbarPanel.Dock = DockStyle.Fill;
        _toolbarPanel.FlowDirection = FlowDirection.LeftToRight;
        _toolbarPanel.Location = new Point(3, 3);
        _toolbarPanel.Margin = new Padding(3, 3, 3, 6);
        _toolbarPanel.Name = "_toolbarPanel";
        _toolbarPanel.Size = new Size(587, 31);
        _toolbarPanel.TabIndex = 0;
        //
        // _resetLayoutButton
        //
        _resetLayoutButton.AutoSize = true;
        _resetLayoutButton.Location = new Point(0, 0);
        _resetLayoutButton.Margin = new Padding(0, 0, 3, 0);
        _resetLayoutButton.Name = "_resetLayoutButton";
        _resetLayoutButton.Padding = new Padding(8, 4, 8, 4);
        _resetLayoutButton.Size = new Size(180, 31);
        _resetLayoutButton.TabIndex = 0;
        _resetLayoutButton.Text = "Восстановить раскладку";
        _resetLayoutButton.UseVisualStyleBackColor = true;
        _resetLayoutButton.Click += OnResetLayoutButtonClicked;
        //
        // _infoLabel
        //
        _infoLabel.AutoSize = true;
        _infoLabel.Dock = DockStyle.Fill;
        _infoLabel.ForeColor = SystemColors.GrayText;
        _infoLabel.Location = new Point(3, 43);
        _infoLabel.Margin = new Padding(3, 3, 3, 6);
        _infoLabel.MaximumSize = new Size(580, 0);
        _infoLabel.Name = "_infoLabel";
        _infoLabel.Size = new Size(580, 30);
        _infoLabel.TabIndex = 1;
        _infoLabel.Text = "ℹ️ Включите нужные плитки, выставьте ширину/высоту в ячейках сетки и порядок размещения. Стрелки ↑ ↓ перемещают плитку выше или ниже в общей ленте.";
        //
        // _rowsTableLayoutPanel
        //
        _rowsTableLayoutPanel.AutoScroll = true;
        _rowsTableLayoutPanel.ColumnCount = 1;
        _rowsTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _rowsTableLayoutPanel.Controls.Add(_headerTableLayoutPanel, 0, 0);
        _rowsTableLayoutPanel.Dock = DockStyle.Fill;
        _rowsTableLayoutPanel.Location = new Point(3, 82);
        _rowsTableLayoutPanel.Name = "_rowsTableLayoutPanel";
        _rowsTableLayoutPanel.RowCount = 2;
        _rowsTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        _rowsTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _rowsTableLayoutPanel.Size = new Size(587, 444);
        _rowsTableLayoutPanel.TabIndex = 2;
        //
        // _headerTableLayoutPanel
        //
        _headerTableLayoutPanel.ColumnCount = 5;
        _headerTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        _headerTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _headerTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        _headerTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        _headerTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        _headerTableLayoutPanel.Controls.Add(_visibleHeaderLabel, 0, 0);
        _headerTableLayoutPanel.Controls.Add(_titleHeaderLabel, 1, 0);
        _headerTableLayoutPanel.Controls.Add(_columnSpanHeaderLabel, 2, 0);
        _headerTableLayoutPanel.Controls.Add(_rowSpanHeaderLabel, 3, 0);
        _headerTableLayoutPanel.Controls.Add(_orderHeaderLabel, 4, 0);
        _headerTableLayoutPanel.Dock = DockStyle.Fill;
        _headerTableLayoutPanel.Location = new Point(0, 0);
        _headerTableLayoutPanel.Margin = new Padding(0);
        _headerTableLayoutPanel.Name = "_headerTableLayoutPanel";
        _headerTableLayoutPanel.RowCount = 1;
        _headerTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _headerTableLayoutPanel.Size = new Size(587, 30);
        _headerTableLayoutPanel.TabIndex = 0;
        //
        // _visibleHeaderLabel
        //
        _visibleHeaderLabel.AutoSize = true;
        _visibleHeaderLabel.Dock = DockStyle.Fill;
        _visibleHeaderLabel.Location = new Point(3, 0);
        _visibleHeaderLabel.Name = "_visibleHeaderLabel";
        _visibleHeaderLabel.Size = new Size(74, 30);
        _visibleHeaderLabel.TabIndex = 0;
        _visibleHeaderLabel.Text = "Включена";
        _visibleHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _titleHeaderLabel
        //
        _titleHeaderLabel.AutoSize = true;
        _titleHeaderLabel.Dock = DockStyle.Fill;
        _titleHeaderLabel.Location = new Point(83, 0);
        _titleHeaderLabel.Name = "_titleHeaderLabel";
        _titleHeaderLabel.Size = new Size(231, 30);
        _titleHeaderLabel.TabIndex = 1;
        _titleHeaderLabel.Text = "Плитка";
        _titleHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _columnSpanHeaderLabel
        //
        _columnSpanHeaderLabel.AutoSize = true;
        _columnSpanHeaderLabel.Dock = DockStyle.Fill;
        _columnSpanHeaderLabel.Location = new Point(320, 0);
        _columnSpanHeaderLabel.Name = "_columnSpanHeaderLabel";
        _columnSpanHeaderLabel.Size = new Size(84, 30);
        _columnSpanHeaderLabel.TabIndex = 2;
        _columnSpanHeaderLabel.Text = "Колонок";
        _columnSpanHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _rowSpanHeaderLabel
        //
        _rowSpanHeaderLabel.AutoSize = true;
        _rowSpanHeaderLabel.Dock = DockStyle.Fill;
        _rowSpanHeaderLabel.Location = new Point(410, 0);
        _rowSpanHeaderLabel.Name = "_rowSpanHeaderLabel";
        _rowSpanHeaderLabel.Size = new Size(84, 30);
        _rowSpanHeaderLabel.TabIndex = 3;
        _rowSpanHeaderLabel.Text = "Строк";
        _rowSpanHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _orderHeaderLabel
        //
        _orderHeaderLabel.AutoSize = true;
        _orderHeaderLabel.Dock = DockStyle.Fill;
        _orderHeaderLabel.Location = new Point(500, 0);
        _orderHeaderLabel.Name = "_orderHeaderLabel";
        _orderHeaderLabel.Size = new Size(84, 30);
        _orderHeaderLabel.TabIndex = 4;
        _orderHeaderLabel.Text = "Порядок";
        _orderHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // DashboardSettingsControl
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_mainTableLayoutPanel);
        Name = "DashboardSettingsControl";
        Size = new Size(593, 529);
        _mainTableLayoutPanel.ResumeLayout(false);
        _mainTableLayoutPanel.PerformLayout();
        _toolbarPanel.ResumeLayout(false);
        _toolbarPanel.PerformLayout();
        _rowsTableLayoutPanel.ResumeLayout(false);
        _rowsTableLayoutPanel.PerformLayout();
        _headerTableLayoutPanel.ResumeLayout(false);
        _headerTableLayoutPanel.PerformLayout();
        ResumeLayout(false);
    }

    private TableLayoutPanel _mainTableLayoutPanel;
    private FlowLayoutPanel _toolbarPanel;
    private Button _resetLayoutButton;
    private Label _infoLabel;
    private TableLayoutPanel _rowsTableLayoutPanel;
    private TableLayoutPanel _headerTableLayoutPanel;
    private Label _visibleHeaderLabel;
    private Label _titleHeaderLabel;
    private Label _columnSpanHeaderLabel;
    private Label _rowSpanHeaderLabel;
    private Label _orderHeaderLabel;
}
