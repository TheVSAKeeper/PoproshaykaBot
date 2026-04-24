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
        components = new System.ComponentModel.Container();
        var resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
        _mainTableLayoutPanel = new TableLayoutPanel();
        _mainToolStrip = new ClickThroughToolStrip();
        _leftSlotLabel = new ToolStripLabel();
        _leftContentCombo = new ToolStripComboBox();
        _rightSlotLabel = new ToolStripLabel();
        _rightContentCombo = new ToolStripComboBox();
        _slotsSeparator = new ToolStripSeparator();
        _connectToolStripButton = new ToolStripButton();
        _settingsToolStripButton = new ToolStripButton();
        _statsToolStripButton = new ToolStripButton();
        _widgetsTableLayoutPanel = new TableLayoutPanel();
        _streamInfoWidget = new StreamInfoWidget();
        _broadcastInfoWidget = new BroadcastInfoWidget();
        _slotsTableLayoutPanel = new TableLayoutPanel();
        _leftSlot = new PanelSlot();
        _rightSlot = new PanelSlot();
        _connectionProgressBar = new ToolStripProgressBar();
        _connectionStatusLabel = new ToolStripStatusLabel();
        _statusStrip = new StatusStrip();
        _chatDisplay = new ChatDisplay();
        _overlayWebView = new Microsoft.Web.WebView2.WinForms.WebView2();
        _broadcastProfilesPanel = new BroadcastProfilesPanel();
        _logTextBox = new TextBox();
        _streamInfoTimer = new System.Windows.Forms.Timer(components);
        _mainTableLayoutPanel.SuspendLayout();
        _mainToolStrip.SuspendLayout();
        _widgetsTableLayoutPanel.SuspendLayout();
        _slotsTableLayoutPanel.SuspendLayout();
        _statusStrip.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_overlayWebView).BeginInit();
        SuspendLayout();
        // 
        // _mainTableLayoutPanel
        // 
        _mainTableLayoutPanel.ColumnCount = 1;
        _mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Controls.Add(_mainToolStrip, 0, 0);
        _mainTableLayoutPanel.Controls.Add(_widgetsTableLayoutPanel, 0, 1);
        _mainTableLayoutPanel.Controls.Add(_slotsTableLayoutPanel, 0, 2);
        _mainTableLayoutPanel.Dock = DockStyle.Fill;
        _mainTableLayoutPanel.Location = new Point(0, 0);
        _mainTableLayoutPanel.Name = "_mainTableLayoutPanel";
        _mainTableLayoutPanel.Padding = new Padding(12, 12, 12, 12);
        _mainTableLayoutPanel.RowCount = 3;
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));
        _mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _mainTableLayoutPanel.Size = new Size(800, 562);
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
        _mainToolStrip.Items.AddRange(new ToolStripItem[] { _leftSlotLabel, _leftContentCombo, _rightSlotLabel, _rightContentCombo, _slotsSeparator, _connectToolStripButton, _settingsToolStripButton, _statsToolStripButton });
        _mainToolStrip.Location = new Point(12, 12);
        _mainToolStrip.Name = "_mainToolStrip";
        _mainToolStrip.Padding = new Padding(5, 0, 5, 0);
        _mainToolStrip.Size = new Size(776, 40);
        _mainToolStrip.TabIndex = 0;
        _mainToolStrip.Text = "Панель управления";
        // 
        // _leftSlotLabel
        // 
        _leftSlotLabel.Name = "_leftSlotLabel";
        _leftSlotLabel.Size = new Size(43, 37);
        _leftSlotLabel.Text = "Слева:";
        // 
        // _leftContentCombo
        // 
        _leftContentCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _leftContentCombo.Name = "_leftContentCombo";
        _leftContentCombo.Size = new Size(130, 40);
        _leftContentCombo.ToolTipText = "Контент левого слота";
        // 
        // _rightSlotLabel
        // 
        _rightSlotLabel.Name = "_rightSlotLabel";
        _rightSlotLabel.Size = new Size(50, 37);
        _rightSlotLabel.Text = "Справа:";
        // 
        // _rightContentCombo
        // 
        _rightContentCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _rightContentCombo.Name = "_rightContentCombo";
        _rightContentCombo.Size = new Size(130, 40);
        _rightContentCombo.ToolTipText = "Контент правого слота";
        // 
        // _slotsSeparator
        // 
        _slotsSeparator.Name = "_slotsSeparator";
        _slotsSeparator.Size = new Size(6, 40);
        // 
        // _connectToolStripButton
        // 
        _connectToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _connectToolStripButton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _connectToolStripButton.Name = "_connectToolStripButton";
        _connectToolStripButton.Size = new Size(101, 37);
        _connectToolStripButton.Text = "🔌 Подключить";
        _connectToolStripButton.Click += OnConnectButtonClicked;
        // 
        // _settingsToolStripButton
        // 
        _settingsToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _settingsToolStripButton.Name = "_settingsToolStripButton";
        _settingsToolStripButton.Size = new Size(86, 37);
        _settingsToolStripButton.Text = "⚙️ Настройки";
        _settingsToolStripButton.Click += OnSettingsButtonClicked;
        // 
        // _statsToolStripButton
        // 
        _statsToolStripButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _statsToolStripButton.Name = "_statsToolStripButton";
        _statsToolStripButton.Size = new Size(87, 37);
        _statsToolStripButton.Text = "📊 Статистика";
        _statsToolStripButton.ToolTipText = "Открыть окно статистики (Alt+U)";
        _statsToolStripButton.Click += OnUserStatisticsButtonClicked;
        //
        // _widgetsTableLayoutPanel
        // 
        _widgetsTableLayoutPanel.ColumnCount = 2;
        _widgetsTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _widgetsTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _widgetsTableLayoutPanel.Controls.Add(_streamInfoWidget, 0, 0);
        _widgetsTableLayoutPanel.Controls.Add(_broadcastInfoWidget, 1, 0);
        _widgetsTableLayoutPanel.Dock = DockStyle.Fill;
        _widgetsTableLayoutPanel.Location = new Point(15, 55);
        _widgetsTableLayoutPanel.Name = "_widgetsTableLayoutPanel";
        _widgetsTableLayoutPanel.RowCount = 1;
        _widgetsTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _widgetsTableLayoutPanel.Size = new Size(770, 154);
        _widgetsTableLayoutPanel.TabIndex = 3;
        // 
        // _streamInfoWidget
        // 
        _streamInfoWidget.BackColor = Color.White;
        _streamInfoWidget.BorderStyle = BorderStyle.FixedSingle;
        _streamInfoWidget.Dock = DockStyle.Fill;
        _streamInfoWidget.Location = new Point(3, 3);
        _streamInfoWidget.Name = "_streamInfoWidget";
        _streamInfoWidget.Size = new Size(379, 148);
        _streamInfoWidget.TabIndex = 0;
        // 
        // _broadcastInfoWidget
        // 
        _broadcastInfoWidget.BackColor = Color.White;
        _broadcastInfoWidget.BorderStyle = BorderStyle.FixedSingle;
        _broadcastInfoWidget.Dock = DockStyle.Fill;
        _broadcastInfoWidget.Location = new Point(388, 3);
        _broadcastInfoWidget.Name = "_broadcastInfoWidget";
        _broadcastInfoWidget.Size = new Size(379, 148);
        _broadcastInfoWidget.TabIndex = 1;
        //
        // _slotsTableLayoutPanel
        // 
        _slotsTableLayoutPanel.ColumnCount = 2;
        _slotsTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _slotsTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        _slotsTableLayoutPanel.Controls.Add(_leftSlot, 0, 0);
        _slotsTableLayoutPanel.Controls.Add(_rightSlot, 1, 0);
        _slotsTableLayoutPanel.Dock = DockStyle.Fill;
        _slotsTableLayoutPanel.Location = new Point(15, 251);
        _slotsTableLayoutPanel.Name = "_slotsTableLayoutPanel";
        _slotsTableLayoutPanel.RowCount = 1;
        _slotsTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _slotsTableLayoutPanel.Size = new Size(770, 296);
        _slotsTableLayoutPanel.TabIndex = 5;
        // 
        // _leftSlot
        // 
        _leftSlot.Dock = DockStyle.Fill;
        _leftSlot.Location = new Point(3, 3);
        _leftSlot.Name = "_leftSlot";
        _leftSlot.Size = new Size(379, 290);
        _leftSlot.TabIndex = 0;
        // 
        // _rightSlot
        // 
        _rightSlot.Dock = DockStyle.Fill;
        _rightSlot.Location = new Point(388, 3);
        _rightSlot.Name = "_rightSlot";
        _rightSlot.Size = new Size(379, 290);
        _rightSlot.TabIndex = 1;
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
        _statusStrip.ImageScalingSize = new Size(36, 36);
        _statusStrip.Items.AddRange(new ToolStripItem[] { _connectionStatusLabel, _connectionProgressBar });
        _statusStrip.Location = new Point(0, 562);
        _statusStrip.Name = "_statusStrip";
        _statusStrip.Size = new Size(800, 22);
        _statusStrip.TabIndex = 1;
        _statusStrip.Text = "statusStrip1";
        //
        // _chatDisplay
        // 
        _chatDisplay.Dock = DockStyle.Fill;
        _chatDisplay.Location = new Point(0, 0);
        _chatDisplay.Margin = new Padding(6, 7, 6, 7);
        _chatDisplay.Name = "_chatDisplay";
        _chatDisplay.Size = new Size(200, 100);
        _chatDisplay.TabIndex = 2;
        // 
        // _overlayWebView
        // 
        _overlayWebView.AllowExternalDrop = true;
        _overlayWebView.CreationProperties = null;
        _overlayWebView.DefaultBackgroundColor = Color.White;
        _overlayWebView.Dock = DockStyle.Fill;
        _overlayWebView.Location = new Point(0, 0);
        _overlayWebView.Name = "_overlayWebView";
        _overlayWebView.Size = new Size(200, 100);
        _overlayWebView.TabIndex = 9;
        _overlayWebView.ZoomFactor = 1D;
        // 
        // _broadcastProfilesPanel
        // 
        _broadcastProfilesPanel.Dock = DockStyle.Fill;
        _broadcastProfilesPanel.Location = new Point(0, 0);
        _broadcastProfilesPanel.MinimumSize = new Size(360, 200);
        _broadcastProfilesPanel.Name = "_broadcastProfilesPanel";
        _broadcastProfilesPanel.Size = new Size(700, 440);
        _broadcastProfilesPanel.TabIndex = 0;
        // 
        // _logTextBox
        // 
        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.Location = new Point(0, 0);
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
        ClientSize = new Size(800, 584);
        Controls.Add(_mainTableLayoutPanel);
        Controls.Add(_statusStrip);
        Icon = (Icon)resources.GetObject("$this.Icon");
        MinimumSize = new Size(594, 376);
        Name = "MainForm";
        Text = "Попрощайка Бот - Управление";
        _mainTableLayoutPanel.ResumeLayout(false);
        _mainToolStrip.ResumeLayout(false);
        _mainToolStrip.PerformLayout();
        _widgetsTableLayoutPanel.ResumeLayout(false);
        _slotsTableLayoutPanel.ResumeLayout(false);
        _statusStrip.ResumeLayout(false);
        _statusStrip.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_overlayWebView).EndInit();
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
    private TableLayoutPanel _slotsTableLayoutPanel;
    private PoproshaykaBot.WinForms.Controls.PanelSlot _leftSlot;
    private PoproshaykaBot.WinForms.Controls.PanelSlot _rightSlot;
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

    #endregion
}
