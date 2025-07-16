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
        _channelResetButton = new Button();
        _channelTextBox = new TextBox();
        _channelLabel = new Label();
        _botUsernameResetButton = new Button();
        _botUsernameTextBox = new TextBox();
        _botUsernameLabel = new Label();
        _rateLimitingTabPage = new TabPage();
        _throttlingPeriodResetButton = new Button();
        _throttlingPeriodNumeric = new NumericUpDown();
        _throttlingPeriodLabel = new Label();
        _messagesAllowedResetButton = new Button();
        _messagesAllowedNumeric = new NumericUpDown();
        _messagesAllowedLabel = new Label();
        _oauthTabPage = new TabPage();
        _showTokenButton = new Button();
        _clearTokensButton = new Button();
        _refreshTokenButton = new Button();
        _validateTokenButton = new Button();
        _lastRefreshValueLabel = new Label();
        _lastRefreshLabel = new Label();
        _tokenStatusValueLabel = new Label();
        _tokenStatusLabel = new Label();
        _refreshTokenTextBox = new TextBox();
        _refreshTokenLabel = new Label();
        _accessTokenTextBox = new TextBox();
        _accessTokenLabel = new Label();
        _tokenSectionLabel = new Label();
        _authStatusLabel = new Label();
        _testAuthButton = new Button();
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
        _okButton = new Button();
        _cancelButton = new Button();
        _applyButton = new Button();
        _resetButton = new Button();
        _tabControl.SuspendLayout();
        _basicTabPage.SuspendLayout();
        _rateLimitingTabPage.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_throttlingPeriodNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_messagesAllowedNumeric).BeginInit();
        _oauthTabPage.SuspendLayout();
        SuspendLayout();
        // 
        // _tabControl
        // 
        _tabControl.Controls.Add(_basicTabPage);
        _tabControl.Controls.Add(_rateLimitingTabPage);
        _tabControl.Controls.Add(_oauthTabPage);
        _tabControl.Location = new Point(12, 12);
        _tabControl.Name = "_tabControl";
        _tabControl.SelectedIndex = 0;
        _tabControl.Size = new Size(556, 470);
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
        // _channelTextBox
        // 
        _channelTextBox.Location = new Point(170, 47);
        _channelTextBox.Name = "_channelTextBox";
        _channelTextBox.Size = new Size(170, 23);
        _channelTextBox.TabIndex = 4;
        _channelTextBox.TextChanged += OnSettingChanged;
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
        // _botUsernameTextBox
        // 
        _botUsernameTextBox.Location = new Point(170, 17);
        _botUsernameTextBox.Name = "_botUsernameTextBox";
        _botUsernameTextBox.Size = new Size(170, 23);
        _botUsernameTextBox.TabIndex = 1;
        _botUsernameTextBox.TextChanged += OnSettingChanged;
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
        // _throttlingPeriodLabel
        // 
        _throttlingPeriodLabel.AutoSize = true;
        _throttlingPeriodLabel.Location = new Point(15, 50);
        _throttlingPeriodLabel.Name = "_throttlingPeriodLabel";
        _throttlingPeriodLabel.Size = new Size(156, 15);
        _throttlingPeriodLabel.TabIndex = 3;
        _throttlingPeriodLabel.Text = "Период ограничения (сек):";
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
        // _messagesAllowedLabel
        // 
        _messagesAllowedLabel.AutoSize = true;
        _messagesAllowedLabel.Location = new Point(15, 20);
        _messagesAllowedLabel.Name = "_messagesAllowedLabel";
        _messagesAllowedLabel.Size = new Size(129, 15);
        _messagesAllowedLabel.TabIndex = 0;
        _messagesAllowedLabel.Text = "Сообщений в период:";
        // 
        // _oauthTabPage
        // 
        _oauthTabPage.Controls.Add(_showTokenButton);
        _oauthTabPage.Controls.Add(_clearTokensButton);
        _oauthTabPage.Controls.Add(_refreshTokenButton);
        _oauthTabPage.Controls.Add(_validateTokenButton);
        _oauthTabPage.Controls.Add(_lastRefreshValueLabel);
        _oauthTabPage.Controls.Add(_lastRefreshLabel);
        _oauthTabPage.Controls.Add(_tokenStatusValueLabel);
        _oauthTabPage.Controls.Add(_tokenStatusLabel);
        _oauthTabPage.Controls.Add(_refreshTokenTextBox);
        _oauthTabPage.Controls.Add(_refreshTokenLabel);
        _oauthTabPage.Controls.Add(_accessTokenTextBox);
        _oauthTabPage.Controls.Add(_accessTokenLabel);
        _oauthTabPage.Controls.Add(_tokenSectionLabel);
        _oauthTabPage.Controls.Add(_authStatusLabel);
        _oauthTabPage.Controls.Add(_testAuthButton);
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
        _oauthTabPage.Size = new Size(548, 442);
        _oauthTabPage.TabIndex = 2;
        _oauthTabPage.Text = "OAuth";
        _oauthTabPage.UseVisualStyleBackColor = true;
        // 
        // _showTokenButton
        // 
        _showTokenButton.Location = new Point(440, 177);
        _showTokenButton.Name = "_showTokenButton";
        _showTokenButton.Size = new Size(60, 23);
        _showTokenButton.TabIndex = 25;
        _showTokenButton.Text = "👁";
        _showTokenButton.UseVisualStyleBackColor = true;
        _showTokenButton.Click += OnShowTokenButtonClicked;
        // 
        // _clearTokensButton
        // 
        _clearTokensButton.Location = new Point(235, 275);
        _clearTokensButton.Name = "_clearTokensButton";
        _clearTokensButton.Size = new Size(100, 23);
        _clearTokensButton.TabIndex = 24;
        _clearTokensButton.Text = "Очистить";
        _clearTokensButton.UseVisualStyleBackColor = true;
        _clearTokensButton.Click += OnClearTokensButtonClicked;
        // 
        // _refreshTokenButton
        // 
        _refreshTokenButton.Location = new Point(125, 275);
        _refreshTokenButton.Name = "_refreshTokenButton";
        _refreshTokenButton.Size = new Size(100, 23);
        _refreshTokenButton.TabIndex = 23;
        _refreshTokenButton.Text = "Обновить";
        _refreshTokenButton.UseVisualStyleBackColor = true;
        _refreshTokenButton.Click += OnRefreshTokenButtonClicked;
        // 
        // _validateTokenButton
        // 
        _validateTokenButton.Location = new Point(15, 275);
        _validateTokenButton.Name = "_validateTokenButton";
        _validateTokenButton.Size = new Size(100, 23);
        _validateTokenButton.TabIndex = 22;
        _validateTokenButton.Text = "Проверить";
        _validateTokenButton.UseVisualStyleBackColor = true;
        _validateTokenButton.Click += OnValidateTokenButtonClicked;
        // 
        // _lastRefreshValueLabel
        // 
        _lastRefreshValueLabel.AutoSize = true;
        _lastRefreshValueLabel.ForeColor = Color.Gray;
        _lastRefreshValueLabel.Location = new Point(130, 245);
        _lastRefreshValueLabel.Name = "_lastRefreshValueLabel";
        _lastRefreshValueLabel.Size = new Size(71, 15);
        _lastRefreshValueLabel.TabIndex = 21;
        _lastRefreshValueLabel.Text = "Неизвестно";
        // 
        // _lastRefreshLabel
        // 
        _lastRefreshLabel.AutoSize = true;
        _lastRefreshLabel.Location = new Point(15, 245);
        _lastRefreshLabel.Name = "_lastRefreshLabel";
        _lastRefreshLabel.Size = new Size(140, 15);
        _lastRefreshLabel.TabIndex = 20;
        _lastRefreshLabel.Text = "Последнее обновление:";
        // 
        // _tokenStatusValueLabel
        // 
        _tokenStatusValueLabel.AutoSize = true;
        _tokenStatusValueLabel.ForeColor = Color.Gray;
        _tokenStatusValueLabel.Location = new Point(110, 225);
        _tokenStatusValueLabel.Name = "_tokenStatusValueLabel";
        _tokenStatusValueLabel.Size = new Size(78, 15);
        _tokenStatusValueLabel.TabIndex = 19;
        _tokenStatusValueLabel.Text = "Не проверен";
        // 
        // _tokenStatusLabel
        // 
        _tokenStatusLabel.AutoSize = true;
        _tokenStatusLabel.Location = new Point(15, 225);
        _tokenStatusLabel.Name = "_tokenStatusLabel";
        _tokenStatusLabel.Size = new Size(86, 15);
        _tokenStatusLabel.TabIndex = 18;
        _tokenStatusLabel.Text = "Статус токена:";
        // 
        // _refreshTokenTextBox
        // 
        _refreshTokenTextBox.Location = new Point(110, 192);
        _refreshTokenTextBox.Name = "_refreshTokenTextBox";
        _refreshTokenTextBox.ReadOnly = true;
        _refreshTokenTextBox.Size = new Size(320, 23);
        _refreshTokenTextBox.TabIndex = 17;
        _refreshTokenTextBox.UseSystemPasswordChar = true;
        // 
        // _refreshTokenLabel
        // 
        _refreshTokenLabel.AutoSize = true;
        _refreshTokenLabel.Location = new Point(15, 195);
        _refreshTokenLabel.Name = "_refreshTokenLabel";
        _refreshTokenLabel.Size = new Size(83, 15);
        _refreshTokenLabel.TabIndex = 16;
        _refreshTokenLabel.Text = "Refresh Token:";
        // 
        // _accessTokenTextBox
        // 
        _accessTokenTextBox.Location = new Point(110, 162);
        _accessTokenTextBox.Name = "_accessTokenTextBox";
        _accessTokenTextBox.ReadOnly = true;
        _accessTokenTextBox.Size = new Size(320, 23);
        _accessTokenTextBox.TabIndex = 15;
        _accessTokenTextBox.UseSystemPasswordChar = true;
        // 
        // _accessTokenLabel
        // 
        _accessTokenLabel.AutoSize = true;
        _accessTokenLabel.Location = new Point(15, 165);
        _accessTokenLabel.Name = "_accessTokenLabel";
        _accessTokenLabel.Size = new Size(80, 15);
        _accessTokenLabel.TabIndex = 14;
        _accessTokenLabel.Text = "Access Token:";
        // 
        // _tokenSectionLabel
        // 
        _tokenSectionLabel.AutoSize = true;
        _tokenSectionLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _tokenSectionLabel.Location = new Point(15, 140);
        _tokenSectionLabel.Name = "_tokenSectionLabel";
        _tokenSectionLabel.Size = new Size(136, 15);
        _tokenSectionLabel.TabIndex = 13;
        _tokenSectionLabel.Text = "Управление токенами";
        // 
        // _authStatusLabel
        // 
        _authStatusLabel.AutoSize = true;
        _authStatusLabel.Location = new Point(145, 314);
        _authStatusLabel.Name = "_authStatusLabel";
        _authStatusLabel.Size = new Size(0, 15);
        _authStatusLabel.TabIndex = 27;
        // 
        // _testAuthButton
        // 
        _testAuthButton.Location = new Point(15, 310);
        _testAuthButton.Name = "_testAuthButton";
        _testAuthButton.Size = new Size(120, 23);
        _testAuthButton.TabIndex = 26;
        _testAuthButton.Text = "Тест авторизации";
        _testAuthButton.UseVisualStyleBackColor = true;
        _testAuthButton.Click += OnTestAuthButtonClicked;
        // 
        // _oauthInfoLabel
        // 
        _oauthInfoLabel.ForeColor = Color.Gray;
        _oauthInfoLabel.Location = new Point(15, 340);
        _oauthInfoLabel.Name = "_oauthInfoLabel";
        _oauthInfoLabel.Size = new Size(520, 34);
        _oauthInfoLabel.TabIndex = 28;
        _oauthInfoLabel.Text = "Получите Client ID и Client Secret на https://dev.twitch.tv/console/apps. \r\nScopes разделяйте пробелами.";
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
        // _okButton
        // 
        _okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Location = new Point(335, 488);
        _okButton.Name = "_okButton";
        _okButton.Size = new Size(75, 23);
        _okButton.TabIndex = 14;
        _okButton.Text = "OK";
        _okButton.UseVisualStyleBackColor = true;
        _okButton.Click += OnOkButtonClicked;
        // 
        // _cancelButton
        // 
        _cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(416, 488);
        _cancelButton.Name = "_cancelButton";
        _cancelButton.Size = new Size(75, 23);
        _cancelButton.TabIndex = 15;
        _cancelButton.Text = "Отмена";
        _cancelButton.UseVisualStyleBackColor = true;
        _cancelButton.Click += OnCancelButtonClicked;
        // 
        // _applyButton
        // 
        _applyButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _applyButton.Enabled = false;
        _applyButton.Location = new Point(497, 488);
        _applyButton.Name = "_applyButton";
        _applyButton.Size = new Size(75, 23);
        _applyButton.TabIndex = 16;
        _applyButton.Text = "Применить";
        _applyButton.UseVisualStyleBackColor = true;
        _applyButton.Click += OnApplyButtonClicked;
        // 
        // _resetButton
        // 
        _resetButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _resetButton.Location = new Point(12, 488);
        _resetButton.Name = "_resetButton";
        _resetButton.Size = new Size(75, 23);
        _resetButton.TabIndex = 17;
        _resetButton.Text = "Сброс";
        _resetButton.UseVisualStyleBackColor = true;
        _resetButton.Click += OnResetButtonClicked;
        // 
        // SettingsForm
        // 
        AcceptButton = _okButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = _cancelButton;
        ClientSize = new Size(580, 523);
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
        _tabControl.ResumeLayout(false);
        _basicTabPage.ResumeLayout(false);
        _basicTabPage.PerformLayout();
        _rateLimitingTabPage.ResumeLayout(false);
        _rateLimitingTabPage.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_throttlingPeriodNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)_messagesAllowedNumeric).EndInit();
        _oauthTabPage.ResumeLayout(false);
        _oauthTabPage.PerformLayout();
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
    private Button _testAuthButton;
    private Label _authStatusLabel;
    private Label _tokenSectionLabel;
    private Label _accessTokenLabel;
    private TextBox _accessTokenTextBox;
    private Label _refreshTokenLabel;
    private TextBox _refreshTokenTextBox;
    private Label _tokenStatusLabel;
    private Label _tokenStatusValueLabel;
    private Label _lastRefreshLabel;
    private Label _lastRefreshValueLabel;
    private Button _validateTokenButton;
    private Button _refreshTokenButton;
    private Button _clearTokensButton;
    private Button _showTokenButton;

    #endregion
}
