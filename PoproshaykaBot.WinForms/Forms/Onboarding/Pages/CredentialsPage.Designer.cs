namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

partial class CredentialsPage
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Component Designer generated code

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _layout = new TableLayoutPanel();
        _channelLabel = new Label();
        _channelTextBox = new TextBox();
        _autoDetectChannelCheckBox = new CheckBox();
        _channelStatusLabel = new Label();
        _clientIdLabel = new Label();
        _clientIdTextBox = new TextBox();
        _clientSecretLabel = new Label();
        _clientSecretTextBox = new TextBox();
        _clientStatusLabel = new Label();
        _redirectUriLabel = new Label();
        _redirectUriTextBox = new TextBox();
        _portHintLabel = new Label();
        _validationLabel = new Label();
        _validationTimer = new System.Windows.Forms.Timer(components);
        _channelValidationTimer = new System.Windows.Forms.Timer(components);
        _layout.SuspendLayout();
        SuspendLayout();
        //
        // _layout
        //
        _layout.ColumnCount = 2;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_channelLabel, 0, 0);
        _layout.Controls.Add(_channelTextBox, 1, 0);
        _layout.Controls.Add(_autoDetectChannelCheckBox, 1, 1);
        _layout.Controls.Add(_channelStatusLabel, 0, 2);
        _layout.SetColumnSpan(_channelStatusLabel, 2);
        _layout.Controls.Add(_clientIdLabel, 0, 3);
        _layout.Controls.Add(_clientIdTextBox, 1, 3);
        _layout.Controls.Add(_clientSecretLabel, 0, 4);
        _layout.Controls.Add(_clientSecretTextBox, 1, 4);
        _layout.Controls.Add(_clientStatusLabel, 0, 5);
        _layout.SetColumnSpan(_clientStatusLabel, 2);
        _layout.Controls.Add(_redirectUriLabel, 0, 6);
        _layout.Controls.Add(_redirectUriTextBox, 1, 6);
        _layout.Controls.Add(_portHintLabel, 1, 7);
        _layout.Controls.Add(_validationLabel, 0, 8);
        _layout.SetColumnSpan(_validationLabel, 2);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(20, 18, 20, 18);
        _layout.RowCount = 10;
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _channelLabel
        //
        _channelLabel.AutoSize = false;
        _channelLabel.Dock = DockStyle.Fill;
        _channelLabel.Name = "_channelLabel";
        _channelLabel.Text = "Канал:";
        _channelLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _channelTextBox
        //
        _channelTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _channelTextBox.Margin = new Padding(0, 6, 0, 6);
        _channelTextBox.Name = "_channelTextBox";
        _channelTextBox.PlaceholderText = "название_канала";
        _channelTextBox.TextChanged += OnInputChanged;
        //
        // _autoDetectChannelCheckBox
        //
        _autoDetectChannelCheckBox.AutoSize = true;
        _autoDetectChannelCheckBox.Anchor = AnchorStyles.Left;
        _autoDetectChannelCheckBox.Checked = true;
        _autoDetectChannelCheckBox.CheckState = CheckState.Checked;
        _autoDetectChannelCheckBox.Margin = new Padding(0, 0, 0, 6);
        _autoDetectChannelCheckBox.Name = "_autoDetectChannelCheckBox";
        _autoDetectChannelCheckBox.Text = "Определить автоматически по аккаунту стримера";
        _autoDetectChannelCheckBox.UseVisualStyleBackColor = true;
        _autoDetectChannelCheckBox.CheckedChanged += OnAutoDetectChannelCheckedChanged;
        //
        // _channelStatusLabel
        //
        _channelStatusLabel.AutoSize = true;
        _channelStatusLabel.Dock = DockStyle.Fill;
        _channelStatusLabel.ForeColor = Color.Gray;
        _channelStatusLabel.Margin = new Padding(0, 0, 0, 4);
        _channelStatusLabel.Name = "_channelStatusLabel";
        _channelStatusLabel.Text = "";
        _channelStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _clientIdLabel
        //
        _clientIdLabel.AutoSize = false;
        _clientIdLabel.Dock = DockStyle.Fill;
        _clientIdLabel.Name = "_clientIdLabel";
        _clientIdLabel.Text = "Client ID:";
        _clientIdLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _clientIdTextBox
        //
        _clientIdTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _clientIdTextBox.Margin = new Padding(0, 6, 0, 6);
        _clientIdTextBox.Name = "_clientIdTextBox";
        _clientIdTextBox.PlaceholderText = "Скопируйте из консоли Twitch Dev";
        _clientIdTextBox.UseSystemPasswordChar = true;
        _clientIdTextBox.TextChanged += OnInputChanged;
        //
        // _clientSecretLabel
        //
        _clientSecretLabel.AutoSize = false;
        _clientSecretLabel.Dock = DockStyle.Fill;
        _clientSecretLabel.Name = "_clientSecretLabel";
        _clientSecretLabel.Text = "Client Secret:";
        _clientSecretLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _clientSecretTextBox
        //
        _clientSecretTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _clientSecretTextBox.Margin = new Padding(0, 6, 0, 6);
        _clientSecretTextBox.Name = "_clientSecretTextBox";
        _clientSecretTextBox.PlaceholderText = "Сгенерируйте в консоли Twitch Dev";
        _clientSecretTextBox.UseSystemPasswordChar = true;
        _clientSecretTextBox.TextChanged += OnInputChanged;
        //
        // _clientStatusLabel
        //
        _clientStatusLabel.AutoSize = true;
        _clientStatusLabel.Dock = DockStyle.Fill;
        _clientStatusLabel.ForeColor = Color.Gray;
        _clientStatusLabel.Margin = new Padding(0, 4, 0, 4);
        _clientStatusLabel.Name = "_clientStatusLabel";
        _clientStatusLabel.Text = "";
        _clientStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _redirectUriLabel
        //
        _redirectUriLabel.AutoSize = false;
        _redirectUriLabel.Dock = DockStyle.Fill;
        _redirectUriLabel.Name = "_redirectUriLabel";
        _redirectUriLabel.Text = "Redirect URI:";
        _redirectUriLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _redirectUriTextBox
        //
        _redirectUriTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _redirectUriTextBox.Margin = new Padding(0, 6, 0, 6);
        _redirectUriTextBox.Name = "_redirectUriTextBox";
        _redirectUriTextBox.PlaceholderText = "http://localhost:3000";
        _redirectUriTextBox.TextChanged += OnInputChanged;
        //
        // _portHintLabel
        //
        _portHintLabel.AutoSize = false;
        _portHintLabel.Dock = DockStyle.Fill;
        _portHintLabel.ForeColor = Color.Gray;
        _portHintLabel.Name = "_portHintLabel";
        _portHintLabel.Text = "";
        _portHintLabel.TextAlign = ContentAlignment.TopLeft;
        //
        // _validationLabel
        //
        _validationLabel.AutoSize = true;
        _validationLabel.Dock = DockStyle.Fill;
        _validationLabel.ForeColor = Color.DarkRed;
        _validationLabel.Margin = new Padding(0, 8, 0, 0);
        _validationLabel.Name = "_validationLabel";
        _validationLabel.Text = "";
        _validationLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _validationTimer
        //
        _validationTimer.Interval = 800;
        _validationTimer.Tick += OnValidationTimerTick;
        //
        // _channelValidationTimer
        //
        _channelValidationTimer.Interval = 800;
        _channelValidationTimer.Tick += OnChannelValidationTimerTick;
        //
        // CredentialsPage
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_layout);
        Name = "CredentialsPage";
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private TableLayoutPanel _layout;
    private Label _channelLabel;
    private TextBox _channelTextBox;
    private CheckBox _autoDetectChannelCheckBox;
    private Label _channelStatusLabel;
    private Label _clientIdLabel;
    private TextBox _clientIdTextBox;
    private Label _clientSecretLabel;
    private TextBox _clientSecretTextBox;
    private Label _clientStatusLabel;
    private Label _redirectUriLabel;
    private TextBox _redirectUriTextBox;
    private Label _portHintLabel;
    private Label _validationLabel;
    private System.Windows.Forms.Timer _validationTimer;
    private System.Windows.Forms.Timer _channelValidationTimer;
}
