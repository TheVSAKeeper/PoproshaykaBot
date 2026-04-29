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
        _headerToolStrip = new ToolStrip();
        _collapseButton = new ToolStripButton();
        _rootPanel.SuspendLayout();
        _bodyPanel.SuspendLayout();
        _headerPanel.SuspendLayout();
        _headerToolStrip.SuspendLayout();
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
        _headerPanel.Controls.Add(_headerToolStrip);
        _headerPanel.Dock = DockStyle.Top;
        _headerPanel.Location = new Point(0, 0);
        _headerPanel.Name = "_headerPanel";
        _headerPanel.Padding = new Padding(8, 0, 0, 0);
        _headerPanel.Size = new Size(385, 32);
        _headerPanel.TabIndex = 0;
        //
        // _titleLabel
        //
        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _titleLabel.Location = new Point(8, 0);
        _titleLabel.Name = "_titleLabel";
        _titleLabel.Size = new Size(345, 32);
        _titleLabel.TabIndex = 0;
        _titleLabel.Text = "Тайл";
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _headerToolStrip
        //
        _headerToolStrip.AutoSize = true;
        _headerToolStrip.BackColor = SystemColors.Control;
        _headerToolStrip.CanOverflow = false;
        _headerToolStrip.Dock = DockStyle.Right;
        _headerToolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _headerToolStrip.LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow;
        _headerToolStrip.ImageScalingSize = new Size(16, 16);
        _headerToolStrip.Items.AddRange(new ToolStripItem[] { _collapseButton });
        _headerToolStrip.Location = new Point(345, 0);
        _headerToolStrip.Name = "_headerToolStrip";
        _headerToolStrip.Padding = new Padding(0, 0, 4, 0);
        _headerToolStrip.RenderMode = ToolStripRenderMode.System;
        _headerToolStrip.Size = new Size(40, 32);
        _headerToolStrip.TabIndex = 1;
        //
        // _collapseButton
        //
        _collapseButton.Alignment = ToolStripItemAlignment.Right;
        _collapseButton.AutoToolTip = false;
        _collapseButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _collapseButton.Name = "_collapseButton";
        _collapseButton.Size = new Size(28, 28);
        _collapseButton.Text = "▾";
        _collapseButton.ToolTipText = "Свернуть";
        _collapseButton.Click += OnCollapseButtonClick;
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
        _headerPanel.PerformLayout();
        _headerToolStrip.ResumeLayout(false);
        _headerToolStrip.PerformLayout();
        ResumeLayout(false);
    }

    private Panel _rootPanel;
    private Panel _headerPanel;
    private Label _titleLabel;
    private ToolStrip _headerToolStrip;
    private ToolStripButton _collapseButton;
    private Panel _bodyPanel;
    private Label _emptyLabel;
}
