namespace PoproshaykaBot.WinForms.Settings
{
    partial class AutoBroadcastSettingsControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            _mainTableLayoutPanel = new TableLayoutPanel();
            _autoBroadcastEnabledCheckBox = new CheckBox();
            _streamNotificationsEnabledCheckBox = new CheckBox();
            _streamStartMessageLabel = new Label();
            _streamStartMessageTextBox = new TextBox();
            _streamStopMessageLabel = new Label();
            _streamStopMessageTextBox = new TextBox();
            _broadcastIntervalLabel = new Label();
            _broadcastIntervalNumericUpDown = new NumericUpDown();
            _broadcastTemplateLabel = new Label();
            _broadcastTemplateTextBox = new TextBox();
            _infoLabel = new Label();
            _mainTableLayoutPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_broadcastIntervalNumericUpDown).BeginInit();
            SuspendLayout();
            // 
            // _mainTableLayoutPanel
            // 
            _mainTableLayoutPanel.ColumnCount = 1;
            _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _mainTableLayoutPanel.Controls.Add(_autoBroadcastEnabledCheckBox, 0, 0);
            _mainTableLayoutPanel.Controls.Add(_streamNotificationsEnabledCheckBox, 0, 1);
            _mainTableLayoutPanel.Controls.Add(_streamStartMessageLabel, 0, 2);
            _mainTableLayoutPanel.Controls.Add(_streamStartMessageTextBox, 0, 3);
            _mainTableLayoutPanel.Controls.Add(_streamStopMessageLabel, 0, 4);
            _mainTableLayoutPanel.Controls.Add(_streamStopMessageTextBox, 0, 5);
            _mainTableLayoutPanel.Controls.Add(_broadcastIntervalLabel, 0, 6);
            _mainTableLayoutPanel.Controls.Add(_broadcastIntervalNumericUpDown, 0, 7);
            _mainTableLayoutPanel.Controls.Add(_broadcastTemplateLabel, 0, 8);
            _mainTableLayoutPanel.Controls.Add(_broadcastTemplateTextBox, 0, 9);
            _mainTableLayoutPanel.Controls.Add(_infoLabel, 0, 10);
            _mainTableLayoutPanel.Dock = DockStyle.Fill;
            _mainTableLayoutPanel.Location = new Point(0, 0);
            _mainTableLayoutPanel.Name = "_mainTableLayoutPanel";
            _mainTableLayoutPanel.RowCount = 12;
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 60F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _mainTableLayoutPanel.Size = new Size(400, 360);
            _mainTableLayoutPanel.TabIndex = 0;
            // 
            // _autoBroadcastEnabledCheckBox
            // 
            _autoBroadcastEnabledCheckBox.AutoSize = true;
            _autoBroadcastEnabledCheckBox.Dock = DockStyle.Fill;
            _autoBroadcastEnabledCheckBox.Location = new Point(3, 3);
            _autoBroadcastEnabledCheckBox.Name = "_autoBroadcastEnabledCheckBox";
            _autoBroadcastEnabledCheckBox.Size = new Size(394, 24);
            _autoBroadcastEnabledCheckBox.TabIndex = 0;
            _autoBroadcastEnabledCheckBox.Text = "Включить автоматическое управление рассылкой";
            _autoBroadcastEnabledCheckBox.UseVisualStyleBackColor = true;
            _autoBroadcastEnabledCheckBox.CheckedChanged += OnSettingChanged;
            // 
            // _streamNotificationsEnabledCheckBox
            // 
            _streamNotificationsEnabledCheckBox.AutoSize = true;
            _streamNotificationsEnabledCheckBox.Dock = DockStyle.Fill;
            _streamNotificationsEnabledCheckBox.Location = new Point(3, 33);
            _streamNotificationsEnabledCheckBox.Name = "_streamNotificationsEnabledCheckBox";
            _streamNotificationsEnabledCheckBox.Size = new Size(394, 24);
            _streamNotificationsEnabledCheckBox.TabIndex = 1;
            _streamNotificationsEnabledCheckBox.Text = "Отправлять уведомления о статусе стрима в чат";
            _streamNotificationsEnabledCheckBox.UseVisualStyleBackColor = true;
            _streamNotificationsEnabledCheckBox.CheckedChanged += OnSettingChanged;
            // 
            // _streamStartMessageLabel
            // 
            _streamStartMessageLabel.AutoSize = true;
            _streamStartMessageLabel.Dock = DockStyle.Fill;
            _streamStartMessageLabel.Location = new Point(3, 60);
            _streamStartMessageLabel.Name = "_streamStartMessageLabel";
            _streamStartMessageLabel.Size = new Size(394, 25);
            _streamStartMessageLabel.TabIndex = 2;
            _streamStartMessageLabel.Text = "Сообщение при запуске стрима:";
            _streamStartMessageLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _streamStartMessageTextBox
            // 
            _streamStartMessageTextBox.Dock = DockStyle.Fill;
            _streamStartMessageTextBox.Location = new Point(3, 88);
            _streamStartMessageTextBox.Name = "_streamStartMessageTextBox";
            _streamStartMessageTextBox.Size = new Size(394, 23);
            _streamStartMessageTextBox.TabIndex = 3;
            _streamStartMessageTextBox.TextChanged += OnSettingChanged;
            // 
            // _streamStopMessageLabel
            // 
            _streamStopMessageLabel.AutoSize = true;
            _streamStopMessageLabel.Dock = DockStyle.Fill;
            _streamStopMessageLabel.Location = new Point(3, 115);
            _streamStopMessageLabel.Name = "_streamStopMessageLabel";
            _streamStopMessageLabel.Size = new Size(394, 25);
            _streamStopMessageLabel.TabIndex = 4;
            _streamStopMessageLabel.Text = "Сообщение при остановке стрима:";
            _streamStopMessageLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _streamStopMessageTextBox
            // 
            _streamStopMessageTextBox.Dock = DockStyle.Fill;
            _streamStopMessageTextBox.Location = new Point(3, 143);
            _streamStopMessageTextBox.Name = "_streamStopMessageTextBox";
            _streamStopMessageTextBox.Size = new Size(394, 23);
            _streamStopMessageTextBox.TabIndex = 5;
            _streamStopMessageTextBox.TextChanged += OnSettingChanged;
            // 
            // _broadcastIntervalLabel
            // 
            _broadcastIntervalLabel.AutoSize = true;
            _broadcastIntervalLabel.Dock = DockStyle.Fill;
            _broadcastIntervalLabel.Location = new Point(3, 200);
            _broadcastIntervalLabel.Name = "_broadcastIntervalLabel";
            _broadcastIntervalLabel.Size = new Size(394, 25);
            _broadcastIntervalLabel.TabIndex = 6;
            _broadcastIntervalLabel.Text = "Интервал рассылки (минуты):";
            _broadcastIntervalLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _broadcastIntervalNumericUpDown
            // 
            _broadcastIntervalNumericUpDown.Dock = DockStyle.Left;
            _broadcastIntervalNumericUpDown.Location = new Point(3, 228);
            _broadcastIntervalNumericUpDown.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            _broadcastIntervalNumericUpDown.Maximum = new decimal(new int[] { 1440, 0, 0, 0 });
            _broadcastIntervalNumericUpDown.Name = "_broadcastIntervalNumericUpDown";
            _broadcastIntervalNumericUpDown.Size = new Size(120, 23);
            _broadcastIntervalNumericUpDown.TabIndex = 7;
            _broadcastIntervalNumericUpDown.ValueChanged += OnSettingChanged;
            // 
            // _broadcastTemplateLabel
            // 
            _broadcastTemplateLabel.AutoSize = true;
            _broadcastTemplateLabel.Dock = DockStyle.Fill;
            _broadcastTemplateLabel.Location = new Point(3, 255);
            _broadcastTemplateLabel.Name = "_broadcastTemplateLabel";
            _broadcastTemplateLabel.Size = new Size(394, 25);
            _broadcastTemplateLabel.TabIndex = 8;
            _broadcastTemplateLabel.Text = "Шаблон сообщения рассылки:";
            _broadcastTemplateLabel.TextAlign = ContentAlignment.BottomLeft;
            // 
            // _broadcastTemplateTextBox
            // 
            _broadcastTemplateTextBox.Dock = DockStyle.Fill;
            _broadcastTemplateTextBox.Location = new Point(3, 283);
            _broadcastTemplateTextBox.Name = "_broadcastTemplateTextBox";
            _broadcastTemplateTextBox.Size = new Size(394, 23);
            _broadcastTemplateTextBox.TabIndex = 9;
            _broadcastTemplateTextBox.TextChanged += OnSettingChanged;
            // 
            // _infoLabel
            // 
            _infoLabel.AutoSize = true;
            _infoLabel.Dock = DockStyle.Fill;
            _infoLabel.ForeColor = SystemColors.GrayText;
            _infoLabel.Location = new Point(3, 310);
            _infoLabel.Name = "_infoLabel";
            _infoLabel.Size = new Size(394, 60);
            _infoLabel.TabIndex = 10;
            _infoLabel.Text = "ℹ️ Автоматическое управление использует EventSub WebSocket для отслеживания статуса стрима. Рассылка будет автоматически запускаться при начале стрима и останавливаться при его завершении.";
            // 
            // AutoBroadcastSettingsControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_mainTableLayoutPanel);
            Name = "AutoBroadcastSettingsControl";
            Size = new Size(400, 360);
            _mainTableLayoutPanel.ResumeLayout(false);
            _mainTableLayoutPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_broadcastIntervalNumericUpDown).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel _mainTableLayoutPanel;
        private CheckBox _autoBroadcastEnabledCheckBox;
        private CheckBox _streamNotificationsEnabledCheckBox;
        private Label _streamStartMessageLabel;
        private TextBox _streamStartMessageTextBox;
        private Label _streamStopMessageLabel;
        private TextBox _streamStopMessageTextBox;
        private Label _broadcastIntervalLabel;
        private NumericUpDown _broadcastIntervalNumericUpDown;
        private Label _broadcastTemplateLabel;
        private TextBox _broadcastTemplateTextBox;
        private Label _infoLabel;
    }
}
