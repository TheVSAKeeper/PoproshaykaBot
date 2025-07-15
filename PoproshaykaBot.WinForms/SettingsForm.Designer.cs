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
        _okButton = new Button();
        _cancelButton = new Button();
        _applyButton = new Button();
        _resetButton = new Button();
        _botUsernameLabel = new Label();
        _botUsernameTextBox = new TextBox();
        _channelLabel = new Label();
        _channelTextBox = new TextBox();
        _messagesAllowedLabel = new Label();
        _messagesAllowedNumeric = new NumericUpDown();
        _throttlingPeriodLabel = new Label();
        _throttlingPeriodNumeric = new NumericUpDown();
        _oauthGroupBox = new GroupBox();
        _oauthInfoLabel = new Label();
        _scopesTextBox = new TextBox();
        _scopesLabel = new Label();
        _redirectUriTextBox = new TextBox();
        _redirectUriLabel = new Label();
        _clientSecretTextBox = new TextBox();
        _clientSecretLabel = new Label();
        _clientIdTextBox = new TextBox();
        _clientIdLabel = new Label();
        ((System.ComponentModel.ISupportInitialize)_messagesAllowedNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_throttlingPeriodNumeric).BeginInit();
        _oauthGroupBox.SuspendLayout();
        SuspendLayout();
        // 
        // _okButton
        // 
        _okButton.DialogResult = DialogResult.OK;
        _okButton.Location = new Point(335, 410);
        _okButton.Name = "_okButton";
        _okButton.Size = new Size(75, 23);
        _okButton.TabIndex = 0;
        _okButton.Text = "OK";
        _okButton.UseVisualStyleBackColor = true;
        _okButton.Click += OnOkButtonClicked;
        // 
        // _cancelButton
        // 
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(416, 410);
        _cancelButton.Name = "_cancelButton";
        _cancelButton.Size = new Size(75, 23);
        _cancelButton.TabIndex = 1;
        _cancelButton.Text = "Отмена";
        _cancelButton.UseVisualStyleBackColor = true;
        _cancelButton.Click += OnCancelButtonClicked;
        // 
        // _applyButton
        // 
        _applyButton.Enabled = false;
        _applyButton.Location = new Point(497, 410);
        _applyButton.Name = "_applyButton";
        _applyButton.Size = new Size(75, 23);
        _applyButton.TabIndex = 2;
        _applyButton.Text = "Применить";
        _applyButton.UseVisualStyleBackColor = true;
        _applyButton.Click += OnApplyButtonClicked;
        // 
        // _resetButton
        // 
        _resetButton.Location = new Point(12, 410);
        _resetButton.Name = "_resetButton";
        _resetButton.Size = new Size(75, 23);
        _resetButton.TabIndex = 3;
        _resetButton.Text = "Сброс";
        _resetButton.UseVisualStyleBackColor = true;
        _resetButton.Click += OnResetButtonClicked;
        // 
        // _botUsernameLabel
        // 
        _botUsernameLabel.AutoSize = true;
        _botUsernameLabel.Location = new Point(15, 20);
        _botUsernameLabel.Name = "_botUsernameLabel";
        _botUsernameLabel.Size = new Size(140, 15);
        _botUsernameLabel.TabIndex = 4;
        _botUsernameLabel.Text = "Имя пользователя бота:";
        // 
        // _botUsernameTextBox
        // 
        _botUsernameTextBox.Location = new Point(170, 17);
        _botUsernameTextBox.Name = "_botUsernameTextBox";
        _botUsernameTextBox.Size = new Size(200, 23);
        _botUsernameTextBox.TabIndex = 5;
        _botUsernameTextBox.TextChanged += OnSettingChanged;
        // 
        // _channelLabel
        // 
        _channelLabel.AutoSize = true;
        _channelLabel.Location = new Point(15, 50);
        _channelLabel.Name = "_channelLabel";
        _channelLabel.Size = new Size(43, 15);
        _channelLabel.TabIndex = 6;
        _channelLabel.Text = "Канал:";
        // 
        // _channelTextBox
        // 
        _channelTextBox.Location = new Point(170, 47);
        _channelTextBox.Name = "_channelTextBox";
        _channelTextBox.Size = new Size(200, 23);
        _channelTextBox.TabIndex = 7;
        _channelTextBox.TextChanged += OnSettingChanged;
        // 
        // _messagesAllowedLabel
        // 
        _messagesAllowedLabel.AutoSize = true;
        _messagesAllowedLabel.Location = new Point(15, 80);
        _messagesAllowedLabel.Name = "_messagesAllowedLabel";
        _messagesAllowedLabel.Size = new Size(129, 15);
        _messagesAllowedLabel.TabIndex = 8;
        _messagesAllowedLabel.Text = "Сообщений в период:";
        // 
        // _messagesAllowedNumeric
        // 
        _messagesAllowedNumeric.Location = new Point(170, 77);
        _messagesAllowedNumeric.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
        _messagesAllowedNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _messagesAllowedNumeric.Name = "_messagesAllowedNumeric";
        _messagesAllowedNumeric.Size = new Size(100, 23);
        _messagesAllowedNumeric.TabIndex = 9;
        _messagesAllowedNumeric.Value = new decimal(new int[] { 750, 0, 0, 0 });
        _messagesAllowedNumeric.ValueChanged += OnSettingChanged;
        // 
        // _throttlingPeriodLabel
        // 
        _throttlingPeriodLabel.AutoSize = true;
        _throttlingPeriodLabel.Location = new Point(15, 110);
        _throttlingPeriodLabel.Name = "_throttlingPeriodLabel";
        _throttlingPeriodLabel.Size = new Size(156, 15);
        _throttlingPeriodLabel.TabIndex = 10;
        _throttlingPeriodLabel.Text = "Период ограничения (сек):";
        // 
        // _throttlingPeriodNumeric
        // 
        _throttlingPeriodNumeric.Location = new Point(170, 107);
        _throttlingPeriodNumeric.Maximum = new decimal(new int[] { 300, 0, 0, 0 });
        _throttlingPeriodNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _throttlingPeriodNumeric.Name = "_throttlingPeriodNumeric";
        _throttlingPeriodNumeric.Size = new Size(100, 23);
        _throttlingPeriodNumeric.TabIndex = 11;
        _throttlingPeriodNumeric.Value = new decimal(new int[] { 30, 0, 0, 0 });
        _throttlingPeriodNumeric.ValueChanged += OnSettingChanged;
        // 
        // _oauthGroupBox
        // 
        _oauthGroupBox.Controls.Add(_oauthInfoLabel);
        _oauthGroupBox.Controls.Add(_scopesTextBox);
        _oauthGroupBox.Controls.Add(_scopesLabel);
        _oauthGroupBox.Controls.Add(_redirectUriTextBox);
        _oauthGroupBox.Controls.Add(_redirectUriLabel);
        _oauthGroupBox.Controls.Add(_clientSecretTextBox);
        _oauthGroupBox.Controls.Add(_clientSecretLabel);
        _oauthGroupBox.Controls.Add(_clientIdTextBox);
        _oauthGroupBox.Controls.Add(_clientIdLabel);
        _oauthGroupBox.Location = new Point(15, 140);
        _oauthGroupBox.Name = "_oauthGroupBox";
        _oauthGroupBox.Size = new Size(355, 180);
        _oauthGroupBox.TabIndex = 12;
        _oauthGroupBox.TabStop = false;
        _oauthGroupBox.Text = "OAuth настройки";
        // 
        // _oauthInfoLabel
        // 
        _oauthInfoLabel.ForeColor = Color.Gray;
        _oauthInfoLabel.Location = new Point(10, 145);
        _oauthInfoLabel.Name = "_oauthInfoLabel";
        _oauthInfoLabel.Size = new Size(335, 30);
        _oauthInfoLabel.TabIndex = 8;
        _oauthInfoLabel.Text = "Получите Client ID и Client Secret на https://dev.twitch.tv/console/apps\nScopes разделяйте пробелами (например: chat:read chat:edit)";
        // 
        // _scopesTextBox
        // 
        _scopesTextBox.Location = new Point(95, 112);
        _scopesTextBox.Name = "_scopesTextBox";
        _scopesTextBox.Size = new Size(250, 23);
        _scopesTextBox.TabIndex = 7;
        _scopesTextBox.TextChanged += OnSettingChanged;
        // 
        // _scopesLabel
        // 
        _scopesLabel.AutoSize = true;
        _scopesLabel.Location = new Point(10, 115);
        _scopesLabel.Name = "_scopesLabel";
        _scopesLabel.Size = new Size(47, 15);
        _scopesLabel.TabIndex = 6;
        _scopesLabel.Text = "Scopes:";
        // 
        // _redirectUriTextBox
        // 
        _redirectUriTextBox.Location = new Point(95, 82);
        _redirectUriTextBox.Name = "_redirectUriTextBox";
        _redirectUriTextBox.Size = new Size(250, 23);
        _redirectUriTextBox.TabIndex = 5;
        _redirectUriTextBox.TextChanged += OnSettingChanged;
        // 
        // _redirectUriLabel
        // 
        _redirectUriLabel.AutoSize = true;
        _redirectUriLabel.Location = new Point(10, 85);
        _redirectUriLabel.Name = "_redirectUriLabel";
        _redirectUriLabel.Size = new Size(74, 15);
        _redirectUriLabel.TabIndex = 4;
        _redirectUriLabel.Text = "Redirect URI:";
        // 
        // _clientSecretTextBox
        // 
        _clientSecretTextBox.Location = new Point(95, 52);
        _clientSecretTextBox.Name = "_clientSecretTextBox";
        _clientSecretTextBox.Size = new Size(250, 23);
        _clientSecretTextBox.TabIndex = 3;
        _clientSecretTextBox.UseSystemPasswordChar = true;
        _clientSecretTextBox.TextChanged += OnSettingChanged;
        // 
        // _clientSecretLabel
        // 
        _clientSecretLabel.AutoSize = true;
        _clientSecretLabel.Location = new Point(10, 55);
        _clientSecretLabel.Name = "_clientSecretLabel";
        _clientSecretLabel.Size = new Size(76, 15);
        _clientSecretLabel.TabIndex = 2;
        _clientSecretLabel.Text = "Client Secret:";
        // 
        // _clientIdTextBox
        // 
        _clientIdTextBox.Location = new Point(95, 22);
        _clientIdTextBox.Name = "_clientIdTextBox";
        _clientIdTextBox.Size = new Size(250, 23);
        _clientIdTextBox.TabIndex = 1;
        _clientIdTextBox.UseSystemPasswordChar = true;
        _clientIdTextBox.TextChanged += OnSettingChanged;
        // 
        // _clientIdLabel
        // 
        _clientIdLabel.AutoSize = true;
        _clientIdLabel.Location = new Point(10, 25);
        _clientIdLabel.Name = "_clientIdLabel";
        _clientIdLabel.Size = new Size(55, 15);
        _clientIdLabel.TabIndex = 0;
        _clientIdLabel.Text = "Client ID:";
        // 
        // SettingsForm
        // 
        AcceptButton = _okButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = _cancelButton;
        ClientSize = new Size(584, 445);
        Controls.Add(_oauthGroupBox);
        Controls.Add(_throttlingPeriodNumeric);
        Controls.Add(_throttlingPeriodLabel);
        Controls.Add(_messagesAllowedNumeric);
        Controls.Add(_messagesAllowedLabel);
        Controls.Add(_channelTextBox);
        Controls.Add(_channelLabel);
        Controls.Add(_botUsernameTextBox);
        Controls.Add(_botUsernameLabel);
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
        _oauthGroupBox.ResumeLayout(false);
        _oauthGroupBox.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    private Button _okButton;
    private Button _cancelButton;
    private Button _applyButton;
    private Button _resetButton;
    private Label _botUsernameLabel;
    private TextBox _botUsernameTextBox;
    private Label _channelLabel;
    private TextBox _channelTextBox;
    private Label _messagesAllowedLabel;
    private NumericUpDown _messagesAllowedNumeric;
    private Label _throttlingPeriodLabel;
    private NumericUpDown _throttlingPeriodNumeric;
    private GroupBox _oauthGroupBox;
    private Label _clientIdLabel;
    private TextBox _clientIdTextBox;
    private Label _clientSecretLabel;
    private TextBox _clientSecretTextBox;
    private Label _redirectUriLabel;
    private TextBox _redirectUriTextBox;
    private Label _scopesLabel;
    private TextBox _scopesTextBox;
    private Label _oauthInfoLabel;

    #endregion
}
