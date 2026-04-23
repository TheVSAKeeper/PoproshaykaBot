using PoproshaykaBot.WinForms.Broadcast;
using PoproshaykaBot.WinForms.Chat;
using PoproshaykaBot.WinForms.Controls;
using PoproshaykaBot.WinForms.Streaming;

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
        _mainToolStrip = new ClickThroughToolStrip();
        _leftSlotLabel = new ToolStripLabel();
        _leftContentCombo = new ToolStripComboBox();
        _rightSlotLabel = new ToolStripLabel();
        _rightContentCombo = new ToolStripComboBox();
        _slotsSeparator = new ToolStripSeparator();
        _chatViewSeparator = new ToolStripSeparator();
        _chatViewToolStripButton = new ToolStripButton();
        _connectToolStripButton = new ToolStripButton();
        _settingsToolStripButton = new ToolStripButton();
        _statsToolStripButton = new ToolStripButton();
        _connectionProgressBar = new ToolStripProgressBar();
        _connectionStatusLabel = new ToolStripStatusLabel();
        _statusStrip = new StatusStrip();
        _widgetsTableLayoutPanel = new TableLayoutPanel();
        _streamInfoWidget = new StreamInfoWidget();
        _broadcastInfoWidget = new BroadcastInfoWidget();
        _broadcastProfileQuickPanel = new BroadcastProfileQuickPanel();
        _slotsTableLayoutPanel = new TableLayoutPanel();
        _leftSlot = new PoproshaykaBot.WinForms.Controls.PanelSlot();
        _rightSlot = new PoproshaykaBot.WinForms.Controls.PanelSlot();
        _chatHost = new Panel();
        _broadcastProfilesPanel = new PoproshaykaBot.WinForms.Broadcast.BroadcastProfilesPanel();
        _chatDisplay = new ChatDisplay();
        _overlayWebView = new Microsoft.Web.WebView2.WinForms.WebView2();
        _logTextBox = new TextBox();
        _streamInfoTimer = new System.Windows.Forms.Timer(components);
        _mainTableLayoutPanel.SuspendLayout();
        _mainToolStrip.SuspendLayout();
        _widgetsTableLayoutPanel.SuspendLayout();
        _slotsTableLayoutPanel.SuspendLayout();
        SuspendLayout();
        // 
        // _mainTableLayoutPanel
        // 
        _mainTableLayoutPanel.ColumnCount = 1;
        _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Controls.Add(_mainToolStrip, 0, 0);
        _mainTableLayoutPanel.Controls.Add(_widgetsTableLayoutPanel, 0, 1);
        _mainTableLayoutPanel.Controls.Add(_broadcastProfileQuickPanel, 0, 2);
        _mainTableLayoutPanel.Controls.Add(_slotsTableLayoutPanel, 0, 3);
        _mainTableLayoutPanel.Dock = DockStyle.Fill;
        _mainTableLayoutPanel.Location = new Point(0, 0);
        _mainTableLayoutPanel.Name = "_mainTableLayoutPanel";
        _mainTableLayoutPanel.Padding = new Padding(12);
        _mainTableLayoutPanel.RowCount = 4;
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Size = new Size(785, 430);
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
        _mainToolStrip.Items.AddRange(new ToolStripItem[]
        {
            _leftSlotLabel, _leftContentCombo, _rightSlotLabel, _rightContentCombo, _slotsSeparator,
            _connectToolStripButton, _settingsToolStripButton, _statsToolStripButton,
            _chatViewSeparator, _chatViewToolStripButton,
        });
        _mainToolStrip.Location = new Point(15, 12);
        _mainToolStrip.Name = "_mainToolStrip";
        _mainToolStrip.Padding = new Padding(5, 0, 5, 0);
        _mainToolStrip.Size = new Size(755, 40);
        _mainToolStrip.TabIndex = 0;
        _mainToolStrip.Text = "Панель управления";
        //
        // _leftSlotLabel
        //
        _leftSlotLabel.Name = "_leftSlotLabel";
        _leftSlotLabel.Text = "Слева:";
        //
        // _leftContentCombo
        //
        _leftContentCombo.Name = "_leftContentCombo";
        _leftContentCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _leftContentCombo.Size = new Size(130, 23);
        _leftContentCombo.ToolTipText = "Контент левого слота";
        //
        // _rightSlotLabel
        //
        _rightSlotLabel.Name = "_rightSlotLabel";
        _rightSlotLabel.Text = "Справа:";
        //
        // _rightContentCombo
        //
        _rightContentCombo.Name = "_rightContentCombo";
        _rightContentCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _rightContentCombo.Size = new Size(130, 23);
        _rightContentCombo.ToolTipText = "Контент правого слота";
        //
        // _chatViewToolStripButton
        //
        _chatViewToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _chatViewToolStripButton.Name = "_chatViewToolStripButton";
        _chatViewToolStripButton.Text = "👁️ Чат";
        _chatViewToolStripButton.ToolTipText = "Переключить режим отображения чата (Legacy/Overlay)";
        _chatViewToolStripButton.Visible = false;
        _chatViewToolStripButton.Click += OnSwitchChatViewButtonClicked;
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
        // 
        // _widgetsTableLayoutPanel
        // 
        _widgetsTableLayoutPanel.ColumnCount = 2;
        _widgetsTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _widgetsTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _widgetsTableLayoutPanel.Controls.Add(_streamInfoWidget, 0, 0);
        _widgetsTableLayoutPanel.Controls.Add(_broadcastInfoWidget, 1, 0);
        _widgetsTableLayoutPanel.Dock = DockStyle.Fill;
        _widgetsTableLayoutPanel.Location = new Point(15, 52);
        _widgetsTableLayoutPanel.Name = "_widgetsTableLayoutPanel";
        _widgetsTableLayoutPanel.RowCount = 1;
        _widgetsTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _widgetsTableLayoutPanel.Size = new Size(755, 160);
        _widgetsTableLayoutPanel.TabIndex = 3;
        // 
        // _streamInfoWidget
        // 
        _streamInfoWidget.Dock = DockStyle.Fill;
        _streamInfoWidget.Name = "_streamInfoWidget";
        _streamInfoWidget.Size = new Size(371, 124);
        //
        // _broadcastInfoWidget
        //
        _broadcastInfoWidget.Dock = DockStyle.Fill;
        _broadcastInfoWidget.Name = "_broadcastInfoWidget";
        _broadcastInfoWidget.Size = new Size(372, 124);
        //
        // _broadcastProfileQuickPanel
        //
        _broadcastProfileQuickPanel.Dock = DockStyle.Fill;
        _broadcastProfileQuickPanel.Name = "_broadcastProfileQuickPanel";
        _broadcastProfileQuickPanel.Size = new Size(755, 36);
        //
        // _slotsTableLayoutPanel
        //
        _slotsTableLayoutPanel.ColumnCount = 2;
        _slotsTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _slotsTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _slotsTableLayoutPanel.Controls.Add(_leftSlot, 0, 0);
        _slotsTableLayoutPanel.Controls.Add(_rightSlot, 1, 0);
        _slotsTableLayoutPanel.Dock = DockStyle.Fill;
        _slotsTableLayoutPanel.Location = new Point(15, 172);
        _slotsTableLayoutPanel.Name = "_slotsTableLayoutPanel";
        _slotsTableLayoutPanel.RowCount = 1;
        _slotsTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _slotsTableLayoutPanel.Size = new Size(755, 200);
        _slotsTableLayoutPanel.TabIndex = 5;
        //
        // _chatDisplay
        //
        _chatDisplay.Dock = DockStyle.Fill;
        _chatDisplay.Name = "_chatDisplay";
        _chatDisplay.Size = new Size(372, 233);
        _chatDisplay.TabIndex = 2;
        //
        // _overlayWebView
        //
        _overlayWebView.AllowExternalDrop = true;
        _overlayWebView.CreationProperties = null;
        _overlayWebView.DefaultBackgroundColor = Color.White;
        _overlayWebView.Dock = DockStyle.Fill;
        _overlayWebView.Name = "_overlayWebView";
        _overlayWebView.Size = new Size(372, 233);
        _overlayWebView.TabIndex = 9;
        _overlayWebView.ZoomFactor = 1D;
        _overlayWebView.Visible = false;
        //
        // _logTextBox
        //
        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Name = "_logTextBox";
        _logTextBox.Multiline = true;
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.Size = new Size(371, 239);
        _logTextBox.TabIndex = 1;
        //
        // _chatHost
        //
        _chatHost.Dock = DockStyle.Fill;
        _chatHost.Name = "_chatHost";
        _chatHost.Controls.Add(_chatDisplay);
        _chatHost.Controls.Add(_overlayWebView);
        //
        // _leftSlot
        //
        _leftSlot.Dock = DockStyle.Fill;
        _leftSlot.Name = "_leftSlot";
        _leftSlot.TabIndex = 0;
        //
        // _rightSlot
        //
        _rightSlot.Dock = DockStyle.Fill;
        _rightSlot.Name = "_rightSlot";
        _rightSlot.TabIndex = 1;
        //
        // _broadcastProfilesPanel
        //
        _broadcastProfilesPanel.Dock = DockStyle.Fill;
        _broadcastProfilesPanel.Name = "_broadcastProfilesPanel";
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
        ClientSize = new Size(785, 536);
        Controls.Add(_mainTableLayoutPanel);
        Controls.Add(_statusStrip);
        Icon = (Icon)resources.GetObject("$this.Icon");
        MinimumSize = new Size(600, 400);
        Name = "MainForm";
        Text = "Попрощайка Бот - Управление";
        _mainTableLayoutPanel.ResumeLayout(false);
        _mainTableLayoutPanel.PerformLayout();
        _widgetsTableLayoutPanel.ResumeLayout(false);
        _mainToolStrip.ResumeLayout(false);
        _mainToolStrip.PerformLayout();
        _statusStrip.ResumeLayout(false);
        _statusStrip.PerformLayout();
        _slotsTableLayoutPanel.ResumeLayout(false);
        _slotsTableLayoutPanel.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    private TableLayoutPanel _mainTableLayoutPanel;
    private ClickThroughToolStrip _mainToolStrip;
    private ToolStripButton _connectToolStripButton;
    private ToolStripButton _settingsToolStripButton;
    private ToolStripButton _statsToolStripButton;
    private TableLayoutPanel _widgetsTableLayoutPanel;
    private StreamInfoWidget _streamInfoWidget;
    private BroadcastInfoWidget _broadcastInfoWidget;
    private BroadcastProfileQuickPanel _broadcastProfileQuickPanel;
    private TableLayoutPanel _slotsTableLayoutPanel;
    private PoproshaykaBot.WinForms.Controls.PanelSlot _leftSlot;
    private PoproshaykaBot.WinForms.Controls.PanelSlot _rightSlot;
    private Panel _chatHost;
    private PoproshaykaBot.WinForms.Broadcast.BroadcastProfilesPanel _broadcastProfilesPanel;
    private ToolStripProgressBar _connectionProgressBar;
    private ToolStripStatusLabel _connectionStatusLabel;
    private StatusStrip _statusStrip;
    private TextBox _logTextBox;
    private ChatDisplay _chatDisplay;
    private Microsoft.Web.WebView2.WinForms.WebView2 _overlayWebView;
    private System.Windows.Forms.Timer _streamInfoTimer;
    private ToolStripLabel _leftSlotLabel;
    private ToolStripLabel _rightSlotLabel;
    private ToolStripComboBox _leftContentCombo;
    private ToolStripComboBox _rightContentCombo;
    private ToolStripSeparator _slotsSeparator;
    private ToolStripSeparator _chatViewSeparator;
    private ToolStripButton _chatViewToolStripButton;

    #endregion
}
