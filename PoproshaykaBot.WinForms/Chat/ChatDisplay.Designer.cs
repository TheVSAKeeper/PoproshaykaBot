namespace PoproshaykaBot.WinForms.Chat;

partial class ChatDisplay
{
    /// <summary>
    /// Обязательная переменная конструктора.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Освободить все используемые ресурсы.
    /// </summary>
    /// <param name="disposing">истинно, если управляемый ресурс должен быть удален; иначе ложно.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Код, автоматически созданный конструктором компонентов

    /// <summary>
    /// Требуемый метод для поддержки конструктора — не изменяйте
    /// содержимое этого метода с помощью редактора кода.
    /// </summary>
    private void InitializeComponent()
    {
        _webView = new Microsoft.Web.WebView2.WinForms.WebView2();
        _fallbackPanel = new Panel();
        _fallbackLabel = new Label();
        _fallbackLink = new LinkLabel();
        _toolStrip = new ToolStrip();
        _reloadButton = new ToolStripButton();
        _resetZoomButton = new ToolStripButton();
        _openInBrowserButton = new ToolStripButton();
        _resetSessionButton = new ToolStripButton();
        ((System.ComponentModel.ISupportInitialize)_webView).BeginInit();
        _fallbackPanel.SuspendLayout();
        _toolStrip.SuspendLayout();
        SuspendLayout();
        //
        // _webView
        //
        _webView.AllowExternalDrop = true;
        _webView.CreationProperties = null;
        _webView.DefaultBackgroundColor = Color.White;
        _webView.Dock = DockStyle.Fill;
        _webView.Location = new Point(0, 28);
        _webView.Name = "_webView";
        _webView.Size = new Size(375, 266);
        _webView.TabIndex = 0;
        _webView.ZoomFactor = 1D;
        //
        // _fallbackPanel
        //
        _fallbackPanel.Controls.Add(_fallbackLabel);
        _fallbackPanel.Controls.Add(_fallbackLink);
        _fallbackPanel.Dock = DockStyle.Fill;
        _fallbackPanel.Location = new Point(0, 28);
        _fallbackPanel.Name = "_fallbackPanel";
        _fallbackPanel.Padding = new Padding(12);
        _fallbackPanel.Size = new Size(375, 266);
        _fallbackPanel.TabIndex = 1;
        _fallbackPanel.Visible = false;
        //
        // _fallbackLabel
        //
        _fallbackLabel.Dock = DockStyle.Fill;
        _fallbackLabel.Location = new Point(12, 12);
        _fallbackLabel.Name = "_fallbackLabel";
        _fallbackLabel.Size = new Size(351, 218);
        _fallbackLabel.TabIndex = 0;
        _fallbackLabel.Text = "Инициализация чата...";
        _fallbackLabel.TextAlign = ContentAlignment.MiddleCenter;
        //
        // _fallbackLink
        //
        _fallbackLink.Dock = DockStyle.Bottom;
        _fallbackLink.Location = new Point(12, 230);
        _fallbackLink.Name = "_fallbackLink";
        _fallbackLink.Size = new Size(351, 24);
        _fallbackLink.TabIndex = 1;
        _fallbackLink.TabStop = true;
        _fallbackLink.TextAlign = ContentAlignment.MiddleCenter;
        _fallbackLink.Visible = false;
        _fallbackLink.LinkClicked += OnFallbackLinkClicked;
        //
        // _toolStrip
        //
        _toolStrip.BackColor = SystemColors.Control;
        _toolStrip.CanOverflow = false;
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.ImageScalingSize = new Size(20, 20);
        _toolStrip.Items.AddRange(new ToolStripItem[] { _reloadButton, _resetZoomButton, _openInBrowserButton, _resetSessionButton });
        _toolStrip.Location = new Point(0, 0);
        _toolStrip.Name = "_toolStrip";
        _toolStrip.Padding = new Padding(4, 0, 4, 0);
        _toolStrip.Size = new Size(375, 28);
        _toolStrip.TabIndex = 2;
        //
        // _reloadButton
        //
        _reloadButton.AutoToolTip = false;
        _reloadButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _reloadButton.Name = "_reloadButton";
        _reloadButton.Size = new Size(28, 25);
        _reloadButton.Text = "⟳";
        _reloadButton.ToolTipText = "Перезагрузить страницу чата";
        _reloadButton.Click += OnReloadClicked;
        //
        // _resetZoomButton
        //
        _resetZoomButton.AutoToolTip = false;
        _resetZoomButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _resetZoomButton.Name = "_resetZoomButton";
        _resetZoomButton.Size = new Size(28, 25);
        _resetZoomButton.Text = "🔍";
        _resetZoomButton.ToolTipText = "Сбросить масштаб к значению по умолчанию";
        _resetZoomButton.Click += OnResetZoomClicked;
        //
        // _openInBrowserButton
        //
        _openInBrowserButton.AutoToolTip = false;
        _openInBrowserButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _openInBrowserButton.Name = "_openInBrowserButton";
        _openInBrowserButton.Size = new Size(28, 25);
        _openInBrowserButton.Text = "🌐";
        _openInBrowserButton.ToolTipText = "Открыть текущую страницу в системном браузере";
        _openInBrowserButton.Click += OnOpenInBrowserClicked;
        //
        // _resetSessionButton
        //
        _resetSessionButton.Alignment = ToolStripItemAlignment.Right;
        _resetSessionButton.AutoToolTip = false;
        _resetSessionButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        _resetSessionButton.Name = "_resetSessionButton";
        _resetSessionButton.Size = new Size(28, 25);
        _resetSessionButton.Text = "🗑";
        _resetSessionButton.ToolTipText = "Очистить куки и кэш (разлогинит бота)";
        _resetSessionButton.Click += OnResetSessionClicked;
        //
        // ChatDisplay
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_webView);
        Controls.Add(_fallbackPanel);
        Controls.Add(_toolStrip);
        Name = "ChatDisplay";
        Size = new Size(375, 294);
        ((System.ComponentModel.ISupportInitialize)_webView).EndInit();
        _fallbackPanel.ResumeLayout(false);
        _toolStrip.ResumeLayout(false);
        _toolStrip.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    private Microsoft.Web.WebView2.WinForms.WebView2 _webView;
    private Panel _fallbackPanel;
    private Label _fallbackLabel;
    private LinkLabel _fallbackLink;
    private ToolStrip _toolStrip;
    private ToolStripButton _reloadButton;
    private ToolStripButton _resetZoomButton;
    private ToolStripButton _openInBrowserButton;
    private ToolStripButton _resetSessionButton;

    #endregion
}
