namespace PoproshaykaBot.WinForms.Settings
{
    partial class BasicSettingsControl
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
            _basicTableLayout = new TableLayoutPanel();
            _botUsernameLabel = new Label();
            _botUsernamePanel = new Panel();
            _botUsernameTextBox = new TextBox();
            _botUsernameHintLabel = new Label();
            _channelLabel = new Label();
            _channelPanel = new Panel();
            _channelTextBox = new TextBox();
            _channelResetButton = new Button();
            _basicTableLayout.SuspendLayout();
            _botUsernamePanel.SuspendLayout();
            _channelPanel.SuspendLayout();
            SuspendLayout();
            //
            // _basicTableLayout
            //
            _basicTableLayout.ColumnCount = 2;
            _basicTableLayout.ColumnStyles.Add(new ColumnStyle());
            _basicTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _basicTableLayout.Controls.Add(_botUsernameLabel, 0, 0);
            _basicTableLayout.Controls.Add(_botUsernamePanel, 1, 0);
            _basicTableLayout.Controls.Add(_botUsernameHintLabel, 1, 1);
            _basicTableLayout.Controls.Add(_channelLabel, 0, 2);
            _basicTableLayout.Controls.Add(_channelPanel, 1, 2);
            _basicTableLayout.Dock = DockStyle.Fill;
            _basicTableLayout.Location = new Point(0, 0);
            _basicTableLayout.Name = "_basicTableLayout";
            _basicTableLayout.Padding = new Padding(5);
            _basicTableLayout.RowCount = 4;
            _basicTableLayout.RowStyles.Add(new RowStyle());
            _basicTableLayout.RowStyles.Add(new RowStyle());
            _basicTableLayout.RowStyles.Add(new RowStyle());
            _basicTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _basicTableLayout.Size = new Size(548, 458);
            _basicTableLayout.TabIndex = 0;
            //
            // _botUsernameLabel
            //
            _botUsernameLabel.AutoSize = true;
            _botUsernameLabel.Dock = DockStyle.Fill;
            _botUsernameLabel.Location = new Point(8, 5);
            _botUsernameLabel.Name = "_botUsernameLabel";
            _botUsernameLabel.Size = new Size(140, 35);
            _botUsernameLabel.TabIndex = 0;
            _botUsernameLabel.Text = "Имя пользователя бота:";
            _botUsernameLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _botUsernamePanel
            //
            _botUsernamePanel.Controls.Add(_botUsernameTextBox);
            _botUsernamePanel.Dock = DockStyle.Fill;
            _botUsernamePanel.Location = new Point(154, 8);
            _botUsernamePanel.Name = "_botUsernamePanel";
            _botUsernamePanel.Size = new Size(386, 29);
            _botUsernamePanel.TabIndex = 1;
            //
            // _botUsernameTextBox
            //
            _botUsernameTextBox.Dock = DockStyle.Fill;
            _botUsernameTextBox.Location = new Point(0, 0);
            _botUsernameTextBox.Margin = new Padding(0);
            _botUsernameTextBox.Name = "_botUsernameTextBox";
            _botUsernameTextBox.ReadOnly = true;
            _botUsernameTextBox.Size = new Size(386, 23);
            _botUsernameTextBox.TabIndex = 1;
            _botUsernameTextBox.TabStop = false;
            _botUsernameTextBox.TextChanged += OnSettingChanged;
            //
            // _botUsernameHintLabel
            //
            _botUsernameHintLabel.AutoSize = true;
            _botUsernameHintLabel.Dock = DockStyle.Fill;
            _botUsernameHintLabel.Font = new Font("Segoe UI", 8F, FontStyle.Italic);
            _botUsernameHintLabel.ForeColor = SystemColors.GrayText;
            _botUsernameHintLabel.Location = new Point(154, 40);
            _botUsernameHintLabel.Name = "_botUsernameHintLabel";
            _botUsernameHintLabel.Size = new Size(386, 20);
            _botUsernameHintLabel.TabIndex = 2;
            _botUsernameHintLabel.Text = "Определяется OAuth-токеном бота, отдельно не редактируется.";
            _botUsernameHintLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _channelLabel
            //
            _channelLabel.AutoSize = true;
            _channelLabel.Dock = DockStyle.Fill;
            _channelLabel.Location = new Point(8, 60);
            _channelLabel.Name = "_channelLabel";
            _channelLabel.Size = new Size(140, 35);
            _channelLabel.TabIndex = 3;
            _channelLabel.Text = "Канал:";
            _channelLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _channelPanel
            //
            _channelPanel.Controls.Add(_channelTextBox);
            _channelPanel.Controls.Add(_channelResetButton);
            _channelPanel.Dock = DockStyle.Fill;
            _channelPanel.Location = new Point(154, 63);
            _channelPanel.Name = "_channelPanel";
            _channelPanel.Size = new Size(386, 29);
            _channelPanel.TabIndex = 4;
            //
            // _channelTextBox
            //
            _channelTextBox.Dock = DockStyle.Fill;
            _channelTextBox.Location = new Point(0, 0);
            _channelTextBox.Margin = new Padding(0, 0, 3, 0);
            _channelTextBox.Name = "_channelTextBox";
            _channelTextBox.Size = new Size(361, 23);
            _channelTextBox.TabIndex = 4;
            _channelTextBox.TextChanged += OnSettingChanged;
            //
            // _channelResetButton
            //
            _channelResetButton.Dock = DockStyle.Right;
            _channelResetButton.Location = new Point(361, 0);
            _channelResetButton.Name = "_channelResetButton";
            _channelResetButton.Size = new Size(25, 29);
            _channelResetButton.TabIndex = 5;
            _channelResetButton.Text = "↺";
            _channelResetButton.UseVisualStyleBackColor = true;
            _channelResetButton.Click += OnChannelResetButtonClicked;
            //
            // BasicSettingsControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_basicTableLayout);
            Name = "BasicSettingsControl";
            Size = new Size(548, 458);
            _basicTableLayout.ResumeLayout(false);
            _basicTableLayout.PerformLayout();
            _botUsernamePanel.ResumeLayout(false);
            _botUsernamePanel.PerformLayout();
            _channelPanel.ResumeLayout(false);
            _channelPanel.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel _basicTableLayout;
        private Panel _botUsernamePanel;
        private Panel _channelPanel;
        private Label _botUsernameLabel;
        private TextBox _botUsernameTextBox;
        private Label _botUsernameHintLabel;
        private Label _channelLabel;
        private TextBox _channelTextBox;
        private Button _channelResetButton;
    }
}
