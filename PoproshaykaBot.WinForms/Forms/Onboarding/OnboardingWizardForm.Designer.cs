namespace PoproshaykaBot.WinForms.Forms.Onboarding;

partial class OnboardingWizardForm
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
        _headerPanel = new Panel();
        _headerLabel = new Label();
        _stepLabel = new Label();
        _pageContainer = new Panel();
        _footerLayout = new TableLayoutPanel();
        _backButton = new Button();
        _nextButton = new Button();
        _cancelButton = new Button();
        _spacerLabel = new Label();
        _rootLayout.SuspendLayout();
        _headerPanel.SuspendLayout();
        _footerLayout.SuspendLayout();
        SuspendLayout();
        //
        // _rootLayout
        //
        _rootLayout.ColumnCount = 1;
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _rootLayout.Controls.Add(_headerPanel, 0, 0);
        _rootLayout.Controls.Add(_pageContainer, 0, 1);
        _rootLayout.Controls.Add(_footerLayout, 0, 2);
        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.Location = new Point(0, 0);
        _rootLayout.Name = "_rootLayout";
        _rootLayout.Padding = new Padding(16, 14, 16, 14);
        _rootLayout.RowCount = 3;
        _rootLayout.RowStyles.Add(new RowStyle());
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _rootLayout.RowStyles.Add(new RowStyle());
        _rootLayout.Size = new Size(720, 520);
        _rootLayout.TabIndex = 0;
        //
        // _headerPanel
        //
        _headerPanel.AutoSize = true;
        _headerPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _headerPanel.Controls.Add(_stepLabel);
        _headerPanel.Controls.Add(_headerLabel);
        _headerPanel.Dock = DockStyle.Fill;
        _headerPanel.Margin = new Padding(0, 0, 0, 10);
        _headerPanel.Name = "_headerPanel";
        _headerPanel.Padding = new Padding(0, 0, 0, 8);
        _headerPanel.Size = new Size(688, 50);
        _headerPanel.TabIndex = 0;
        //
        // _headerLabel
        //
        _headerLabel.AutoSize = false;
        _headerLabel.Dock = DockStyle.Top;
        _headerLabel.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
        _headerLabel.Location = new Point(0, 0);
        _headerLabel.Name = "_headerLabel";
        _headerLabel.Size = new Size(688, 28);
        _headerLabel.TabIndex = 0;
        _headerLabel.Text = "Первичная настройка";
        _headerLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _stepLabel
        //
        _stepLabel.AutoSize = false;
        _stepLabel.Dock = DockStyle.Bottom;
        _stepLabel.ForeColor = Color.Gray;
        _stepLabel.Location = new Point(0, 28);
        _stepLabel.Name = "_stepLabel";
        _stepLabel.Size = new Size(688, 14);
        _stepLabel.TabIndex = 1;
        _stepLabel.Text = "Шаг 1 из N";
        _stepLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _pageContainer
        //
        _pageContainer.BackColor = SystemColors.Window;
        _pageContainer.BorderStyle = BorderStyle.FixedSingle;
        _pageContainer.Dock = DockStyle.Fill;
        _pageContainer.Margin = new Padding(0, 0, 0, 12);
        _pageContainer.Name = "_pageContainer";
        _pageContainer.Padding = new Padding(0);
        _pageContainer.Size = new Size(688, 376);
        _pageContainer.TabIndex = 1;
        //
        // _footerLayout
        //
        _footerLayout.AutoSize = true;
        _footerLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _footerLayout.ColumnCount = 4;
        _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _footerLayout.ColumnStyles.Add(new ColumnStyle());
        _footerLayout.ColumnStyles.Add(new ColumnStyle());
        _footerLayout.ColumnStyles.Add(new ColumnStyle());
        _footerLayout.Controls.Add(_spacerLabel, 0, 0);
        _footerLayout.Controls.Add(_backButton, 1, 0);
        _footerLayout.Controls.Add(_nextButton, 2, 0);
        _footerLayout.Controls.Add(_cancelButton, 3, 0);
        _footerLayout.Dock = DockStyle.Fill;
        _footerLayout.Margin = new Padding(0);
        _footerLayout.Name = "_footerLayout";
        _footerLayout.RowCount = 1;
        _footerLayout.RowStyles.Add(new RowStyle());
        _footerLayout.Size = new Size(688, 32);
        _footerLayout.TabIndex = 2;
        //
        // _spacerLabel
        //
        _spacerLabel.AutoSize = false;
        _spacerLabel.Dock = DockStyle.Fill;
        _spacerLabel.Location = new Point(0, 0);
        _spacerLabel.Name = "_spacerLabel";
        _spacerLabel.Size = new Size(400, 32);
        _spacerLabel.TabIndex = 0;
        _spacerLabel.Text = "";
        //
        // _backButton
        //
        _backButton.AutoSize = true;
        _backButton.Margin = new Padding(0, 0, 8, 0);
        _backButton.MinimumSize = new Size(96, 28);
        _backButton.Name = "_backButton";
        _backButton.TabIndex = 1;
        _backButton.Text = "← Назад";
        _backButton.UseVisualStyleBackColor = true;
        _backButton.Click += OnBackButtonClicked;
        //
        // _nextButton
        //
        _nextButton.AutoSize = true;
        _nextButton.Margin = new Padding(0, 0, 8, 0);
        _nextButton.MinimumSize = new Size(96, 28);
        _nextButton.Name = "_nextButton";
        _nextButton.TabIndex = 2;
        _nextButton.Text = "Далее →";
        _nextButton.UseVisualStyleBackColor = true;
        _nextButton.Click += OnNextButtonClicked;
        //
        // _cancelButton
        //
        _cancelButton.AutoSize = true;
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Margin = new Padding(0);
        _cancelButton.MinimumSize = new Size(96, 28);
        _cancelButton.Name = "_cancelButton";
        _cancelButton.TabIndex = 3;
        _cancelButton.Text = "Отмена";
        _cancelButton.UseVisualStyleBackColor = true;
        _cancelButton.Click += OnCancelButtonClicked;
        //
        // OnboardingWizardForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = _cancelButton;
        ClientSize = new Size(720, 520);
        Controls.Add(_rootLayout);
        MaximizeBox = false;
        MinimizeBox = false;
        MinimumSize = new Size(640, 480);
        Name = "OnboardingWizardForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Первичная настройка";
        _rootLayout.ResumeLayout(false);
        _rootLayout.PerformLayout();
        _headerPanel.ResumeLayout(false);
        _footerLayout.ResumeLayout(false);
        _footerLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel _rootLayout;
    private Panel _headerPanel;
    private Label _headerLabel;
    private Label _stepLabel;
    private Panel _pageContainer;
    private TableLayoutPanel _footerLayout;
    private Label _spacerLabel;
    private Button _backButton;
    private Button _nextButton;
    private Button _cancelButton;
}
