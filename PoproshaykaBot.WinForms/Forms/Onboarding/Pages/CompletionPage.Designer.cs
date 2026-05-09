namespace PoproshaykaBot.WinForms.Forms.Onboarding.Pages;

partial class CompletionPage
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
        _heading = new Label();
        _summaryLabel = new Label();
        _validationsHeaderLabel = new Label();
        _validationsListLabel = new Label();
        _chatAccountGroupBox = new GroupBox();
        _chatAccountLayout = new TableLayoutPanel();
        _chatAccountBotRadio = new RadioButton();
        _chatAccountBroadcasterRadio = new RadioButton();
        _autoConnectCheckBox = new CheckBox();
        _hintLabel = new Label();
        _layout.SuspendLayout();
        _chatAccountGroupBox.SuspendLayout();
        _chatAccountLayout.SuspendLayout();
        SuspendLayout();
        //
        // _layout
        //
        _layout.ColumnCount = 1;
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _layout.Controls.Add(_heading, 0, 0);
        _layout.Controls.Add(_summaryLabel, 0, 1);
        _layout.Controls.Add(_validationsHeaderLabel, 0, 2);
        _layout.Controls.Add(_validationsListLabel, 0, 3);
        _layout.Controls.Add(_chatAccountGroupBox, 0, 4);
        _layout.Controls.Add(_autoConnectCheckBox, 0, 5);
        _layout.Controls.Add(_hintLabel, 0, 6);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(20, 18, 20, 18);
        _layout.RowCount = 8;
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle());
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        //
        // _heading
        //
        _heading.AutoSize = true;
        _heading.Dock = DockStyle.Top;
        _heading.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _heading.ForeColor = Color.Green;
        _heading.Margin = new Padding(0, 0, 0, 12);
        _heading.Name = "_heading";
        _heading.Text = "✓ Всё готово к сохранению";
        _heading.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _summaryLabel
        //
        _summaryLabel.AutoSize = true;
        _summaryLabel.Dock = DockStyle.Top;
        _summaryLabel.Margin = new Padding(0, 0, 0, 12);
        _summaryLabel.Name = "_summaryLabel";
        _summaryLabel.Text = "";
        _summaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _validationsHeaderLabel
        //
        _validationsHeaderLabel.AutoSize = true;
        _validationsHeaderLabel.Dock = DockStyle.Top;
        _validationsHeaderLabel.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        _validationsHeaderLabel.Margin = new Padding(0, 0, 0, 4);
        _validationsHeaderLabel.Name = "_validationsHeaderLabel";
        _validationsHeaderLabel.Text = "Проверка готовности";
        _validationsHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _validationsListLabel
        //
        _validationsListLabel.AutoSize = true;
        _validationsListLabel.Dock = DockStyle.Top;
        _validationsListLabel.Margin = new Padding(0, 0, 0, 12);
        _validationsListLabel.Name = "_validationsListLabel";
        _validationsListLabel.Text = "";
        _validationsListLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _chatAccountGroupBox
        //
        _chatAccountGroupBox.AutoSize = true;
        _chatAccountGroupBox.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _chatAccountGroupBox.Controls.Add(_chatAccountLayout);
        _chatAccountGroupBox.Dock = DockStyle.Top;
        _chatAccountGroupBox.Margin = new Padding(0, 0, 0, 12);
        _chatAccountGroupBox.Name = "_chatAccountGroupBox";
        _chatAccountGroupBox.Padding = new Padding(8, 4, 8, 4);
        _chatAccountGroupBox.TabStop = false;
        _chatAccountGroupBox.Text = "Twitch-чат на дашборде ведётся от имени:";
        //
        // _chatAccountLayout
        //
        _chatAccountLayout.AutoSize = true;
        _chatAccountLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _chatAccountLayout.ColumnCount = 2;
        _chatAccountLayout.ColumnStyles.Add(new ColumnStyle());
        _chatAccountLayout.ColumnStyles.Add(new ColumnStyle());
        _chatAccountLayout.Controls.Add(_chatAccountBotRadio, 0, 0);
        _chatAccountLayout.Controls.Add(_chatAccountBroadcasterRadio, 1, 0);
        _chatAccountLayout.Dock = DockStyle.Top;
        _chatAccountLayout.Margin = new Padding(0);
        _chatAccountLayout.Name = "_chatAccountLayout";
        _chatAccountLayout.RowCount = 1;
        _chatAccountLayout.RowStyles.Add(new RowStyle());
        //
        // _chatAccountBotRadio
        //
        _chatAccountBotRadio.AutoSize = true;
        _chatAccountBotRadio.Checked = true;
        _chatAccountBotRadio.Margin = new Padding(0, 0, 16, 0);
        _chatAccountBotRadio.Name = "_chatAccountBotRadio";
        _chatAccountBotRadio.TabStop = true;
        _chatAccountBotRadio.Text = "бота";
        _chatAccountBotRadio.UseVisualStyleBackColor = true;
        _chatAccountBotRadio.CheckedChanged += OnChatAccountRadioChanged;
        //
        // _chatAccountBroadcasterRadio
        //
        _chatAccountBroadcasterRadio.AutoSize = true;
        _chatAccountBroadcasterRadio.Margin = new Padding(0);
        _chatAccountBroadcasterRadio.Name = "_chatAccountBroadcasterRadio";
        _chatAccountBroadcasterRadio.Text = "стримера";
        _chatAccountBroadcasterRadio.UseVisualStyleBackColor = true;
        _chatAccountBroadcasterRadio.CheckedChanged += OnChatAccountRadioChanged;
        //
        // _autoConnectCheckBox
        //
        _autoConnectCheckBox.AutoSize = true;
        _autoConnectCheckBox.Checked = true;
        _autoConnectCheckBox.CheckState = CheckState.Checked;
        _autoConnectCheckBox.Dock = DockStyle.Top;
        _autoConnectCheckBox.Margin = new Padding(0, 0, 0, 12);
        _autoConnectCheckBox.Name = "_autoConnectCheckBox";
        _autoConnectCheckBox.Text = "Подключить бота сразу после сохранения";
        _autoConnectCheckBox.UseVisualStyleBackColor = true;
        //
        // _hintLabel
        //
        _hintLabel.AutoSize = true;
        _hintLabel.Dock = DockStyle.Top;
        _hintLabel.ForeColor = Color.Gray;
        _hintLabel.Margin = new Padding(0);
        _hintLabel.Name = "_hintLabel";
        _hintLabel.Text = "Нажмите «Готово», чтобы сохранить настройки и закрыть мастер.";
        _hintLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // CompletionPage
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_layout);
        Name = "CompletionPage";
        _layout.ResumeLayout(false);
        _layout.PerformLayout();
        _chatAccountGroupBox.ResumeLayout(false);
        _chatAccountGroupBox.PerformLayout();
        _chatAccountLayout.ResumeLayout(false);
        _chatAccountLayout.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private TableLayoutPanel _layout;
    private Label _heading;
    private Label _summaryLabel;
    private Label _validationsHeaderLabel;
    private Label _validationsListLabel;
    private GroupBox _chatAccountGroupBox;
    private TableLayoutPanel _chatAccountLayout;
    private RadioButton _chatAccountBotRadio;
    private RadioButton _chatAccountBroadcasterRadio;
    private CheckBox _autoConnectCheckBox;
    private Label _hintLabel;
}
