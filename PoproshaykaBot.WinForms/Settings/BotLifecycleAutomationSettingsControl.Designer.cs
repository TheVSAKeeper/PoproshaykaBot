namespace PoproshaykaBot.WinForms.Settings
{
    partial class BotLifecycleAutomationSettingsControl
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
            _autoConnectCheckBox = new CheckBox();
            _autoDisconnectCheckBox = new CheckBox();
            _infoLabel = new Label();
            _mainTableLayoutPanel.SuspendLayout();
            SuspendLayout();
            //
            // _mainTableLayoutPanel
            //
            _mainTableLayoutPanel.ColumnCount = 1;
            _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _mainTableLayoutPanel.Controls.Add(_autoConnectCheckBox, 0, 0);
            _mainTableLayoutPanel.Controls.Add(_autoDisconnectCheckBox, 0, 1);
            _mainTableLayoutPanel.Controls.Add(_infoLabel, 0, 2);
            _mainTableLayoutPanel.Dock = DockStyle.Fill;
            _mainTableLayoutPanel.Location = new Point(0, 0);
            _mainTableLayoutPanel.Name = "_mainTableLayoutPanel";
            _mainTableLayoutPanel.RowCount = 4;
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90F));
            _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _mainTableLayoutPanel.Size = new Size(400, 300);
            _mainTableLayoutPanel.TabIndex = 0;
            //
            // _autoConnectCheckBox
            //
            _autoConnectCheckBox.AutoSize = true;
            _autoConnectCheckBox.Dock = DockStyle.Fill;
            _autoConnectCheckBox.Location = new Point(3, 3);
            _autoConnectCheckBox.Name = "_autoConnectCheckBox";
            _autoConnectCheckBox.Size = new Size(394, 24);
            _autoConnectCheckBox.TabIndex = 0;
            _autoConnectCheckBox.Text = "Автоматически подключать бота при начале трансляции";
            _autoConnectCheckBox.UseVisualStyleBackColor = true;
            _autoConnectCheckBox.CheckedChanged += OnSettingChanged;
            //
            // _autoDisconnectCheckBox
            //
            _autoDisconnectCheckBox.AutoSize = true;
            _autoDisconnectCheckBox.Dock = DockStyle.Fill;
            _autoDisconnectCheckBox.Location = new Point(3, 33);
            _autoDisconnectCheckBox.Name = "_autoDisconnectCheckBox";
            _autoDisconnectCheckBox.Size = new Size(394, 24);
            _autoDisconnectCheckBox.TabIndex = 1;
            _autoDisconnectCheckBox.Text = "Автоматически отключать бота при завершении трансляции";
            _autoDisconnectCheckBox.UseVisualStyleBackColor = true;
            _autoDisconnectCheckBox.CheckedChanged += OnSettingChanged;
            //
            // _infoLabel
            //
            _infoLabel.AutoSize = true;
            _infoLabel.Dock = DockStyle.Fill;
            _infoLabel.ForeColor = SystemColors.GrayText;
            _infoLabel.Location = new Point(3, 63);
            _infoLabel.Name = "_infoLabel";
            _infoLabel.Size = new Size(394, 90);
            _infoLabel.TabIndex = 2;
            _infoLabel.Text = "ℹ️ Стрим-мониторинг работает независимо от подключения бота, поэтому события начала и завершения трансляции принимаются даже когда бот выключен. Если стрим уже идёт на момент запуска приложения, бот подключится автоматически.";
            //
            // BotLifecycleAutomationSettingsControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_mainTableLayoutPanel);
            Name = "BotLifecycleAutomationSettingsControl";
            Size = new Size(400, 300);
            _mainTableLayoutPanel.ResumeLayout(false);
            _mainTableLayoutPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel _mainTableLayoutPanel;
        private CheckBox _autoConnectCheckBox;
        private CheckBox _autoDisconnectCheckBox;
        private Label _infoLabel;
    }
}
