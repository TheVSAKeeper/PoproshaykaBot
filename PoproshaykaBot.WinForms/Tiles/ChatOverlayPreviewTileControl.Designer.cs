namespace PoproshaykaBot.WinForms.Tiles;

sealed partial class ChatOverlayPreviewTileControl
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _webView = new Microsoft.Web.WebView2.WinForms.WebView2();
        _fallbackLabel = new Label();
        ((System.ComponentModel.ISupportInitialize)_webView).BeginInit();
        SuspendLayout();
        // 
        // _webView
        // 
        _webView.AllowExternalDrop = true;
        _webView.CreationProperties = null;
        _webView.DefaultBackgroundColor = Color.White;
        _webView.Dock = DockStyle.Fill;
        _webView.Location = new Point(0, 0);
        _webView.Name = "_webView";
        _webView.Size = new Size(386, 302);
        _webView.TabIndex = 0;
        _webView.ZoomFactor = 1D;
        // 
        // _fallbackLabel
        // 
        _fallbackLabel.Dock = DockStyle.Fill;
        _fallbackLabel.ForeColor = SystemColors.GrayText;
        _fallbackLabel.Location = new Point(0, 0);
        _fallbackLabel.Name = "_fallbackLabel";
        _fallbackLabel.Padding = new Padding(12);
        _fallbackLabel.Size = new Size(386, 302);
        _fallbackLabel.TabIndex = 1;
        _fallbackLabel.TextAlign = ContentAlignment.MiddleCenter;
        _fallbackLabel.Visible = false;
        // 
        // ChatOverlayPreviewTileControl
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_fallbackLabel);
        Controls.Add(_webView);
        Name = "ChatOverlayPreviewTileControl";
        Size = new Size(386, 302);
        ((System.ComponentModel.ISupportInitialize)_webView).EndInit();
        ResumeLayout(false);
    }

    private Microsoft.Web.WebView2.WinForms.WebView2 _webView;
    private Label _fallbackLabel;
}
