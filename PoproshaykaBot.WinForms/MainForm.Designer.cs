namespace PoproshaykaBot.WinForms;

partial class MainForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
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
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        var resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
        _connectButton = new Button();
        _logTextBox = new TextBox();
        _logLabel = new Label();
        _chatDisplay = new ChatDisplay();
        _settingsButton = new Button();
        _broadcastButton = new Button();
        _connectionProgressBar = new ProgressBar();
        _connectionStatusLabel = new Label();
        SuspendLayout();
        // 
        // _connectButton
        // 
        _connectButton.Location = new Point(350, 20);
        _connectButton.Name = "_connectButton";
        _connectButton.Size = new Size(120, 40);
        _connectButton.TabIndex = 0;
        _connectButton.Text = "Подключить бота";
        _connectButton.UseVisualStyleBackColor = true;
        _connectButton.Click += OnConnectButtonClicked;
        //
        // _logTextBox
        //
        _logTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        _logTextBox.Location = new Point(12, 100);
        _logTextBox.Multiline = true;
        _logTextBox.Name = "_logTextBox";
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.Size = new Size(375, 294);
        _logTextBox.TabIndex = 2;
        //
        // _logLabel
        //
        _logLabel.AutoSize = true;
        _logLabel.Location = new Point(12, 80);
        _logLabel.Name = "_logLabel";
        _logLabel.Size = new Size(37, 15);
        _logLabel.TabIndex = 1;
        _logLabel.Text = "Логи:";
        //
        // _chatDisplay
        //
        _chatDisplay.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right;
        _chatDisplay.Location = new Point(398, 80);
        _chatDisplay.Name = "_chatDisplay";
        _chatDisplay.Size = new Size(375, 314);
        _chatDisplay.TabIndex = 6;
        // 
        // _settingsButton
        // 
        _settingsButton.Location = new Point(480, 20);
        _settingsButton.Name = "_settingsButton";
        _settingsButton.Size = new Size(80, 40);
        _settingsButton.TabIndex = 3;
        _settingsButton.Text = "Настройки";
        _settingsButton.UseVisualStyleBackColor = true;
        _settingsButton.Click += OnSettingsButtonClicked;
        // 
        // _broadcastButton
        // 
        _broadcastButton.Enabled = false;
        _broadcastButton.Location = new Point(570, 20);
        _broadcastButton.Name = "_broadcastButton";
        _broadcastButton.Size = new Size(120, 40);
        _broadcastButton.TabIndex = 4;
        _broadcastButton.Text = "Рассылка недоступна";
        _broadcastButton.UseVisualStyleBackColor = true;
        _broadcastButton.Click += OnBroadcastButtonClicked;
        // 
        // _connectionProgressBar
        // 
        _connectionProgressBar.Location = new Point(12, 20);
        _connectionProgressBar.MarqueeAnimationSpeed = 30;
        _connectionProgressBar.Name = "_connectionProgressBar";
        _connectionProgressBar.Size = new Size(320, 20);
        _connectionProgressBar.Style = ProgressBarStyle.Marquee;
        _connectionProgressBar.TabIndex = 4;
        _connectionProgressBar.Visible = false;
        // 
        // _connectionStatusLabel
        // 
        _connectionStatusLabel.AutoSize = true;
        _connectionStatusLabel.Location = new Point(12, 45);
        _connectionStatusLabel.Name = "_connectionStatusLabel";
        _connectionStatusLabel.Size = new Size(0, 15);
        _connectionStatusLabel.TabIndex = 5;
        _connectionStatusLabel.Visible = false;
        //
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(785, 406);
        Controls.Add(_connectButton);
        Controls.Add(_settingsButton);
        Controls.Add(_broadcastButton);
        Controls.Add(_logLabel);
        Controls.Add(_logTextBox);
        Controls.Add(_chatDisplay);
        Controls.Add(_connectionProgressBar);
        Controls.Add(_connectionStatusLabel);
        Icon = (Icon)resources.GetObject("$this.Icon");
        Name = "MainForm";
        Text = "Попрощайка Бот - Управление";
        ResumeLayout(false);
        PerformLayout();
    }

    private Button _connectButton;
    private TextBox _logTextBox;
    private Label _logLabel;
    private ChatDisplay _chatDisplay;
    private Button _settingsButton;
    private Button _broadcastButton;
    private ProgressBar _connectionProgressBar;
    private Label _connectionStatusLabel;

    #endregion
}
