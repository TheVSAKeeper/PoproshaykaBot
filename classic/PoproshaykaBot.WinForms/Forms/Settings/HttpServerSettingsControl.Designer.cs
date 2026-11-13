namespace PoproshaykaBot.WinForms.Forms.Settings
{
    partial class HttpServerSettingsControl
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
            _httpServerTableLayout = new TableLayoutPanel();
            _portFlow = new FlowLayoutPanel();
            _httpServerPortLabel = new Label();
            _httpServerPortValueLabel = new Label();
            _httpServerPortHintLabel = new Label();
            _statusFlow = new FlowLayoutPanel();
            _serverStatusLabel = new Label();
            _obsUrlLabel = new Label();
            _buttonsFlow = new FlowLayoutPanel();
            _copyUrlButton = new Button();
            _restartServerButton = new Button();
            _httpServerTableLayout.SuspendLayout();
            _portFlow.SuspendLayout();
            _statusFlow.SuspendLayout();
            _buttonsFlow.SuspendLayout();
            SuspendLayout();
            // 
            // _httpServerTableLayout
            // 
            _httpServerTableLayout.ColumnCount = 1;
            _httpServerTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _httpServerTableLayout.Controls.Add(_portFlow, 0, 0);
            _httpServerTableLayout.Controls.Add(_statusFlow, 0, 1);
            _httpServerTableLayout.Controls.Add(_buttonsFlow, 0, 2);
            _httpServerTableLayout.Dock = DockStyle.Fill;
            _httpServerTableLayout.Location = new Point(0, 0);
            _httpServerTableLayout.Name = "_httpServerTableLayout";
            _httpServerTableLayout.RowCount = 3;
            _httpServerTableLayout.RowStyles.Add(new RowStyle());
            _httpServerTableLayout.RowStyles.Add(new RowStyle());
            _httpServerTableLayout.RowStyles.Add(new RowStyle());
            _httpServerTableLayout.Size = new Size(0, 0);
            _httpServerTableLayout.TabIndex = 0;
            // 
            // _portFlow
            // 
            _portFlow.AutoSize = true;
            _portFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _portFlow.Controls.Add(_httpServerPortLabel);
            _portFlow.Controls.Add(_httpServerPortValueLabel);
            _portFlow.Controls.Add(_httpServerPortHintLabel);
            _portFlow.Dock = DockStyle.Fill;
            _portFlow.Location = new Point(0, 0);
            _portFlow.Margin = new Padding(0);
            _portFlow.Name = "_portFlow";
            _portFlow.Size = new Size(1, 27);
            _portFlow.TabIndex = 0;
            _portFlow.WrapContents = false;
            // 
            // _httpServerPortLabel
            // 
            _httpServerPortLabel.AutoSize = true;
            _httpServerPortLabel.Location = new Point(0, 6);
            _httpServerPortLabel.Margin = new Padding(0, 6, 6, 6);
            _httpServerPortLabel.Name = "_httpServerPortLabel";
            _httpServerPortLabel.Size = new Size(85, 15);
            _httpServerPortLabel.TabIndex = 0;
            _httpServerPortLabel.Text = "Порт сервера:";
            // 
            // _httpServerPortValueLabel
            // 
            _httpServerPortValueLabel.AutoSize = true;
            _httpServerPortValueLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _httpServerPortValueLabel.Location = new Point(91, 6);
            _httpServerPortValueLabel.Margin = new Padding(0, 6, 6, 6);
            _httpServerPortValueLabel.Name = "_httpServerPortValueLabel";
            _httpServerPortValueLabel.Size = new Size(35, 15);
            _httpServerPortValueLabel.TabIndex = 1;
            _httpServerPortValueLabel.Text = "8080";
            // 
            // _httpServerPortHintLabel
            // 
            _httpServerPortHintLabel.AutoSize = true;
            _httpServerPortHintLabel.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            _httpServerPortHintLabel.ForeColor = SystemColors.GrayText;
            _httpServerPortHintLabel.Location = new Point(132, 7);
            _httpServerPortHintLabel.Margin = new Padding(0, 7, 0, 6);
            _httpServerPortHintLabel.Name = "_httpServerPortHintLabel";
            _httpServerPortHintLabel.Size = new Size(207, 13);
            _httpServerPortHintLabel.TabIndex = 2;
            _httpServerPortHintLabel.Text = "(синхронизирован с RedirectUri в OAuth)";
            // 
            // _statusFlow
            // 
            _statusFlow.AutoSize = true;
            _statusFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _statusFlow.Controls.Add(_serverStatusLabel);
            _statusFlow.Controls.Add(_obsUrlLabel);
            _statusFlow.Dock = DockStyle.Fill;
            _statusFlow.FlowDirection = FlowDirection.TopDown;
            _statusFlow.Location = new Point(0, 27);
            _statusFlow.Margin = new Padding(0);
            _statusFlow.Name = "_statusFlow";
            _statusFlow.Size = new Size(1, 44);
            _statusFlow.TabIndex = 1;
            _statusFlow.WrapContents = false;
            // 
            // _serverStatusLabel
            // 
            _serverStatusLabel.AutoSize = true;
            _serverStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _serverStatusLabel.Location = new Point(0, 6);
            _serverStatusLabel.Margin = new Padding(0, 6, 0, 2);
            _serverStatusLabel.Name = "_serverStatusLabel";
            _serverStatusLabel.Size = new Size(142, 15);
            _serverStatusLabel.TabIndex = 0;
            _serverStatusLabel.Text = "Статус сервера: ○ Готов";
            // 
            // _obsUrlLabel
            // 
            _obsUrlLabel.AutoSize = true;
            _obsUrlLabel.Location = new Point(0, 23);
            _obsUrlLabel.Margin = new Padding(0, 0, 0, 6);
            _obsUrlLabel.Name = "_obsUrlLabel";
            _obsUrlLabel.Size = new Size(219, 15);
            _obsUrlLabel.TabIndex = 1;
            _obsUrlLabel.Text = "URL для OBS: http://localhost:8080/chat";
            // 
            // _buttonsFlow
            // 
            _buttonsFlow.AutoSize = true;
            _buttonsFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _buttonsFlow.Controls.Add(_copyUrlButton);
            _buttonsFlow.Controls.Add(_restartServerButton);
            _buttonsFlow.Dock = DockStyle.Fill;
            _buttonsFlow.Location = new Point(0, 71);
            _buttonsFlow.Margin = new Padding(0);
            _buttonsFlow.Name = "_buttonsFlow";
            _buttonsFlow.Size = new Size(1, 36);
            _buttonsFlow.TabIndex = 2;
            _buttonsFlow.WrapContents = false;
            // 
            // _copyUrlButton
            // 
            _copyUrlButton.AutoSize = true;
            _copyUrlButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _copyUrlButton.Location = new Point(0, 4);
            _copyUrlButton.Margin = new Padding(0, 4, 6, 4);
            _copyUrlButton.MinimumSize = new Size(120, 28);
            _copyUrlButton.Name = "_copyUrlButton";
            _copyUrlButton.Size = new Size(120, 28);
            _copyUrlButton.TabIndex = 0;
            _copyUrlButton.Text = "Копировать URL";
            _copyUrlButton.UseVisualStyleBackColor = true;
            _copyUrlButton.Click += OnCopyUrlButtonClicked;
            // 
            // _restartServerButton
            // 
            _restartServerButton.AutoSize = true;
            _restartServerButton.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _restartServerButton.Location = new Point(126, 4);
            _restartServerButton.Margin = new Padding(0, 4, 0, 4);
            _restartServerButton.MinimumSize = new Size(160, 28);
            _restartServerButton.Name = "_restartServerButton";
            _restartServerButton.Size = new Size(160, 28);
            _restartServerButton.TabIndex = 1;
            _restartServerButton.Text = "Перезапустить сервер";
            _restartServerButton.UseVisualStyleBackColor = true;
            _restartServerButton.Click += OnRestartServerButtonClicked;
            // 
            // HttpServerSettingsControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Controls.Add(_httpServerTableLayout);
            Name = "HttpServerSettingsControl";
            Size = new Size(300, 300);
            _httpServerTableLayout.ResumeLayout(false);
            _httpServerTableLayout.PerformLayout();
            _portFlow.ResumeLayout(false);
            _portFlow.PerformLayout();
            _statusFlow.ResumeLayout(false);
            _statusFlow.PerformLayout();
            _buttonsFlow.ResumeLayout(false);
            _buttonsFlow.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel _httpServerTableLayout;
        private FlowLayoutPanel _portFlow;
        private Label _httpServerPortLabel;
        private Label _httpServerPortValueLabel;
        private Label _httpServerPortHintLabel;
        private FlowLayoutPanel _statusFlow;
        private Label _serverStatusLabel;
        private Label _obsUrlLabel;
        private FlowLayoutPanel _buttonsFlow;
        private Button _copyUrlButton;
        private Button _restartServerButton;
    }
}
