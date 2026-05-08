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
        _authButton = new Button();
        _statusLabel = new Label();
        _resultLabel = new Label();
        _layout.SuspendLayout();
        SuspendLayout();
        //
        // _layout
        //
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_intro, 0, 0);
        _layout.Controls.Add(_authButton, 0, 1);
        _layout.Controls.Add(_statusLabel, 0, 2);
        _layout.Controls.Add(_resultLabel, 0, 3);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(20, 18, 20, 18);
        _layout.RowCount = 5;
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
        // _authButton
        //
        _authButton.AutoSize = true;
        _authButton.Margin = new Padding(0, 0, 0, 12);
        _authButton.MinimumSize = new Size(220, 30);
        _authButton.Name = "_authButton";
        _authButton.Text = "Авторизовать";
        _authButton.UseVisualStyleBackColor = true;
        _authButton.Click += OnAuthButtonClicked;
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
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel _layout;
    private Label _intro;
    private Button _authButton;
    private Label _statusLabel;
    private Label _resultLabel;
}
