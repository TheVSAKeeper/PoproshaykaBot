namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

partial class AuthorizationPage
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
        _layout = new TableLayoutPanel();
        _intro = new Label();
        _buttonsLayout = new TableLayoutPanel();
        _copyLinkButton = new Button();
        _openBrowserButton = new Button();
        _cancelButton = new Button();
        _statusLabel = new Label();
        _resultLabel = new Label();
        _layout.SuspendLayout();
        _buttonsLayout.SuspendLayout();
        SuspendLayout();
        //
        // _layout
        //
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_intro, 0, 0);
        _layout.Controls.Add(_buttonsLayout, 0, 1);
        _layout.Controls.Add(_cancelButton, 0, 2);
        _layout.Controls.Add(_statusLabel, 0, 3);
        _layout.Controls.Add(_resultLabel, 0, 4);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(20, 18, 20, 18);
        _layout.RowCount = 6;
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _intro
        //
        _intro.AutoSize = true;
        _intro.Dock = DockStyle.Top;
        _intro.Margin = new Padding(0, 0, 0, 16);
        _intro.Name = "_intro";
        _intro.Text = "Откройте браузер и подтвердите доступ для бота.";
        _intro.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _buttonsLayout
        //
        _buttonsLayout.AutoSize = true;
        _buttonsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _buttonsLayout.ColumnCount = 2;
        _buttonsLayout.ColumnStyles.Add(new ColumnStyle());
        _buttonsLayout.ColumnStyles.Add(new ColumnStyle());
        _buttonsLayout.Controls.Add(_openBrowserButton, 0, 0);
        _buttonsLayout.Controls.Add(_copyLinkButton, 1, 0);
        _buttonsLayout.Dock = DockStyle.Top;
        _buttonsLayout.Margin = new Padding(0, 0, 0, 8);
        _buttonsLayout.Name = "_buttonsLayout";
        _buttonsLayout.RowCount = 1;
        _buttonsLayout.RowStyles.Add(new RowStyle());
        //
        // _openBrowserButton
        //
        _openBrowserButton.AutoSize = true;
        _openBrowserButton.Margin = new Padding(0, 0, 8, 0);
        _openBrowserButton.MinimumSize = new Size(220, 30);
        _openBrowserButton.Name = "_openBrowserButton";
        _openBrowserButton.Text = "🌐 Открыть в браузере";
        _openBrowserButton.UseVisualStyleBackColor = true;
        _openBrowserButton.Click += OnOpenBrowserButtonClicked;
        //
        // _copyLinkButton
        //
        _copyLinkButton.AutoSize = true;
        _copyLinkButton.Margin = new Padding(0);
        _copyLinkButton.MinimumSize = new Size(180, 30);
        _copyLinkButton.Name = "_copyLinkButton";
        _copyLinkButton.Text = "⎘ Скопировать ссылку";
        _copyLinkButton.UseVisualStyleBackColor = true;
        _copyLinkButton.Click += OnCopyLinkButtonClicked;
        //
        // _cancelButton
        //
        _cancelButton.AutoSize = true;
        _cancelButton.Margin = new Padding(0, 0, 0, 12);
        _cancelButton.MinimumSize = new Size(220, 30);
        _cancelButton.Name = "_cancelButton";
        _cancelButton.Text = "⏹️ Отменить авторизацию";
        _cancelButton.UseVisualStyleBackColor = true;
        _cancelButton.Visible = false;
        _cancelButton.Click += OnCancelButtonClicked;
        //
        // _statusLabel
        //
        _statusLabel.AutoSize = true;
        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.ForeColor = Color.Gray;
        _statusLabel.Margin = new Padding(0, 0, 0, 6);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Text = "";
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _resultLabel
        //
        _resultLabel.AutoSize = true;
        _resultLabel.Dock = DockStyle.Top;
        _resultLabel.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
        _resultLabel.Margin = new Padding(0);
        _resultLabel.Name = "_resultLabel";
        _resultLabel.Text = "";
        _resultLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // AuthorizationPage
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_layout);
        Name = "AuthorizationPage";
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _buttonsLayout.ResumeLayout(false);
        _buttonsLayout.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private TableLayoutPanel _layout;
    private Label _intro;
    private TableLayoutPanel _buttonsLayout;
    private Button _openBrowserButton;
    private Button _copyLinkButton;
    private Button _cancelButton;
    private Label _statusLabel;
    private Label _resultLabel;
}
