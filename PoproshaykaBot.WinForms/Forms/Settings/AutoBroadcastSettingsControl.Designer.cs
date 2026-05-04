namespace PoproshaykaBot.WinForms.Forms.Settings
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
            _mainTableLayout = new TableLayoutPanel();
            _autoControlGroupBox = new GroupBox();
            _autoControlTableLayout = new TableLayoutPanel();
            _autoBroadcastEnabledCheckBox = new CheckBox();
            _infoLabel = new Label();
            _notificationsGroupBox = new GroupBox();
            _notificationsTableLayout = new TableLayoutPanel();
            _streamNotificationsEnabledCheckBox = new CheckBox();
            _streamStartMessageLabel = new Label();
            _streamStartMessageTextBox = new TextBox();
            _streamStopMessageLabel = new Label();
            _streamStopMessageTextBox = new TextBox();
            _streamEndStatsMessageLabel = new Label();
            _streamEndStatsMessageTextBox = new TextBox();
            _broadcastGroupBox = new GroupBox();
            _broadcastTableLayout = new TableLayoutPanel();
            _broadcastIntervalLabel = new Label();
            _broadcastIntervalNumericUpDown = new NumericUpDown();
            _broadcastTemplateLabel = new Label();
            _broadcastTemplateTextBox = new TextBox();
            _mainTableLayout.SuspendLayout();
            _autoControlGroupBox.SuspendLayout();
            _autoControlTableLayout.SuspendLayout();
            _notificationsGroupBox.SuspendLayout();
            _notificationsTableLayout.SuspendLayout();
            _broadcastGroupBox.SuspendLayout();
            _broadcastTableLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_broadcastIntervalNumericUpDown).BeginInit();
            SuspendLayout();
            //
            // _mainTableLayout
            //
            _mainTableLayout.ColumnCount = 1;
            _mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _mainTableLayout.Controls.Add(_autoControlGroupBox, 0, 0);
            _mainTableLayout.Controls.Add(_notificationsGroupBox, 0, 1);
            _mainTableLayout.Controls.Add(_broadcastGroupBox, 0, 2);
            _mainTableLayout.Dock = DockStyle.Fill;
            _mainTableLayout.Location = new Point(0, 0);
            _mainTableLayout.Name = "_mainTableLayout";
            _mainTableLayout.RowCount = 4;
            _mainTableLayout.RowStyles.Add(new RowStyle());
            _mainTableLayout.RowStyles.Add(new RowStyle());
            _mainTableLayout.RowStyles.Add(new RowStyle());
            _mainTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _mainTableLayout.Size = new Size(593, 529);
            _mainTableLayout.TabIndex = 0;
            //
            // _autoControlGroupBox
            //
            _autoControlGroupBox.AutoSize = true;
            _autoControlGroupBox.Controls.Add(_autoControlTableLayout);
            _autoControlGroupBox.Dock = DockStyle.Fill;
            _autoControlGroupBox.Location = new Point(3, 3);
            _autoControlGroupBox.Name = "_autoControlGroupBox";
            _autoControlGroupBox.Padding = new Padding(10);
            _autoControlGroupBox.TabIndex = 0;
            _autoControlGroupBox.TabStop = false;
            _autoControlGroupBox.Text = "Автоматический режим";
            //
            // _autoControlTableLayout
            //
            _autoControlTableLayout.AutoSize = true;
            _autoControlTableLayout.ColumnCount = 1;
            _autoControlTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _autoControlTableLayout.Controls.Add(_autoBroadcastEnabledCheckBox, 0, 0);
            _autoControlTableLayout.Controls.Add(_infoLabel, 0, 1);
            _autoControlTableLayout.Dock = DockStyle.Fill;
            _autoControlTableLayout.Name = "_autoControlTableLayout";
            _autoControlTableLayout.RowCount = 2;
            _autoControlTableLayout.RowStyles.Add(new RowStyle());
            _autoControlTableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
            _autoControlTableLayout.TabIndex = 0;
            //
            // _autoBroadcastEnabledCheckBox
            //
            _autoBroadcastEnabledCheckBox.AutoSize = true;
            _autoBroadcastEnabledCheckBox.Dock = DockStyle.Fill;
            _autoBroadcastEnabledCheckBox.Name = "_autoBroadcastEnabledCheckBox";
            _autoBroadcastEnabledCheckBox.TabIndex = 0;
            _autoBroadcastEnabledCheckBox.Text = "Включить автоматическое управление рассылкой";
            _autoBroadcastEnabledCheckBox.UseVisualStyleBackColor = true;
            _autoBroadcastEnabledCheckBox.CheckedChanged += OnSettingChanged;
            //
            // _infoLabel
            //
            _infoLabel.AutoSize = false;
            _infoLabel.Dock = DockStyle.Fill;
            _infoLabel.ForeColor = SystemColors.GrayText;
            _infoLabel.Margin = new Padding(3, 6, 3, 0);
            _infoLabel.Name = "_infoLabel";
            _infoLabel.TabIndex = 1;
            _infoLabel.Text = "ℹ️ Автоматическое управление использует EventSub WebSocket для отслеживания статуса стрима. Рассылка будет автоматически запускаться при начале стрима и останавливаться при его завершении.";
            //
            // _notificationsGroupBox
            //
            _notificationsGroupBox.AutoSize = true;
            _notificationsGroupBox.Controls.Add(_notificationsTableLayout);
            _notificationsGroupBox.Dock = DockStyle.Fill;
            _notificationsGroupBox.Name = "_notificationsGroupBox";
            _notificationsGroupBox.Padding = new Padding(10);
            _notificationsGroupBox.TabIndex = 1;
            _notificationsGroupBox.TabStop = false;
            _notificationsGroupBox.Text = "Уведомления о статусе стрима";
            //
            // _notificationsTableLayout
            //
            _notificationsTableLayout.AutoSize = true;
            _notificationsTableLayout.ColumnCount = 1;
            _notificationsTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _notificationsTableLayout.Controls.Add(_streamNotificationsEnabledCheckBox, 0, 0);
            _notificationsTableLayout.Controls.Add(_streamStartMessageLabel, 0, 1);
            _notificationsTableLayout.Controls.Add(_streamStartMessageTextBox, 0, 2);
            _notificationsTableLayout.Controls.Add(_streamStopMessageLabel, 0, 3);
            _notificationsTableLayout.Controls.Add(_streamStopMessageTextBox, 0, 4);
            _notificationsTableLayout.Controls.Add(_streamEndStatsMessageLabel, 0, 5);
            _notificationsTableLayout.Controls.Add(_streamEndStatsMessageTextBox, 0, 6);
            _notificationsTableLayout.Dock = DockStyle.Fill;
            _notificationsTableLayout.Name = "_notificationsTableLayout";
            _notificationsTableLayout.RowCount = 7;
            _notificationsTableLayout.RowStyles.Add(new RowStyle());
            _notificationsTableLayout.RowStyles.Add(new RowStyle());
            _notificationsTableLayout.RowStyles.Add(new RowStyle());
            _notificationsTableLayout.RowStyles.Add(new RowStyle());
            _notificationsTableLayout.RowStyles.Add(new RowStyle());
            _notificationsTableLayout.RowStyles.Add(new RowStyle());
            _notificationsTableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            _notificationsTableLayout.TabIndex = 0;
            //
            // _streamNotificationsEnabledCheckBox
            //
            _streamNotificationsEnabledCheckBox.AutoSize = true;
            _streamNotificationsEnabledCheckBox.Dock = DockStyle.Fill;
            _streamNotificationsEnabledCheckBox.Name = "_streamNotificationsEnabledCheckBox";
            _streamNotificationsEnabledCheckBox.TabIndex = 0;
            _streamNotificationsEnabledCheckBox.Text = "Отправлять уведомления о статусе стрима в чат";
            _streamNotificationsEnabledCheckBox.UseVisualStyleBackColor = true;
            _streamNotificationsEnabledCheckBox.CheckedChanged += OnSettingChanged;
            //
            // _streamStartMessageLabel
            //
            _streamStartMessageLabel.AutoSize = true;
            _streamStartMessageLabel.Dock = DockStyle.Fill;
            _streamStartMessageLabel.Margin = new Padding(3, 8, 3, 0);
            _streamStartMessageLabel.Name = "_streamStartMessageLabel";
            _streamStartMessageLabel.TabIndex = 1;
            _streamStartMessageLabel.Text = "Сообщение при запуске стрима:";
            //
            // _streamStartMessageTextBox
            //
            _streamStartMessageTextBox.Dock = DockStyle.Fill;
            _streamStartMessageTextBox.Name = "_streamStartMessageTextBox";
            _streamStartMessageTextBox.TabIndex = 2;
            _streamStartMessageTextBox.TextChanged += OnSettingChanged;
            //
            // _streamStopMessageLabel
            //
            _streamStopMessageLabel.AutoSize = true;
            _streamStopMessageLabel.Dock = DockStyle.Fill;
            _streamStopMessageLabel.Margin = new Padding(3, 8, 3, 0);
            _streamStopMessageLabel.Name = "_streamStopMessageLabel";
            _streamStopMessageLabel.TabIndex = 3;
            _streamStopMessageLabel.Text = "Сообщение при остановке стрима:";
            //
            // _streamStopMessageTextBox
            //
            _streamStopMessageTextBox.Dock = DockStyle.Fill;
            _streamStopMessageTextBox.Name = "_streamStopMessageTextBox";
            _streamStopMessageTextBox.TabIndex = 4;
            _streamStopMessageTextBox.TextChanged += OnSettingChanged;
            //
            // _streamEndStatsMessageLabel
            //
            _streamEndStatsMessageLabel.AutoSize = true;
            _streamEndStatsMessageLabel.Dock = DockStyle.Fill;
            _streamEndStatsMessageLabel.Margin = new Padding(3, 8, 3, 0);
            _streamEndStatsMessageLabel.Name = "_streamEndStatsMessageLabel";
            _streamEndStatsMessageLabel.TabIndex = 5;
            _streamEndStatsMessageLabel.Text = "Сообщение со статистикой по завершении ({duration}, {messages}, {chatters}, {peakViewers}, {avgViewers}, {title}, {game}, {channel}):";
            //
            // _streamEndStatsMessageTextBox
            //
            _streamEndStatsMessageTextBox.Dock = DockStyle.Fill;
            _streamEndStatsMessageTextBox.Multiline = true;
            _streamEndStatsMessageTextBox.Name = "_streamEndStatsMessageTextBox";
            _streamEndStatsMessageTextBox.ScrollBars = ScrollBars.Vertical;
            _streamEndStatsMessageTextBox.TabIndex = 6;
            _streamEndStatsMessageTextBox.TextChanged += OnSettingChanged;
            //
            // _broadcastGroupBox
            //
            _broadcastGroupBox.AutoSize = true;
            _broadcastGroupBox.Controls.Add(_broadcastTableLayout);
            _broadcastGroupBox.Dock = DockStyle.Fill;
            _broadcastGroupBox.Name = "_broadcastGroupBox";
            _broadcastGroupBox.Padding = new Padding(10);
            _broadcastGroupBox.TabIndex = 2;
            _broadcastGroupBox.TabStop = false;
            _broadcastGroupBox.Text = "Периодическая рассылка";
            //
            // _broadcastTableLayout
            //
            _broadcastTableLayout.AutoSize = true;
            _broadcastTableLayout.ColumnCount = 1;
            _broadcastTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _broadcastTableLayout.Controls.Add(_broadcastIntervalLabel, 0, 0);
            _broadcastTableLayout.Controls.Add(_broadcastIntervalNumericUpDown, 0, 1);
            _broadcastTableLayout.Controls.Add(_broadcastTemplateLabel, 0, 2);
            _broadcastTableLayout.Controls.Add(_broadcastTemplateTextBox, 0, 3);
            _broadcastTableLayout.Dock = DockStyle.Fill;
            _broadcastTableLayout.Name = "_broadcastTableLayout";
            _broadcastTableLayout.RowCount = 4;
            _broadcastTableLayout.RowStyles.Add(new RowStyle());
            _broadcastTableLayout.RowStyles.Add(new RowStyle());
            _broadcastTableLayout.RowStyles.Add(new RowStyle());
            _broadcastTableLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            _broadcastTableLayout.TabIndex = 0;
            //
            // _broadcastIntervalLabel
            //
            _broadcastIntervalLabel.AutoSize = true;
            _broadcastIntervalLabel.Dock = DockStyle.Fill;
            _broadcastIntervalLabel.Name = "_broadcastIntervalLabel";
            _broadcastIntervalLabel.TabIndex = 0;
            _broadcastIntervalLabel.Text = "Интервал рассылки (минуты):";
            //
            // _broadcastIntervalNumericUpDown
            //
            _broadcastIntervalNumericUpDown.Dock = DockStyle.Left;
            _broadcastIntervalNumericUpDown.Maximum = new decimal(new int[] { 1440, 0, 0, 0 });
            _broadcastIntervalNumericUpDown.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            _broadcastIntervalNumericUpDown.Name = "_broadcastIntervalNumericUpDown";
            _broadcastIntervalNumericUpDown.Size = new Size(120, 23);
            _broadcastIntervalNumericUpDown.TabIndex = 1;
            _broadcastIntervalNumericUpDown.Value = new decimal(new int[] { 1, 0, 0, 0 });
            _broadcastIntervalNumericUpDown.ValueChanged += OnSettingChanged;
            //
            // _broadcastTemplateLabel
            //
            _broadcastTemplateLabel.AutoSize = true;
            _broadcastTemplateLabel.Dock = DockStyle.Fill;
            _broadcastTemplateLabel.Margin = new Padding(3, 8, 3, 0);
            _broadcastTemplateLabel.Name = "_broadcastTemplateLabel";
            _broadcastTemplateLabel.TabIndex = 2;
            _broadcastTemplateLabel.Text = "Шаблон сообщения рассылки:";
            //
            // _broadcastTemplateTextBox
            //
            _broadcastTemplateTextBox.Dock = DockStyle.Fill;
            _broadcastTemplateTextBox.Multiline = true;
            _broadcastTemplateTextBox.Name = "_broadcastTemplateTextBox";
            _broadcastTemplateTextBox.ScrollBars = ScrollBars.Vertical;
            _broadcastTemplateTextBox.TabIndex = 3;
            _broadcastTemplateTextBox.TextChanged += OnSettingChanged;
            //
            // AutoBroadcastSettingsControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_mainTableLayout);
            Name = "AutoBroadcastSettingsControl";
            Size = new Size(593, 529);
            _mainTableLayout.ResumeLayout(false);
            _mainTableLayout.PerformLayout();
            _autoControlGroupBox.ResumeLayout(false);
            _autoControlGroupBox.PerformLayout();
            _autoControlTableLayout.ResumeLayout(false);
            _autoControlTableLayout.PerformLayout();
            _notificationsGroupBox.ResumeLayout(false);
            _notificationsGroupBox.PerformLayout();
            _notificationsTableLayout.ResumeLayout(false);
            _notificationsTableLayout.PerformLayout();
            _broadcastGroupBox.ResumeLayout(false);
            _broadcastGroupBox.PerformLayout();
            _broadcastTableLayout.ResumeLayout(false);
            _broadcastTableLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_broadcastIntervalNumericUpDown).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel _mainTableLayout;
        private GroupBox _autoControlGroupBox;
        private TableLayoutPanel _autoControlTableLayout;
        private CheckBox _autoBroadcastEnabledCheckBox;
        private Label _infoLabel;
        private GroupBox _notificationsGroupBox;
        private TableLayoutPanel _notificationsTableLayout;
        private CheckBox _streamNotificationsEnabledCheckBox;
        private Label _streamStartMessageLabel;
        private TextBox _streamStartMessageTextBox;
        private Label _streamStopMessageLabel;
        private TextBox _streamStopMessageTextBox;
        private Label _streamEndStatsMessageLabel;
        private TextBox _streamEndStatsMessageTextBox;
        private GroupBox _broadcastGroupBox;
        private TableLayoutPanel _broadcastTableLayout;
        private Label _broadcastIntervalLabel;
        private NumericUpDown _broadcastIntervalNumericUpDown;
        private Label _broadcastTemplateLabel;
        private TextBox _broadcastTemplateTextBox;
    }
}
