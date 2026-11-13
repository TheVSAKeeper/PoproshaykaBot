namespace PoproshaykaBot.WinForms.Forms.Onboarding;

partial class EmbeddedTwitchAuthDialog
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

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        _rootLayout = new TableLayoutPanel();
        _statusLabel = new Label();
        _contentPanel = new Panel();
        _webView = new Microsoft.Web.WebView2.WinForms.WebView2();
        _fallbackPanel = new Panel();
        _fallbackLabel = new Label();
        _fallbackLink = new LinkLabel();
        _footerLayout = new TableLayoutPanel();
        _spacerLabel = new Label();
        _cancelButton = new Button();
        _rootLayout.SuspendLayout();
        _contentPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_webView).BeginInit();
        _fallbackPanel.SuspendLayout();
        _footerLayout.SuspendLayout();
        SuspendLayout();
        //
        // _rootLayout
        //
        _rootLayout.ColumnCount = 1;
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _rootLayout.Controls.Add(_statusLabel, 0, 0);
        _rootLayout.Controls.Add(_contentPanel, 0, 1);
        _rootLayout.Controls.Add(_footerLayout, 0, 2);
        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.Location = new Point(0, 0);
        _rootLayout.Name = "_rootLayout";
        _rootLayout.Padding = new Padding(12, 10, 12, 10);
        _rootLayout.RowCount = 3;
        _rootLayout.RowStyles.Add(new RowStyle());
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _rootLayout.RowStyles.Add(new RowStyle());
        _rootLayout.Size = new Size(900, 700);
        _rootLayout.TabIndex = 0;
        //
        // _statusLabel
        //
        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Margin = new Padding(3, 3, 3, 8);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Size = new Size(870, 20);
        _statusLabel.TabIndex = 0;
        _statusLabel.Text = "Подготовка...";
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _contentPanel
        //
        _contentPanel.Controls.Add(_fallbackPanel);
        _contentPanel.Controls.Add(_webView);
        _contentPanel.Dock = DockStyle.Fill;
        _contentPanel.Margin = new Padding(0);
        _contentPanel.Name = "_contentPanel";
        _contentPanel.TabIndex = 1;
        //
        // _webView
        //
        _webView.AllowExternalDrop = false;
        _webView.CreationProperties = null;
        _webView.DefaultBackgroundColor = Color.White;
        _webView.Dock = DockStyle.Fill;
        _webView.Name = "_webView";
        _webView.TabIndex = 0;
        _webView.ZoomFactor = 1D;
        //
        // _fallbackPanel
        //
        _fallbackPanel.Controls.Add(_fallbackLabel);
        _fallbackPanel.Controls.Add(_fallbackLink);
        _fallbackPanel.Dock = DockStyle.Fill;
        _fallbackPanel.Name = "_fallbackPanel";
        _fallbackPanel.Padding = new Padding(16);
        _fallbackPanel.TabIndex = 1;
        _fallbackPanel.Visible = false;
        //
        // _fallbackLabel
        //
        _fallbackLabel.Dock = DockStyle.Fill;
        _fallbackLabel.Name = "_fallbackLabel";
        _fallbackLabel.TabIndex = 0;
        _fallbackLabel.Text = "Не удалось открыть встроенный браузер.";
        _fallbackLabel.TextAlign = ContentAlignment.MiddleCenter;
        //
        // _fallbackLink
        //
        _fallbackLink.Dock = DockStyle.Bottom;
        _fallbackLink.Name = "_fallbackLink";
        _fallbackLink.Size = new Size(0, 24);
        _fallbackLink.TabIndex = 1;
        _fallbackLink.TabStop = true;
        _fallbackLink.Text = "Скачать WebView2 Runtime";
        _fallbackLink.TextAlign = ContentAlignment.MiddleCenter;
        _fallbackLink.Visible = false;
        _fallbackLink.LinkClicked += OnFallbackLinkClicked;
        //
        // _footerLayout
        //
        _footerLayout.AutoSize = true;
        _footerLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _footerLayout.ColumnCount = 2;
        _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _footerLayout.ColumnStyles.Add(new ColumnStyle());
        _footerLayout.Controls.Add(_spacerLabel, 0, 0);
        _footerLayout.Controls.Add(_cancelButton, 1, 0);
        _footerLayout.Dock = DockStyle.Fill;
        _footerLayout.Margin = new Padding(0, 8, 0, 0);
        _footerLayout.Name = "_footerLayout";
        _footerLayout.RowCount = 1;
        _footerLayout.RowStyles.Add(new RowStyle());
        _footerLayout.Size = new Size(870, 32);
        _footerLayout.TabIndex = 2;
        //
        // _spacerLabel
        //
        _spacerLabel.AutoSize = false;
        _spacerLabel.Dock = DockStyle.Fill;
        _spacerLabel.Name = "_spacerLabel";
        _spacerLabel.TabIndex = 0;
        _spacerLabel.Text = "";
        //
        // _cancelButton
        //
        _cancelButton.AutoSize = true;
        _cancelButton.Margin = new Padding(0);
        _cancelButton.MinimumSize = new Size(110, 28);
        _cancelButton.Name = "_cancelButton";
        _cancelButton.TabIndex = 1;
        _cancelButton.Text = "Отмена";
        _cancelButton.UseVisualStyleBackColor = true;
        _cancelButton.Click += OnCancelButtonClicked;
        //
        // EmbeddedTwitchAuthDialog
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = _cancelButton;
        ClientSize = new Size(900, 700);
        Controls.Add(_rootLayout);
        MinimumSize = new Size(640, 520);
        Name = "EmbeddedTwitchAuthDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Авторизация в Twitch";
        _rootLayout.ResumeLayout(false);
        _rootLayout.PerformLayout();
        _contentPanel.ResumeLayout(false);
        ((System.ComponentModel.ISupportInitialize)_webView).EndInit();
        _fallbackPanel.ResumeLayout(false);
        _footerLayout.ResumeLayout(false);
        _footerLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel _rootLayout;
    private Label _statusLabel;
    private Panel _contentPanel;
    private Microsoft.Web.WebView2.WinForms.WebView2 _webView;
    private Panel _fallbackPanel;
    private Label _fallbackLabel;
    private LinkLabel _fallbackLink;
    private TableLayoutPanel _footerLayout;
    private Label _spacerLabel;
    private Button _cancelButton;
}
