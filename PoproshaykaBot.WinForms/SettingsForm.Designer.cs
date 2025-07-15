namespace PoproshaykaBot.WinForms;

partial class SettingsForm
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

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        _tabControl = new TabControl();
        _basicTabPage = new TabPage();
        _rateLimitingTabPage = new TabPage();
        _oauthTabPage = new TabPage();
        _okButton = new Button();
        _cancelButton = new Button();
        _applyButton = new Button();
        _resetButton = new Button();
        _botUsernameLabel = new Label();
        _botUsernameTextBox = new TextBox();
        _botUsernameResetButton = new Button();
        _channelLabel = new Label();
        _channelTextBox = new TextBox();
        _channelResetButton = new Button();
        _messagesAllowedLabel = new Label();
        _messagesAllowedNumeric = new NumericUpDown();
        _messagesAllowedResetButton = new Button();
        _throttlingPeriodLabel = new Label();
        _throttlingPeriodNumeric = new NumericUpDown();
        _throttlingPeriodResetButton = new Button();
        _oauthInfoLabel = new Label();
        _scopesTextBox = new TextBox();
        _scopesLabel = new Label();
        _scopesResetButton = new Button();
        _redirectUriTextBox = new TextBox();
        _redirectUriLabel = new Label();
        _redirectUriResetButton = new Button();
        _clientSecretTextBox = new TextBox();
        _clientSecretLabel = new Label();
        _clientSecretResetButton = new Button();
        _clientIdTextBox = new TextBox();
        _clientIdLabel = new Label();
        _clientIdResetButton = new Button();
        _tabControl.SuspendLayout();
        _basicTabPage.SuspendLayout();
        _rateLimitingTabPage.SuspendLayout();
        _oauthTabPage.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_messagesAllowedNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_throttlingPeriodNumeric).BeginInit();
        SuspendLayout();
        // 
        // _okButton
        //
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Location = new Point(335, 415);
        _okButton.Name = "_okButton";
        _okButton.Size = new Size(75, 23);
        _okButton.TabIndex = 14;
        _okButton.Text = "OK";
        _okButton.UseVisualStyleBackColor = true;
        _okButton.Click += OnOkButtonClicked;
        //
        // _cancelButton
        //
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(416, 415);
        _cancelButton.Name = "_cancelButton";
        _cancelButton.Size = new Size(75, 23);
        _cancelButton.TabIndex = 15;
        _cancelButton.Text = "Отмена";
        _cancelButton.UseVisualStyleBackColor = true;
        _cancelButton.Click += OnCancelButtonClicked;
        //
        // _applyButton
        //
        _applyButton.Enabled = false;
        _applyButton.Location = new Point(497, 415);
        _applyButton.Name = "_applyButton";
        _applyButton.Size = new Size(75, 23);
        _applyButton.TabIndex = 16;
        _applyButton.Text = "Применить";
        _applyButton.UseVisualStyleBackColor = true;
        _applyButton.Click += OnApplyButtonClicked;
        //
        // _resetButton
        //
        _resetButton.Location = new Point(12, 415);
        _resetButton.Name = "_resetButton";
        _resetButton.Size = new Size(75, 23);
        _resetButton.TabIndex = 17;
        _resetButton.Text = "Сброс";
        _resetButton.UseVisualStyleBackColor = true;
        _resetButton.Click += OnResetButtonClicked;
        //
        // _tabControl
        //
        _tabControl.Controls.Add(_basicTabPage);
        _tabControl.Controls.Add(_rateLimitingTabPage);
        _tabControl.Controls.Add(_oauthTabPage);
        _tabControl.Location = new Point(12, 12);
        _tabControl.Name = "_tabControl";
        _tabControl.SelectedIndex = 0;
        _tabControl.Size = new Size(556, 392);
        _tabControl.TabIndex = 13;
        //
        // _basicTabPage
        //
        _basicTabPage.Controls.Add(_channelResetButton);
        _basicTabPage.Controls.Add(_channelTextBox);
        _basicTabPage.Controls.Add(_channelLabel);
        _basicTabPage.Controls.Add(_botUsernameResetButton);
        _basicTabPage.Controls.Add(_botUsernameTextBox);
        _basicTabPage.Controls.Add(_botUsernameLabel);
        _basicTabPage.Location = new Point(4, 24);
        _basicTabPage.Name = "_basicTabPage";
        _basicTabPage.Padding = new Padding(3);
        _basicTabPage.Size = new Size(548, 364);
        _basicTabPage.TabIndex = 0;
        _basicTabPage.Text = "Основные";
        _basicTabPage.UseVisualStyleBackColor = true;
        //
        // _rateLimitingTabPage
        //
        _rateLimitingTabPage.Controls.Add(_throttlingPeriodResetButton);
        _rateLimitingTabPage.Controls.Add(_throttlingPeriodNumeric);
        _rateLimitingTabPage.Controls.Add(_throttlingPeriodLabel);
        _rateLimitingTabPage.Controls.Add(_messagesAllowedResetButton);
        _rateLimitingTabPage.Controls.Add(_messagesAllowedNumeric);
        _rateLimitingTabPage.Controls.Add(_messagesAllowedLabel);
        _rateLimitingTabPage.Location = new Point(4, 24);
        _rateLimitingTabPage.Name = "_rateLimitingTabPage";
        _rateLimitingTabPage.Padding = new Padding(3);
        _rateLimitingTabPage.Size = new Size(548, 364);
        _rateLimitingTabPage.TabIndex = 1;
        _rateLimitingTabPage.Text = "Ограничения";
        _rateLimitingTabPage.UseVisualStyleBackColor = true;
        //
        // _oauthTabPage
        //
        _oauthTabPage.Controls.Add(_oauthInfoLabel);
        _oauthTabPage.Controls.Add(_scopesTextBox);
        _oauthTabPage.Controls.Add(_scopesLabel);
        _oauthTabPage.Controls.Add(_scopesResetButton);
        _oauthTabPage.Controls.Add(_redirectUriTextBox);
        _oauthTabPage.Controls.Add(_redirectUriLabel);
        _oauthTabPage.Controls.Add(_redirectUriResetButton);
        _oauthTabPage.Controls.Add(_clientSecretTextBox);
        _oauthTabPage.Controls.Add(_clientSecretLabel);
        _oauthTabPage.Controls.Add(_clientSecretResetButton);
        _oauthTabPage.Controls.Add(_clientIdTextBox);
        _oauthTabPage.Controls.Add(_clientIdLabel);
        _oauthTabPage.Controls.Add(_clientIdResetButton);
        _oauthTabPage.Location = new Point(4, 24);
        _oauthTabPage.Name = "_oauthTabPage";
        _oauthTabPage.Padding = new Padding(3);
        _oauthTabPage.Size = new Size(548, 364);
        _oauthTabPage.TabIndex = 2;
        _oauthTabPage.Text = "OAuth";
        _oauthTabPage.UseVisualStyleBackColor = true;
        //
        // _botUsernameLabel
        //
        _botUsernameLabel.AutoSize = true;
        _botUsernameLabel.Location = new Point(15, 20);
        _botUsernameLabel.Name = "_botUsernameLabel";
        _botUsernameLabel.Size = new Size(140, 15);
        _botUsernameLabel.TabIndex = 0;
        _botUsernameLabel.Text = "Имя пользователя бота:";
        //
        // _botUsernameTextBox
        //
        _botUsernameTextBox.Location = new Point(170, 17);
        _botUsernameTextBox.Name = "_botUsernameTextBox";
        _botUsernameTextBox.Size = new Size(170, 23);
        _botUsernameTextBox.TabIndex = 1;
        _botUsernameTextBox.TextChanged += OnSettingChanged;
        //
        // _botUsernameResetButton
        //
        _botUsernameResetButton.Location = new Point(345, 17);
        _botUsernameResetButton.Name = "_botUsernameResetButton";
        _botUsernameResetButton.Size = new Size(25, 23);
        _botUsernameResetButton.TabIndex = 2;
        _botUsernameResetButton.Text = "↺";
        _botUsernameResetButton.UseVisualStyleBackColor = true;
        _botUsernameResetButton.Click += OnBotUsernameResetButtonClicked;
        // 
        // _channelLabel
        //
        _channelLabel.AutoSize = true;
        _channelLabel.Location = new Point(15, 50);
        _channelLabel.Name = "_channelLabel";
        _channelLabel.Size = new Size(43, 15);
        _channelLabel.TabIndex = 3;
        _channelLabel.Text = "Канал:";
        //
        // _channelTextBox
        //
        _channelTextBox.Location = new Point(170, 47);
        _channelTextBox.Name = "_channelTextBox";
        _channelTextBox.Size = new Size(170, 23);
        _channelTextBox.TabIndex = 4;
        _channelTextBox.TextChanged += OnSettingChanged;
        //
        // _channelResetButton
        //
        _channelResetButton.Location = new Point(345, 47);
        _channelResetButton.Name = "_channelResetButton";
        _channelResetButton.Size = new Size(25, 23);
        _channelResetButton.TabIndex = 5;
        _channelResetButton.Text = "↺";
        _channelResetButton.UseVisualStyleBackColor = true;
        _channelResetButton.Click += OnChannelResetButtonClicked;
        // 
        // _messagesAllowedLabel
        //
        _messagesAllowedLabel.AutoSize = true;
        _messagesAllowedLabel.Location = new Point(15, 20);
        _messagesAllowedLabel.Name = "_messagesAllowedLabel";
        _messagesAllowedLabel.Size = new Size(129, 15);
        _messagesAllowedLabel.TabIndex = 0;
        _messagesAllowedLabel.Text = "Сообщений в период:";
        //
        // _messagesAllowedNumeric
        //
        _messagesAllowedNumeric.Location = new Point(170, 17);
        _messagesAllowedNumeric.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
        _messagesAllowedNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _messagesAllowedNumeric.Name = "_messagesAllowedNumeric";
        _messagesAllowedNumeric.Size = new Size(100, 23);
        _messagesAllowedNumeric.TabIndex = 1;
        _messagesAllowedNumeric.Value = new decimal(new int[] { 750, 0, 0, 0 });
        _messagesAllowedNumeric.ValueChanged += OnSettingChanged;
        //
        // _messagesAllowedResetButton
        //
        _messagesAllowedResetButton.Location = new Point(275, 17);
        _messagesAllowedResetButton.Name = "_messagesAllowedResetButton";
        _messagesAllowedResetButton.Size = new Size(25, 23);
        _messagesAllowedResetButton.TabIndex = 2;
        _messagesAllowedResetButton.Text = "↺";
        _messagesAllowedResetButton.UseVisualStyleBackColor = true;
        _messagesAllowedResetButton.Click += OnMessagesAllowedResetButtonClicked;
        // 
        // _throttlingPeriodLabel
        //
        _throttlingPeriodLabel.AutoSize = true;
        _throttlingPeriodLabel.Location = new Point(15, 50);
        _throttlingPeriodLabel.Name = "_throttlingPeriodLabel";
        _throttlingPeriodLabel.Size = new Size(156, 15);
        _throttlingPeriodLabel.TabIndex = 3;
        _throttlingPeriodLabel.Text = "Период ограничения (сек):";
        //
        // _throttlingPeriodNumeric
        //
        _throttlingPeriodNumeric.Location = new Point(170, 47);
        _throttlingPeriodNumeric.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
        _throttlingPeriodNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _throttlingPeriodNumeric.Name = "_throttlingPeriodNumeric";
        _throttlingPeriodNumeric.Size = new Size(100, 23);
        _throttlingPeriodNumeric.TabIndex = 4;
        _throttlingPeriodNumeric.Value = new decimal(new int[] { 30, 0, 0, 0 });
        _throttlingPeriodNumeric.ValueChanged += OnSettingChanged;
        //
        // _throttlingPeriodResetButton
        //
        _throttlingPeriodResetButton.Location = new Point(275, 47);
        _throttlingPeriodResetButton.Name = "_throttlingPeriodResetButton";
        _throttlingPeriodResetButton.Size = new Size(25, 23);
        _throttlingPeriodResetButton.TabIndex = 5;
        _throttlingPeriodResetButton.Text = "↺";
        _throttlingPeriodResetButton.UseVisualStyleBackColor = true;
        _throttlingPeriodResetButton.Click += OnThrottlingPeriodResetButtonClicked;
        // 
        // _oauthInfoLabel
        //
        _oauthInfoLabel.ForeColor = Color.Gray;
        _oauthInfoLabel.Location = new Point(15, 160);
        _oauthInfoLabel.Name = "_oauthInfoLabel";
        _oauthInfoLabel.Size = new Size(520, 30);
        _oauthInfoLabel.TabIndex = 12;
        _oauthInfoLabel.Text = "Получите Client ID и Client Secret на https://dev.twitch.tv/console/apps\nScopes разделяйте пробелами (например: chat:read chat:edit)";
        // 
        // _scopesTextBox
        //
        _scopesTextBox.Location = new Point(110, 110);
        _scopesTextBox.Name = "_scopesTextBox";
        _scopesTextBox.Size = new Size(350, 23);
        _scopesTextBox.TabIndex = 10;
        _scopesTextBox.TextChanged += OnSettingChanged;
        //
        // _scopesLabel
        //
        _scopesLabel.AutoSize = true;
        _scopesLabel.Location = new Point(15, 113);
        _scopesLabel.Name = "_scopesLabel";
        _scopesLabel.Size = new Size(47, 15);
        _scopesLabel.TabIndex = 9;
        _scopesLabel.Text = "Scopes:";
        //
        // _scopesResetButton
        //
        _scopesResetButton.Location = new Point(465, 110);
        _scopesResetButton.Name = "_scopesResetButton";
        _scopesResetButton.Size = new Size(25, 23);
        _scopesResetButton.TabIndex = 11;
        _scopesResetButton.Text = "↺";
        _scopesResetButton.UseVisualStyleBackColor = true;
        _scopesResetButton.Click += OnScopesResetButtonClicked;
        // 
        // _redirectUriTextBox
        //
        _redirectUriTextBox.Location = new Point(110, 80);
        _redirectUriTextBox.Name = "_redirectUriTextBox";
        _redirectUriTextBox.Size = new Size(350, 23);
        _redirectUriTextBox.TabIndex = 7;
        _redirectUriTextBox.TextChanged += OnSettingChanged;
        //
        // _redirectUriLabel
        //
        _redirectUriLabel.AutoSize = true;
        _redirectUriLabel.Location = new Point(15, 83);
        _redirectUriLabel.Name = "_redirectUriLabel";
        _redirectUriLabel.Size = new Size(74, 15);
        _redirectUriLabel.TabIndex = 6;
        _redirectUriLabel.Text = "Redirect URI:";
        //
        // _redirectUriResetButton
        //
        _redirectUriResetButton.Location = new Point(465, 80);
        _redirectUriResetButton.Name = "_redirectUriResetButton";
        _redirectUriResetButton.Size = new Size(25, 23);
        _redirectUriResetButton.TabIndex = 8;
        _redirectUriResetButton.Text = "↺";
        _redirectUriResetButton.UseVisualStyleBackColor = true;
        _redirectUriResetButton.Click += OnRedirectUriResetButtonClicked;
        // 
        // _clientSecretTextBox
        //
        _clientSecretTextBox.Location = new Point(110, 50);
        _clientSecretTextBox.Name = "_clientSecretTextBox";
        _clientSecretTextBox.Size = new Size(350, 23);
        _clientSecretTextBox.TabIndex = 4;
        _clientSecretTextBox.UseSystemPasswordChar = true;
        _clientSecretTextBox.TextChanged += OnSettingChanged;
        //
        // _clientSecretLabel
        //
        _clientSecretLabel.AutoSize = true;
        _clientSecretLabel.Location = new Point(15, 53);
        _clientSecretLabel.Name = "_clientSecretLabel";
        _clientSecretLabel.Size = new Size(76, 15);
        _clientSecretLabel.TabIndex = 3;
        _clientSecretLabel.Text = "Client Secret:";
        //
        // _clientSecretResetButton
        //
        _clientSecretResetButton.Location = new Point(465, 50);
        _clientSecretResetButton.Name = "_clientSecretResetButton";
        _clientSecretResetButton.Size = new Size(25, 23);
        _clientSecretResetButton.TabIndex = 5;
        _clientSecretResetButton.Text = "↺";
        _clientSecretResetButton.UseVisualStyleBackColor = true;
        _clientSecretResetButton.Click += OnClientSecretResetButtonClicked;
        // 
        // _clientIdTextBox
        //
        _clientIdTextBox.Location = new Point(110, 20);
        _clientIdTextBox.Name = "_clientIdTextBox";
        _clientIdTextBox.Size = new Size(350, 23);
        _clientIdTextBox.TabIndex = 1;
        _clientIdTextBox.UseSystemPasswordChar = true;
        _clientIdTextBox.TextChanged += OnSettingChanged;
        //
        // _clientIdLabel
        //
        _clientIdLabel.AutoSize = true;
        _clientIdLabel.Location = new Point(15, 23);
        _clientIdLabel.Name = "_clientIdLabel";
        _clientIdLabel.Size = new Size(55, 15);
        _clientIdLabel.TabIndex = 0;
        _clientIdLabel.Text = "Client ID:";
        //
        // _clientIdResetButton
        //
        _clientIdResetButton.Location = new Point(465, 20);
        _clientIdResetButton.Name = "_clientIdResetButton";
        _clientIdResetButton.Size = new Size(25, 23);
        _clientIdResetButton.TabIndex = 2;
        _clientIdResetButton.Text = "↺";
        _clientIdResetButton.UseVisualStyleBackColor = true;
        _clientIdResetButton.Click += OnClientIdResetButtonClicked;
        // 
        // SettingsForm
        // 
        AcceptButton = _okButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = _cancelButton;
        ClientSize = new Size(580, 450);
        Controls.Add(_tabControl);
        Controls.Add(_resetButton);
        Controls.Add(_applyButton);
        Controls.Add(_cancelButton);
        Controls.Add(_okButton);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "SettingsForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Настройки Twitch бота";
        ((System.ComponentModel.ISupportInitialize)_messagesAllowedNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)_throttlingPeriodNumeric).EndInit();
        _tabControl.ResumeLayout(false);
        _basicTabPage.ResumeLayout(false);
        _basicTabPage.PerformLayout();
        _rateLimitingTabPage.ResumeLayout(false);
        _rateLimitingTabPage.PerformLayout();
        _oauthTabPage.ResumeLayout(false);
        ResumeLayout(false);
    }

    private TabControl _tabControl;
    private TabPage _basicTabPage;
    private TabPage _rateLimitingTabPage;
    private TabPage _oauthTabPage;
    private Button _okButton;
    private Button _cancelButton;
    private Button _applyButton;
    private Button _resetButton;
    private Label _botUsernameLabel;
    private TextBox _botUsernameTextBox;
    private Button _botUsernameResetButton;
    private Label _channelLabel;
    private TextBox _channelTextBox;
    private Button _channelResetButton;
    private Label _messagesAllowedLabel;
    private NumericUpDown _messagesAllowedNumeric;
    private Button _messagesAllowedResetButton;
    private Label _throttlingPeriodLabel;
    private NumericUpDown _throttlingPeriodNumeric;
    private Button _throttlingPeriodResetButton;
    private Label _clientIdLabel;
    private TextBox _clientIdTextBox;
    private Button _clientIdResetButton;
    private Label _clientSecretLabel;
    private TextBox _clientSecretTextBox;
    private Button _clientSecretResetButton;
    private Label _redirectUriLabel;
    private TextBox _redirectUriTextBox;
    private Button _redirectUriResetButton;
    private Label _scopesLabel;
    private TextBox _scopesTextBox;
    private Button _scopesResetButton;
    private Label _oauthInfoLabel;

    #endregion
}
