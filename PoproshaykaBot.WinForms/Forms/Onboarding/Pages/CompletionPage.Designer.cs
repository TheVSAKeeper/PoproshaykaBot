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
        _layout.Controls.Add(_hintLabel, 0, 2);
        _layout.Dock = DockStyle.Fill;
        _layout.Name = "_layout";
        _layout.Padding = new Padding(20, 18, 20, 18);
        _layout.RowCount = 4;
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
        _heading.Text = "✓ Настройка завершена";
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
        // _hintLabel
        //
        _hintLabel.AutoSize = true;
        _hintLabel.Dock = DockStyle.Top;
        _hintLabel.ForeColor = Color.Gray;
        _hintLabel.Margin = new Padding(0);
        _hintLabel.Name = "_hintLabel";
        _hintLabel.Text = "Нажмите «Готово», чтобы закрыть мастер. После этого можно будет нажать «Подключить» на главной форме.";
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
    }

    #endregion

    private TableLayoutPanel _layout;
    private Label _heading;
    private Label _summaryLabel;
    private Label _hintLabel;
}
