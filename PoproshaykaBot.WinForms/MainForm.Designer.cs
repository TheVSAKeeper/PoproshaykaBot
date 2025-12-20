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
        _mainToolStrip = new ToolStrip();
        _logsToolStripButton = new ToolStripButton();
        _chatToolStripButton = new ToolStripButton();
        _chatViewToolStripButton = new ToolStripButton();
        _connectToolStripButton = new ToolStripButton();
        _toolStripSeparator1 = new ToolStripSeparator();
        _settingsToolStripButton = new ToolStripButton();
        _statsToolStripButton = new ToolStripButton();
        _broadcastToolStripButton = new ToolStripButton();
        _connectionProgressBar = new ToolStripProgressBar();
        _connectionStatusLabel = new ToolStripStatusLabel();
        _statusStrip = new StatusStrip();
        _streamStatusLabel = new Label();
        _streamInfoLabel = new Label();
        _contentTableLayoutPanel = new TableLayoutPanel();
        _logLabel = new Label();
        _chatDisplay = new ChatDisplay();
        _overlayWebView = new Microsoft.Web.WebView2.WinForms.WebView2();
        _logTextBox = new TextBox();
        _streamInfoTimer = new System.Windows.Forms.Timer(components);
        _mainTableLayoutPanel.SuspendLayout();
        _mainToolStrip.SuspendLayout();
        _statusStrip.SuspendLayout();
        _contentTableLayoutPanel.SuspendLayout();
        SuspendLayout();
        // 
        // _mainTableLayoutPanel
        // 
        _mainTableLayoutPanel.ColumnCount = 1;
        _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Controls.Add(_mainToolStrip, 0, 0);
        _mainTableLayoutPanel.Controls.Add(_streamStatusLabel, 0, 1);
        _mainTableLayoutPanel.Controls.Add(_streamInfoLabel, 0, 2);
        _mainTableLayoutPanel.Controls.Add(_contentTableLayoutPanel, 0, 3);
        _mainTableLayoutPanel.Dock = DockStyle.Fill;
        _mainTableLayoutPanel.Location = new Point(0, 0);
        _mainTableLayoutPanel.Name = "_mainTableLayoutPanel";
        _mainTableLayoutPanel.Padding = new Padding(12);
        _mainTableLayoutPanel.RowCount = 4;
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Size = new Size(785, 384);
        _mainTableLayoutPanel.TabIndex = 0;
        // 
        // _mainToolStrip
        // 
        _mainToolStrip.AutoSize = false;
        _mainToolStrip.BackColor = SystemColors.Control;
        _mainToolStrip.CanOverflow = false;
        _mainToolStrip.Dock = DockStyle.Fill;
        _mainToolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _mainToolStrip.ImageScalingSize = new Size(24, 24);
        _mainToolStrip.Items.AddRange(new ToolStripItem[] { _logsToolStripButton, _chatToolStripButton, _chatViewToolStripButton, _toolStripSeparator1, _connectToolStripButton, _settingsToolStripButton, _statsToolStripButton, _broadcastToolStripButton });
        _mainToolStrip.Location = new Point(15, 12);
        _mainToolStrip.Name = "_mainToolStrip";
        _mainToolStrip.Padding = new Padding(5, 0, 5, 0);
        _mainToolStrip.Size = new Size(755, 40);
        _mainToolStrip.TabIndex = 0;
        _mainToolStrip.Text = "Панель управления";
        // 
        // _logsToolStripButton
        // 
        _logsToolStripButton.AutoToolTip = false;
        _logsToolStripButton.CheckOnClick = true;
        _logsToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _logsToolStripButton.Name = "_logsToolStripButton";
        _logsToolStripButton.Size = new Size(50, 37);
        _logsToolStripButton.Text = "📜 Логи";
        _logsToolStripButton.ToolTipText = "Показать/скрыть панель логов (Alt+L)";
        _logsToolStripButton.Click += OnToggleLogsButtonClicked;
        // 
        // _chatToolStripButton
        // 
        _chatToolStripButton.CheckOnClick = true;
        _chatToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _chatToolStripButton.Name = "_chatToolStripButton";
        _chatToolStripButton.Size = new Size(51, 37);
        _chatToolStripButton.Text = "💬 Чат";
        _chatToolStripButton.ToolTipText = "Показать/скрыть панель чата (Alt+C)";
        _chatToolStripButton.Click += OnToggleChatButtonClicked;
        // 
        // _chatViewToolStripButton
        // 
        _chatViewToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _chatViewToolStripButton.Name = "_chatViewToolStripButton";
        _chatViewToolStripButton.Size = new Size(50, 37);
        _chatViewToolStripButton.Text = "👁️ Чат";
        _chatViewToolStripButton.ToolTipText = "Переключить режим отображения чата (Legacy/Overlay)";
        _chatViewToolStripButton.Click += OnSwitchChatViewButtonClicked;
        // 
        // _toolStripSeparator1
        // 
        _toolStripSeparator1.Name = "_toolStripSeparator1";
        _toolStripSeparator1.Size = new Size(6, 40);
        // 
        // _connectToolStripButton
        // 
        _connectToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _connectToolStripButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _connectToolStripButton.Name = "_connectToolStripButton";
        _connectToolStripButton.Size = new Size(116, 37);
        _connectToolStripButton.Text = "🔌 Подключить";
        _connectToolStripButton.Click += OnConnectButtonClicked;
        // 
        // _settingsToolStripButton
        // 
        _settingsToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _settingsToolStripButton.Name = "_settingsToolStripButton";
        _settingsToolStripButton.Size = new Size(95, 37);
        _settingsToolStripButton.Text = "⚙️ Настройки";
        _settingsToolStripButton.Click += OnSettingsButtonClicked;
        // 
        // _statsToolStripButton
        // 
        _statsToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _statsToolStripButton.Name = "_statsToolStripButton";
        _statsToolStripButton.Size = new Size(91, 37);
        _statsToolStripButton.Text = "📊 Статистика";
        _statsToolStripButton.ToolTipText = "Открыть окно статистики (Alt+U)";
        _statsToolStripButton.Click += OnUserStatisticsButtonClicked;
        // 
        // _broadcastToolStripButton
        // 
        _broadcastToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _broadcastToolStripButton.Enabled = false;
        _broadcastToolStripButton.Name = "_broadcastToolStripButton";
        _broadcastToolStripButton.Size = new Size(84, 37);
        _broadcastToolStripButton.Text = "📡 Рассылка";
        _broadcastToolStripButton.Click += OnBroadcastButtonClicked;
        // 
        // _connectionProgressBar
        // 
        _connectionProgressBar.MarqueeAnimationSpeed = 30;
        _connectionProgressBar.Name = "_connectionProgressBar";
        _connectionProgressBar.Size = new Size(100, 16);
        _connectionProgressBar.Style = ProgressBarStyle.Marquee;
        _connectionProgressBar.Visible = false;
        // 
        // _connectionStatusLabel
        // 
        _connectionStatusLabel.Name = "_connectionStatusLabel";
        _connectionStatusLabel.Size = new Size(0, 17);
        _connectionStatusLabel.Visible = false;
        // 
        // _statusStrip
        // 
        _statusStrip.Items.AddRange(new ToolStripItem[] { _connectionStatusLabel, _connectionProgressBar });
        _statusStrip.Location = new Point(0, 384);
        _statusStrip.Name = "_statusStrip";
        _statusStrip.Size = new Size(785, 22);
        _statusStrip.TabIndex = 1;
        _statusStrip.Text = "statusStrip1";
        // 
        // _streamStatusLabel
        // 
        _streamStatusLabel.AutoSize = true;
        _streamStatusLabel.Dock = DockStyle.Fill;
        _streamStatusLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _streamStatusLabel.Location = new Point(15, 52);
        _streamStatusLabel.Name = "_streamStatusLabel";
        _streamStatusLabel.Size = new Size(755, 25);
        _streamStatusLabel.TabIndex = 3;
        _streamStatusLabel.Text = "Статус стрима: Неизвестен";
        _streamStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // _streamInfoLabel
        // 
        _streamInfoLabel.AutoSize = true;
        _streamInfoLabel.Dock = DockStyle.Fill;
        _streamInfoLabel.Location = new Point(15, 77);
        _streamInfoLabel.Name = "_streamInfoLabel";
        _streamInfoLabel.Size = new Size(755, 25);
        _streamInfoLabel.TabIndex = 4;
        _streamInfoLabel.Text = "—";
        _streamInfoLabel.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // _contentTableLayoutPanel
        // 
        _contentTableLayoutPanel.ColumnCount = 2;
        _contentTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _contentTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _contentTableLayoutPanel.Controls.Add(_logLabel, 0, 0);
        _contentTableLayoutPanel.Controls.Add(_chatDisplay, 1, 0);
        _contentTableLayoutPanel.Controls.Add(_overlayWebView, 1, 0);
        _contentTableLayoutPanel.Controls.Add(_logTextBox, 0, 1);
        _contentTableLayoutPanel.Dock = DockStyle.Fill;
        _contentTableLayoutPanel.Location = new Point(15, 102);
        _contentTableLayoutPanel.Name = "_contentTableLayoutPanel";
        _contentTableLayoutPanel.RowCount = 2;
        _contentTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        _contentTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _contentTableLayoutPanel.Size = new Size(755, 270);
        _contentTableLayoutPanel.TabIndex = 5;
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
        _chatDisplay.Size = new Size(372, 233);
        _chatDisplay.TabIndex = 2;
        // 
        // _overlayWebView
        // 
        _overlayWebView.AllowExternalDrop = true;
        _overlayWebView.CreationProperties = null;
        _overlayWebView.DefaultBackgroundColor = Color.White;
        _overlayWebView.Dock = DockStyle.Fill;
        _overlayWebView.Location = new Point(380, 3);
        _overlayWebView.Name = "_overlayWebView";
        _contentTableLayoutPanel.SetRowSpan(_overlayWebView, 2);
        _overlayWebView.Size = new Size(372, 233);
        _overlayWebView.TabIndex = 9;
        _overlayWebView.ZoomFactor = 1D;
        _overlayWebView.Visible = false;
        // 
        // _logTextBox
        // 
        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Location = new Point(3, 28);
        _logTextBox.Multiline = true;
        _logTextBox.Name = "_logTextBox";
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.Size = new Size(371, 239);
        _logTextBox.TabIndex = 1;
        // 
        // _streamInfoTimer
        // 
        _streamInfoTimer.Interval = 60000;
        _streamInfoTimer.Tick += OnStreamInfoTimerTick;
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(785, 406);
        Controls.Add(_mainTableLayoutPanel);
        Controls.Add(_statusStrip);
        Icon = (Icon)resources.GetObject("$this.Icon");
        MinimumSize = new Size(600, 400);
        Name = "MainForm";
        Text = "Попрощайка Бот - Управление";
        _mainTableLayoutPanel.ResumeLayout(false);
        _mainTableLayoutPanel.PerformLayout();
        _mainToolStrip.ResumeLayout(false);
        _mainToolStrip.PerformLayout();
        _statusStrip.ResumeLayout(false);
        _statusStrip.PerformLayout();
        _contentTableLayoutPanel.ResumeLayout(false);
        _contentTableLayoutPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    private TableLayoutPanel _mainTableLayoutPanel;
    private ToolStrip _mainToolStrip;
    private ToolStripButton _logsToolStripButton;
    private ToolStripButton _chatToolStripButton;
    private ToolStripButton _chatViewToolStripButton;
    private ToolStripButton _connectToolStripButton;
    private ToolStripSeparator _toolStripSeparator1;
    private ToolStripButton _settingsToolStripButton;
    private ToolStripButton _statsToolStripButton;
    private ToolStripButton _broadcastToolStripButton;
    private TableLayoutPanel _contentTableLayoutPanel;
    private ToolStripProgressBar _connectionProgressBar;
    private ToolStripStatusLabel _connectionStatusLabel;
    private StatusStrip _statusStrip;
    private Label _streamStatusLabel;
    private Label _streamInfoLabel;
    private Label _logLabel;
    private TextBox _logTextBox;
    private ChatDisplay _chatDisplay;
    private Microsoft.Web.WebView2.WinForms.WebView2 _overlayWebView;
    private System.Windows.Forms.Timer _streamInfoTimer;

    #endregion
}
