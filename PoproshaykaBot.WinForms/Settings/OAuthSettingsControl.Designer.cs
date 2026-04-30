namespace PoproshaykaBot.WinForms.Settings
{
    partial class OAuthSettingsControl
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
            _oauthTableLayout = new TableLayoutPanel();
            _oauthCredentialsSection = new TableLayoutPanel();
            _clientIdLabel = new Label();
            _clientIdPanel = new Panel();
            _clientIdTextBox = new TextBox();
            _clientIdResetButton = new Button();
            _clientSecretLabel = new Label();
            _clientSecretPanel = new Panel();
            _clientSecretTextBox = new TextBox();
            _clientSecretResetButton = new Button();
            _redirectUriLabel = new Label();
            _redirectUriPanel = new Panel();
            _redirectUriTextBox = new TextBox();
            _redirectUriEditButton = new Button();
            _redirectUriResetButton = new Button();
            _accountsTabControl = new TabControl();
            _botTabPage = new TabPage();
            _botAccountSection = new OAuthAccountSection();
            _broadcasterTabPage = new TabPage();
            _broadcasterAccountSection = new OAuthAccountSection();
            _oauthInfoLabel = new Label();
            _oauthTableLayout.SuspendLayout();
            _oauthCredentialsSection.SuspendLayout();
            _clientIdPanel.SuspendLayout();
            _clientSecretPanel.SuspendLayout();
            _redirectUriPanel.SuspendLayout();
            _accountsTabControl.SuspendLayout();
            _botTabPage.SuspendLayout();
            _broadcasterTabPage.SuspendLayout();
            SuspendLayout();
            //
            // _oauthTableLayout
            //
            _oauthTableLayout.ColumnCount = 1;
            _oauthTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _oauthTableLayout.Controls.Add(_oauthCredentialsSection, 0, 0);
            _oauthTableLayout.Controls.Add(_accountsTabControl, 0, 1);
            _oauthTableLayout.Controls.Add(_oauthInfoLabel, 0, 2);
            _oauthTableLayout.Dock = DockStyle.Fill;
            _oauthTableLayout.Location = new Point(0, 0);
            _oauthTableLayout.Name = "_oauthTableLayout";
            _oauthTableLayout.Padding = new Padding(5);
            _oauthTableLayout.RowCount = 3;
            _oauthTableLayout.RowStyles.Add(new RowStyle());
            _oauthTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _oauthTableLayout.RowStyles.Add(new RowStyle());
            _oauthTableLayout.Size = new Size(560, 480);
            _oauthTableLayout.TabIndex = 0;
            //
            // _oauthCredentialsSection
            //
            _oauthCredentialsSection.AutoSize = true;
            _oauthCredentialsSection.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _oauthCredentialsSection.ColumnCount = 2;
            _oauthCredentialsSection.ColumnStyles.Add(new ColumnStyle());
            _oauthCredentialsSection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _oauthCredentialsSection.Controls.Add(_clientIdLabel, 0, 0);
            _oauthCredentialsSection.Controls.Add(_clientIdPanel, 1, 0);
            _oauthCredentialsSection.Controls.Add(_clientSecretLabel, 0, 1);
            _oauthCredentialsSection.Controls.Add(_clientSecretPanel, 1, 1);
            _oauthCredentialsSection.Controls.Add(_redirectUriLabel, 0, 2);
            _oauthCredentialsSection.Controls.Add(_redirectUriPanel, 1, 2);
            _oauthCredentialsSection.Dock = DockStyle.Fill;
            _oauthCredentialsSection.Location = new Point(5, 5);
            _oauthCredentialsSection.Margin = new Padding(0, 0, 0, 10);
            _oauthCredentialsSection.Name = "_oauthCredentialsSection";
            _oauthCredentialsSection.RowCount = 3;
            _oauthCredentialsSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            _oauthCredentialsSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            _oauthCredentialsSection.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            _oauthCredentialsSection.Size = new Size(550, 105);
            _oauthCredentialsSection.TabIndex = 0;
            //
            // _clientIdLabel
            //
            _clientIdLabel.AutoSize = true;
            _clientIdLabel.Dock = DockStyle.Fill;
            _clientIdLabel.Location = new Point(3, 0);
            _clientIdLabel.Name = "_clientIdLabel";
            _clientIdLabel.Size = new Size(76, 35);
            _clientIdLabel.TabIndex = 0;
            _clientIdLabel.Text = "Client ID:";
            _clientIdLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _clientIdPanel
            //
            _clientIdPanel.Controls.Add(_clientIdTextBox);
            _clientIdPanel.Controls.Add(_clientIdResetButton);
            _clientIdPanel.Dock = DockStyle.Fill;
            _clientIdPanel.Location = new Point(85, 3);
            _clientIdPanel.Name = "_clientIdPanel";
            _clientIdPanel.Size = new Size(462, 29);
            _clientIdPanel.TabIndex = 1;
            //
            // _clientIdTextBox
            //
            _clientIdTextBox.Dock = DockStyle.Fill;
            _clientIdTextBox.Location = new Point(0, 0);
            _clientIdTextBox.Margin = new Padding(0, 0, 3, 0);
            _clientIdTextBox.Name = "_clientIdTextBox";
            _clientIdTextBox.Size = new Size(437, 23);
            _clientIdTextBox.TabIndex = 1;
            _clientIdTextBox.UseSystemPasswordChar = true;
            _clientIdTextBox.TextChanged += OnSettingChanged;
            //
            // _clientIdResetButton
            //
            _clientIdResetButton.Dock = DockStyle.Right;
            _clientIdResetButton.Location = new Point(437, 0);
            _clientIdResetButton.Name = "_clientIdResetButton";
            _clientIdResetButton.Size = new Size(25, 29);
            _clientIdResetButton.TabIndex = 2;
            _clientIdResetButton.Text = "↺";
            _clientIdResetButton.UseVisualStyleBackColor = true;
            _clientIdResetButton.Click += OnClientIdResetButtonClicked;
            //
            // _clientSecretLabel
            //
            _clientSecretLabel.AutoSize = true;
            _clientSecretLabel.Dock = DockStyle.Fill;
            _clientSecretLabel.Location = new Point(3, 35);
            _clientSecretLabel.Name = "_clientSecretLabel";
            _clientSecretLabel.Size = new Size(76, 35);
            _clientSecretLabel.TabIndex = 3;
            _clientSecretLabel.Text = "Client Secret:";
            _clientSecretLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _clientSecretPanel
            //
            _clientSecretPanel.Controls.Add(_clientSecretTextBox);
            _clientSecretPanel.Controls.Add(_clientSecretResetButton);
            _clientSecretPanel.Dock = DockStyle.Fill;
            _clientSecretPanel.Location = new Point(85, 38);
            _clientSecretPanel.Name = "_clientSecretPanel";
            _clientSecretPanel.Size = new Size(462, 29);
            _clientSecretPanel.TabIndex = 4;
            //
            // _clientSecretTextBox
            //
            _clientSecretTextBox.Dock = DockStyle.Fill;
            _clientSecretTextBox.Location = new Point(0, 0);
            _clientSecretTextBox.Margin = new Padding(0, 0, 3, 0);
            _clientSecretTextBox.Name = "_clientSecretTextBox";
            _clientSecretTextBox.Size = new Size(437, 23);
            _clientSecretTextBox.TabIndex = 4;
            _clientSecretTextBox.UseSystemPasswordChar = true;
            _clientSecretTextBox.TextChanged += OnSettingChanged;
            //
            // _clientSecretResetButton
            //
            _clientSecretResetButton.Dock = DockStyle.Right;
            _clientSecretResetButton.Location = new Point(437, 0);
            _clientSecretResetButton.Name = "_clientSecretResetButton";
            _clientSecretResetButton.Size = new Size(25, 29);
            _clientSecretResetButton.TabIndex = 5;
            _clientSecretResetButton.Text = "↺";
            _clientSecretResetButton.UseVisualStyleBackColor = true;
            _clientSecretResetButton.Click += OnClientSecretResetButtonClicked;
            //
            // _redirectUriLabel
            //
            _redirectUriLabel.AutoSize = true;
            _redirectUriLabel.Dock = DockStyle.Fill;
            _redirectUriLabel.Location = new Point(3, 70);
            _redirectUriLabel.Name = "_redirectUriLabel";
            _redirectUriLabel.Size = new Size(76, 35);
            _redirectUriLabel.TabIndex = 6;
            _redirectUriLabel.Text = "Redirect URI:";
            _redirectUriLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _redirectUriPanel
            //
            _redirectUriPanel.Controls.Add(_redirectUriTextBox);
            _redirectUriPanel.Controls.Add(_redirectUriEditButton);
            _redirectUriPanel.Controls.Add(_redirectUriResetButton);
            _redirectUriPanel.Dock = DockStyle.Fill;
            _redirectUriPanel.Location = new Point(85, 73);
            _redirectUriPanel.Name = "_redirectUriPanel";
            _redirectUriPanel.Size = new Size(462, 29);
            _redirectUriPanel.TabIndex = 7;
            //
            // _redirectUriTextBox
            //
            _redirectUriTextBox.Dock = DockStyle.Fill;
            _redirectUriTextBox.Location = new Point(0, 0);
            _redirectUriTextBox.Margin = new Padding(0, 0, 3, 0);
            _redirectUriTextBox.Name = "_redirectUriTextBox";
            _redirectUriTextBox.ReadOnly = true;
            _redirectUriTextBox.Size = new Size(407, 23);
            _redirectUriTextBox.TabIndex = 7;
            _redirectUriTextBox.TextChanged += OnSettingChanged;
            //
            // _redirectUriEditButton
            //
            _redirectUriEditButton.Dock = DockStyle.Right;
            _redirectUriEditButton.Location = new Point(407, 0);
            _redirectUriEditButton.Name = "_redirectUriEditButton";
            _redirectUriEditButton.Size = new Size(30, 29);
            _redirectUriEditButton.TabIndex = 8;
            _redirectUriEditButton.Text = "✏";
            _redirectUriEditButton.UseVisualStyleBackColor = true;
            _redirectUriEditButton.Click += OnRedirectUriEditButtonClicked;
            //
            // _redirectUriResetButton
            //
            _redirectUriResetButton.Dock = DockStyle.Right;
            _redirectUriResetButton.Location = new Point(437, 0);
            _redirectUriResetButton.Name = "_redirectUriResetButton";
            _redirectUriResetButton.Size = new Size(25, 29);
            _redirectUriResetButton.TabIndex = 9;
            _redirectUriResetButton.Text = "↺";
            _redirectUriResetButton.UseVisualStyleBackColor = true;
            _redirectUriResetButton.Click += OnRedirectUriResetButtonClicked;
            //
            // _accountsTabControl
            //
            _accountsTabControl.Controls.Add(_botTabPage);
            _accountsTabControl.Controls.Add(_broadcasterTabPage);
            _accountsTabControl.Dock = DockStyle.Fill;
            _accountsTabControl.Location = new Point(8, 118);
            _accountsTabControl.Margin = new Padding(3, 3, 3, 10);
            _accountsTabControl.Name = "_accountsTabControl";
            _accountsTabControl.SelectedIndex = 0;
            _accountsTabControl.Size = new Size(544, 297);
            _accountsTabControl.TabIndex = 1;
            //
            // _botTabPage
            //
            _botTabPage.Controls.Add(_botAccountSection);
            _botTabPage.Location = new Point(4, 24);
            _botTabPage.Name = "_botTabPage";
            _botTabPage.Padding = new Padding(8);
            _botTabPage.Size = new Size(536, 269);
            _botTabPage.TabIndex = 0;
            _botTabPage.Text = "Бот";
            _botTabPage.UseVisualStyleBackColor = true;
            //
            // _botAccountSection
            //
            _botAccountSection.AutoSize = true;
            _botAccountSection.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _botAccountSection.Dock = DockStyle.Fill;
            _botAccountSection.Location = new Point(8, 8);
            _botAccountSection.Margin = new Padding(0);
            _botAccountSection.Name = "_botAccountSection";
            _botAccountSection.Size = new Size(520, 253);
            _botAccountSection.TabIndex = 0;
            _botAccountSection.SettingChanged += OnAccountSettingChanged;
            //
            // _broadcasterTabPage
            //
            _broadcasterTabPage.Controls.Add(_broadcasterAccountSection);
            _broadcasterTabPage.Location = new Point(4, 24);
            _broadcasterTabPage.Name = "_broadcasterTabPage";
            _broadcasterTabPage.Padding = new Padding(8);
            _broadcasterTabPage.Size = new Size(536, 269);
            _broadcasterTabPage.TabIndex = 1;
            _broadcasterTabPage.Text = "Стример";
            _broadcasterTabPage.UseVisualStyleBackColor = true;
            //
            // _broadcasterAccountSection
            //
            _broadcasterAccountSection.AutoSize = true;
            _broadcasterAccountSection.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _broadcasterAccountSection.Dock = DockStyle.Fill;
            _broadcasterAccountSection.Location = new Point(8, 8);
            _broadcasterAccountSection.Margin = new Padding(0);
            _broadcasterAccountSection.Name = "_broadcasterAccountSection";
            _broadcasterAccountSection.Size = new Size(520, 253);
            _broadcasterAccountSection.TabIndex = 0;
            _broadcasterAccountSection.SettingChanged += OnAccountSettingChanged;
            //
            // _oauthInfoLabel
            //
            _oauthInfoLabel.AutoSize = true;
            _oauthInfoLabel.Dock = DockStyle.Fill;
            _oauthInfoLabel.ForeColor = Color.Gray;
            _oauthInfoLabel.Location = new Point(8, 425);
            _oauthInfoLabel.Name = "_oauthInfoLabel";
            _oauthInfoLabel.Size = new Size(544, 45);
            _oauthInfoLabel.TabIndex = 2;
            _oauthInfoLabel.Text = "Получите Client ID и Client Secret на https://dev.twitch.tv/console/apps. \r\nВкладка «Бот» — токен от чьего имени бот пишет в чат и слушает сообщения. \r\nВкладка «Стример» — токен владельца канала для управления опросами и информацией трансляции.";
            //
            // OAuthSettingsControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_oauthTableLayout);
            Name = "OAuthSettingsControl";
            Size = new Size(560, 480);
            _oauthTableLayout.ResumeLayout(false);
            _oauthTableLayout.PerformLayout();
            _oauthCredentialsSection.ResumeLayout(false);
            _oauthCredentialsSection.PerformLayout();
            _clientIdPanel.ResumeLayout(false);
            _clientIdPanel.PerformLayout();
            _clientSecretPanel.ResumeLayout(false);
            _clientSecretPanel.PerformLayout();
            _redirectUriPanel.ResumeLayout(false);
            _redirectUriPanel.PerformLayout();
            _accountsTabControl.ResumeLayout(false);
            _botTabPage.ResumeLayout(false);
            _botTabPage.PerformLayout();
            _broadcasterTabPage.ResumeLayout(false);
            _broadcasterTabPage.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel _oauthTableLayout;
        private TableLayoutPanel _oauthCredentialsSection;
        private Panel _clientIdPanel;
        private Panel _clientSecretPanel;
        private Panel _redirectUriPanel;
        private Label _clientIdLabel;
        private TextBox _clientIdTextBox;
        private Button _clientIdResetButton;
        private Label _clientSecretLabel;
        private TextBox _clientSecretTextBox;
        private Button _clientSecretResetButton;
        private Label _redirectUriLabel;
        private TextBox _redirectUriTextBox;
        private Button _redirectUriEditButton;
        private Button _redirectUriResetButton;
        private TabControl _accountsTabControl;
        private TabPage _botTabPage;
        private OAuthAccountSection _botAccountSection;
        private TabPage _broadcasterTabPage;
        private OAuthAccountSection _broadcasterAccountSection;
        private Label _oauthInfoLabel;
    }
}
