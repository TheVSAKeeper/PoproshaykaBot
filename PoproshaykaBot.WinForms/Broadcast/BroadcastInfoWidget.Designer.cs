﻿namespace PoproshaykaBot.WinForms.Broadcast;

sealed partial class BroadcastInfoWidget
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
        _statusLabel = new Label();
        _modeLabel = new Label();
        _sentCountLabel = new Label();
        _nextTimeLabel = new Label();
        _toggleButton = new Button();
        _modeToggleButton = new Button();
        _sendNowButton = new Button();
        _toolTip = new ToolTip();
        _mainTableLayoutPanel.SuspendLayout();
        SuspendLayout();
        //
        // _mainTableLayoutPanel
        //
        _mainTableLayoutPanel.ColumnCount = 2;
        _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        _mainTableLayoutPanel.Controls.Add(_statusLabel, 0, 0);
        _mainTableLayoutPanel.Controls.Add(_modeLabel, 0, 1);
        _mainTableLayoutPanel.Controls.Add(_sentCountLabel, 0, 2);
        _mainTableLayoutPanel.Controls.Add(_nextTimeLabel, 0, 3);
        _mainTableLayoutPanel.Controls.Add(_toggleButton, 1, 0);
        _mainTableLayoutPanel.Controls.Add(_modeToggleButton, 1, 1);
        _mainTableLayoutPanel.Controls.Add(_sendNowButton, 1, 2);
        _mainTableLayoutPanel.Dock = DockStyle.Fill;
        _mainTableLayoutPanel.Location = new Point(0, 0);
        _mainTableLayoutPanel.Name = "_mainTableLayoutPanel";
        _mainTableLayoutPanel.RowCount = 5;
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Size = new Size(254, 150);
        _mainTableLayoutPanel.TabIndex = 0;
        //
        // _statusLabel
        //
        _statusLabel.AutoSize = true;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _statusLabel.Location = new Point(3, 0);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Size = new Size(158, 32);
        _statusLabel.TabIndex = 0;
        _statusLabel.Text = "Бот не подключен";
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _modeLabel
        //
        _modeLabel.AutoSize = true;
        _modeLabel.Dock = DockStyle.Fill;
        _modeLabel.Location = new Point(3, 32);
        _modeLabel.Name = "_modeLabel";
        _modeLabel.Size = new Size(158, 28);
        _modeLabel.TabIndex = 1;
        _modeLabel.Text = "Режим: Ручной";
        _modeLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _sentCountLabel
        //
        _sentCountLabel.AutoSize = true;
        _sentCountLabel.Dock = DockStyle.Fill;
        _sentCountLabel.Location = new Point(3, 60);
        _sentCountLabel.Name = "_sentCountLabel";
        _sentCountLabel.Size = new Size(158, 28);
        _sentCountLabel.TabIndex = 2;
        _sentCountLabel.Text = "Отправлено: 0";
        _sentCountLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _nextTimeLabel
        //
        _nextTimeLabel.AutoSize = true;
        _nextTimeLabel.Dock = DockStyle.Fill;
        _nextTimeLabel.Location = new Point(3, 88);
        _nextTimeLabel.Name = "_nextTimeLabel";
        _nextTimeLabel.Size = new Size(158, 28);
        _nextTimeLabel.TabIndex = 3;
        _nextTimeLabel.Text = "Следующая: —";
        _nextTimeLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _toggleButton
        //
        _toggleButton.Dock = DockStyle.Fill;
        _toggleButton.Location = new Point(167, 3);
        _toggleButton.Name = "_toggleButton";
        _toggleButton.Size = new Size(84, 26);
        _toggleButton.TabIndex = 4;
        _toggleButton.Text = "Старт";
        _toggleButton.UseVisualStyleBackColor = true;
        _toggleButton.Click += OnToggleButtonClick;
        //
        // _modeToggleButton
        //
        _modeToggleButton.Dock = DockStyle.Fill;
        _modeToggleButton.Location = new Point(167, 35);
        _modeToggleButton.Name = "_modeToggleButton";
        _modeToggleButton.Size = new Size(84, 22);
        _modeToggleButton.TabIndex = 5;
        _modeToggleButton.Text = "В авто";
        _modeToggleButton.UseVisualStyleBackColor = true;
        _modeToggleButton.Click += OnModeToggleClick;
        //
        // _sendNowButton
        //
        _sendNowButton.Dock = DockStyle.Fill;
        _sendNowButton.Location = new Point(167, 63);
        _sendNowButton.Name = "_sendNowButton";
        _mainTableLayoutPanel.SetRowSpan(_sendNowButton, 2);
        _sendNowButton.Size = new Size(84, 84);
        _sendNowButton.TabIndex = 6;
        _sendNowButton.Text = "Сейчас";
        _sendNowButton.UseVisualStyleBackColor = true;
        _sendNowButton.Click += OnSendNowButtonClick;
        //
        // BroadcastInfoWidget
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_mainTableLayoutPanel);
        Name = "BroadcastInfoWidget";
        Size = new Size(254, 150);
        _mainTableLayoutPanel.ResumeLayout(false);
        _mainTableLayoutPanel.PerformLayout();
        ResumeLayout(false);
    }

    private Label _statusLabel;
    private Label _modeLabel;
    private Label _sentCountLabel;
    private Label _nextTimeLabel;
    private Button _toggleButton;
    private Button _modeToggleButton;
    private Button _sendNowButton;
    private ToolTip _toolTip;
    private TableLayoutPanel _mainTableLayoutPanel;
}
