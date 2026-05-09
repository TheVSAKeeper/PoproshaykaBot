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
        _autoConnectCheckBox = new CheckBox();
        _hintLabel = new Label();
        _layout.SuspendLayout();
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
        _layout.Controls.Add(_autoConnectCheckBox, 0, 4);
        _layout.Controls.Add(_hintLabel, 0, 5);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(20, 18, 20, 18);
        _layout.RowCount = 7;
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
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion

    private TableLayoutPanel _layout;
    private Label _heading;
    private Label _summaryLabel;
    private Label _validationsHeaderLabel;
    private Label _validationsListLabel;
    private CheckBox _autoConnectCheckBox;
    private Label _hintLabel;
}
