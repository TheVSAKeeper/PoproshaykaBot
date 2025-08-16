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
		components = new System.ComponentModel.Container();
        _mainTableLayoutPanel = new TableLayoutPanel();
        _buttonTableLayoutPanel = new TableLayoutPanel();
        _connectButton = new Button();
        _settingsButton = new Button();
        _broadcastButton = new Button();
        _toggleLogsButton = new Button();
        _toggleChatButton = new Button();
        _openChatWindowButton = new Button();
        _connectionProgressBar = new ProgressBar();
        _connectionStatusLabel = new Label();
        _streamStatusLabel = new Label();
        _streamInfoLabel = new Label();
        _contentTableLayoutPanel = new TableLayoutPanel();
        _logLabel = new Label();
        _chatDisplay = new ChatDisplay();
        _logTextBox = new TextBox();
		_streamInfoTimer = new System.Windows.Forms.Timer(components);
        _mainTableLayoutPanel.SuspendLayout();
        _buttonTableLayoutPanel.SuspendLayout();
        _contentTableLayoutPanel.SuspendLayout();
        SuspendLayout();
        // 
        // _mainTableLayoutPanel
        // 
        _mainTableLayoutPanel.ColumnCount = 1;
        _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Controls.Add(_buttonTableLayoutPanel, 0, 0);
        _mainTableLayoutPanel.Controls.Add(_connectionProgressBar, 0, 1);
        _mainTableLayoutPanel.Controls.Add(_connectionStatusLabel, 0, 2);
        _mainTableLayoutPanel.Controls.Add(_streamStatusLabel, 0, 3);
        _mainTableLayoutPanel.Controls.Add(_streamInfoLabel, 0, 4);
        _mainTableLayoutPanel.Controls.Add(_contentTableLayoutPanel, 0, 5);
        _mainTableLayoutPanel.Dock = DockStyle.Fill;
        _mainTableLayoutPanel.Location = new Point(0, 0);
        _mainTableLayoutPanel.Name = "_mainTableLayoutPanel";
        _mainTableLayoutPanel.Padding = new Padding(12);
        _mainTableLayoutPanel.RowCount = 6;
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Size = new Size(785, 406);
        _mainTableLayoutPanel.TabIndex = 0;
        //
        // _buttonTableLayoutPanel
        //
        _buttonTableLayoutPanel.ColumnCount = 7;
        _buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        _buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        _buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
        _buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        _buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90F));
        _buttonTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        _buttonTableLayoutPanel.Controls.Add(_toggleLogsButton, 0, 0);
        _buttonTableLayoutPanel.Controls.Add(_toggleChatButton, 1, 0);
        _buttonTableLayoutPanel.Controls.Add(_openChatWindowButton, 2, 0);
        _buttonTableLayoutPanel.Controls.Add(_connectButton, 4, 0);
        _buttonTableLayoutPanel.Controls.Add(_settingsButton, 5, 0);
        _buttonTableLayoutPanel.Controls.Add(_broadcastButton, 6, 0);
        _buttonTableLayoutPanel.Dock = DockStyle.Fill;
        _buttonTableLayoutPanel.Location = new Point(15, 15);
        _buttonTableLayoutPanel.Name = "_buttonTableLayoutPanel";
        _buttonTableLayoutPanel.RowCount = 1;
        _buttonTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _buttonTableLayoutPanel.Size = new Size(755, 44);
        _buttonTableLayoutPanel.TabIndex = 0;
        //
        // _toggleLogsButton
        //
        _toggleLogsButton.Dock = DockStyle.Fill;
        _toggleLogsButton.Location = new Point(3, 3);
        _toggleLogsButton.Name = "_toggleLogsButton";
        _toggleLogsButton.Size = new Size(74, 38);
        _toggleLogsButton.TabIndex = 0;
        _toggleLogsButton.Text = "Логи";
        _toggleLogsButton.UseVisualStyleBackColor = true;
        _toggleLogsButton.Click += OnToggleLogsButtonClicked;
        //
        // _toggleChatButton
        //
        _toggleChatButton.Dock = DockStyle.Fill;
        _toggleChatButton.Location = new Point(83, 3);
        _toggleChatButton.Name = "_toggleChatButton";
        _toggleChatButton.Size = new Size(74, 38);
        _toggleChatButton.TabIndex = 1;
        _toggleChatButton.Text = "Чат";
        _toggleChatButton.UseVisualStyleBackColor = true;
        _toggleChatButton.Click += OnToggleChatButtonClicked;
        //
        // _openChatWindowButton
        //
        _openChatWindowButton.Dock = DockStyle.Fill;
        _openChatWindowButton.Location = new Point(163, 3);
        _openChatWindowButton.Name = "_openChatWindowButton";
        _openChatWindowButton.Size = new Size(94, 38);
        _openChatWindowButton.TabIndex = 6;
        _openChatWindowButton.Text = "Чат в окне (Alt+W)";
        _openChatWindowButton.UseVisualStyleBackColor = true;
        _openChatWindowButton.Click += OnOpenChatWindowButtonClicked;
        //
        // _connectButton
        //
        _connectButton.Dock = DockStyle.Fill;
        _connectButton.Location = new Point(408, 3);
        _connectButton.Name = "_connectButton";
        _connectButton.Size = new Size(124, 38);
        _connectButton.TabIndex = 2;
        _connectButton.Text = "Подключить бота";
        _connectButton.UseVisualStyleBackColor = true;
        _connectButton.Click += OnConnectButtonClicked;
        // 
        // _settingsButton
        // 
        _settingsButton.Dock = DockStyle.Fill;
        _settingsButton.Location = new Point(538, 3);
        _settingsButton.Name = "_settingsButton";
        _settingsButton.Size = new Size(84, 38);
        _settingsButton.TabIndex = 3;
        _settingsButton.Text = "Настройки";
        _settingsButton.UseVisualStyleBackColor = true;
        _settingsButton.Click += OnSettingsButtonClicked;
        // 
        // _broadcastButton
        // 
        _broadcastButton.Dock = DockStyle.Fill;
        _broadcastButton.Enabled = false;
        _broadcastButton.Location = new Point(628, 3);
        _broadcastButton.Name = "_broadcastButton";
        _broadcastButton.Size = new Size(124, 38);
        _broadcastButton.TabIndex = 4;
        _broadcastButton.Text = "Рассылка недоступна";
        _broadcastButton.UseVisualStyleBackColor = true;
        _broadcastButton.Click += OnBroadcastButtonClicked;
        // 
        // _connectionProgressBar
        // 
        _connectionProgressBar.Dock = DockStyle.Fill;
        _connectionProgressBar.Location = new Point(15, 65);
        _connectionProgressBar.MarqueeAnimationSpeed = 30;
        _connectionProgressBar.Name = "_connectionProgressBar";
        _connectionProgressBar.Size = new Size(755, 19);
        _connectionProgressBar.Style = ProgressBarStyle.Marquee;
        _connectionProgressBar.TabIndex = 1;
        _connectionProgressBar.Visible = false;
        // 
        // _connectionStatusLabel
        // 
        _connectionStatusLabel.AutoSize = true;
        _connectionStatusLabel.Dock = DockStyle.Fill;
        _connectionStatusLabel.Location = new Point(15, 87);
        _connectionStatusLabel.Name = "_connectionStatusLabel";
        _connectionStatusLabel.Size = new Size(755, 25);
        _connectionStatusLabel.TabIndex = 2;
        _connectionStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _connectionStatusLabel.Visible = false;
        //
        // _streamStatusLabel
        //
        _streamStatusLabel.AutoSize = true;
        _streamStatusLabel.Dock = DockStyle.Fill;
        _streamStatusLabel.Location = new Point(15, 112);
        _streamStatusLabel.Name = "_streamStatusLabel";
        _streamStatusLabel.Size = new Size(755, 25);
        _streamStatusLabel.TabIndex = 3;
        _streamStatusLabel.Text = "Статус стрима: Неизвестен";
        _streamStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _contentTableLayoutPanel
        // 
        _contentTableLayoutPanel.ColumnCount = 2;
        _contentTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _contentTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _contentTableLayoutPanel.Controls.Add(_logLabel, 0, 0);
        _contentTableLayoutPanel.Controls.Add(_chatDisplay, 1, 0);
        _contentTableLayoutPanel.Controls.Add(_logTextBox, 0, 1);
        _contentTableLayoutPanel.Dock = DockStyle.Fill;
        _contentTableLayoutPanel.Location = new Point(15, 140);
        _contentTableLayoutPanel.Name = "_contentTableLayoutPanel";
        _contentTableLayoutPanel.RowCount = 2;
        _contentTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        _contentTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _contentTableLayoutPanel.Size = new Size(755, 251);
        _contentTableLayoutPanel.TabIndex = 3;
        // 
        // _streamInfoLabel
        // 
        _streamInfoLabel.AutoSize = true;
        _streamInfoLabel.Dock = DockStyle.Fill;
        _streamInfoLabel.Location = new Point(15, 137);
        _streamInfoLabel.Name = "_streamInfoLabel";
        _streamInfoLabel.Size = new Size(755, 25);
        _streamInfoLabel.TabIndex = 4;
        _streamInfoLabel.Text = "—";
        _streamInfoLabel.TextAlign = ContentAlignment.MiddleLeft;
		// 
		// _streamInfoTimer
		// 
		_streamInfoTimer.Interval = 60000;
		_streamInfoTimer.Tick += OnStreamInfoTimerTick;
        // 
        // _logLabel
        // 
        _logLabel.AutoSize = true;
        _logLabel.Dock = DockStyle.Fill;
        _logLabel.Location = new Point(3, 0);
        _logLabel.Name = "_logLabel";
        _logLabel.Size = new Size(371, 25);
        _logLabel.TabIndex = 0;
        _logLabel.Text = "Логи:";
        _logLabel.TextAlign = ContentAlignment.BottomLeft;
        // 
        // _chatDisplay
        // 
        _chatDisplay.Dock = DockStyle.Fill;
        _chatDisplay.Location = new Point(380, 3);
        _chatDisplay.Name = "_chatDisplay";
        _contentTableLayoutPanel.SetRowSpan(_chatDisplay, 2);
        _chatDisplay.Size = new Size(372, 270);
        _chatDisplay.TabIndex = 2;
        // 
        // _logTextBox
        // 
        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Location = new Point(3, 28);
        _logTextBox.Multiline = true;
        _logTextBox.Name = "_logTextBox";
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.Size = new Size(371, 245);
        _logTextBox.TabIndex = 1;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(785, 406);
        Controls.Add(_mainTableLayoutPanel);
        Icon = (Icon)resources.GetObject("$this.Icon");
        MinimumSize = new Size(600, 400);
        Name = "MainForm";
        Text = "Попрощайка Бот - Управление";
        _mainTableLayoutPanel.ResumeLayout(false);
        _mainTableLayoutPanel.PerformLayout();
        _buttonTableLayoutPanel.ResumeLayout(false);
        _contentTableLayoutPanel.ResumeLayout(false);
        _contentTableLayoutPanel.PerformLayout();
        ResumeLayout(false);
    }

    private TableLayoutPanel _mainTableLayoutPanel;
    private TableLayoutPanel _buttonTableLayoutPanel;
    private TableLayoutPanel _contentTableLayoutPanel;
    private Button _toggleLogsButton;
    private Button _toggleChatButton;
    private Button _openChatWindowButton;
    private Button _connectButton;
    private Button _settingsButton;
    private Button _broadcastButton;
    private ProgressBar _connectionProgressBar;
    private Label _connectionStatusLabel;
    private Label _streamStatusLabel;
    private Label _streamInfoLabel;
    private Label _logLabel;
    private TextBox _logTextBox;
    private ChatDisplay _chatDisplay;
	private System.Windows.Forms.Timer _streamInfoTimer;

    #endregion
}
