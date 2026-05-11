namespace PoproshaykaBot.WinForms.Forms.Users
{
    partial class PointTermDialog
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.TableLayoutPanel _mainLayout;
        private System.Windows.Forms.Label _singularLbl;
        private System.Windows.Forms.TextBox _singularTextBox;
        private System.Windows.Forms.Label _fewLbl;
        private System.Windows.Forms.TextBox _fewTextBox;
        private System.Windows.Forms.Label _manyLbl;
        private System.Windows.Forms.TextBox _manyTextBox;
        private System.Windows.Forms.Label _previewCaptionLbl;
        private System.Windows.Forms.Label _previewValueLbl;
        private System.Windows.Forms.Button _resetDefaultsButton;
        private System.Windows.Forms.FlowLayoutPanel _buttonsFlow;
        private System.Windows.Forms.Button _okButton;
        private System.Windows.Forms.Button _cancelButton;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _mainLayout = new TableLayoutPanel();
            _singularLbl = new Label();
            _singularTextBox = new TextBox();
            _fewLbl = new Label();
            _fewTextBox = new TextBox();
            _manyLbl = new Label();
            _manyTextBox = new TextBox();
            _previewCaptionLbl = new Label();
            _previewValueLbl = new Label();
            _resetDefaultsButton = new Button();
            _buttonsFlow = new FlowLayoutPanel();
            _okButton = new Button();
            _cancelButton = new Button();
            _mainLayout.SuspendLayout();
            _buttonsFlow.SuspendLayout();
            SuspendLayout();
            //
            // _mainLayout
            //
            _mainLayout.ColumnCount = 2;
            _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _mainLayout.Controls.Add(_singularLbl, 0, 0);
            _mainLayout.Controls.Add(_singularTextBox, 1, 0);
            _mainLayout.Controls.Add(_fewLbl, 0, 1);
            _mainLayout.Controls.Add(_fewTextBox, 1, 1);
            _mainLayout.Controls.Add(_manyLbl, 0, 2);
            _mainLayout.Controls.Add(_manyTextBox, 1, 2);
            _mainLayout.Controls.Add(_previewCaptionLbl, 0, 3);
            _mainLayout.Controls.Add(_previewValueLbl, 1, 3);
            _mainLayout.Controls.Add(_resetDefaultsButton, 0, 4);
            _mainLayout.Controls.Add(_buttonsFlow, 0, 5);
            _mainLayout.Dock = DockStyle.Fill;
            _mainLayout.Location = new Point(0, 0);
            _mainLayout.Name = "_mainLayout";
            _mainLayout.Padding = new Padding(8);
            _mainLayout.RowCount = 6;
            _mainLayout.RowStyles.Add(new RowStyle());
            _mainLayout.RowStyles.Add(new RowStyle());
            _mainLayout.RowStyles.Add(new RowStyle());
            _mainLayout.RowStyles.Add(new RowStyle());
            _mainLayout.RowStyles.Add(new RowStyle());
            _mainLayout.RowStyles.Add(new RowStyle());
            _mainLayout.Size = new Size(440, 240);
            _mainLayout.TabIndex = 0;
            //
            // _singularLbl
            //
            _singularLbl.Anchor = AnchorStyles.Left;
            _singularLbl.AutoSize = true;
            _singularLbl.Margin = new Padding(0, 6, 6, 4);
            _singularLbl.Name = "_singularLbl";
            _singularLbl.TabIndex = 0;
            _singularLbl.Text = "Один (1 ...):";
            //
            // _singularTextBox
            //
            _singularTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _singularTextBox.Margin = new Padding(0, 3, 0, 4);
            _singularTextBox.Name = "_singularTextBox";
            _singularTextBox.TabIndex = 1;
            _singularTextBox.TextChanged += OnInputChanged;
            //
            // _fewLbl
            //
            _fewLbl.Anchor = AnchorStyles.Left;
            _fewLbl.AutoSize = true;
            _fewLbl.Margin = new Padding(0, 6, 6, 4);
            _fewLbl.Name = "_fewLbl";
            _fewLbl.TabIndex = 2;
            _fewLbl.Text = "Несколько (2–4):";
            //
            // _fewTextBox
            //
            _fewTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _fewTextBox.Margin = new Padding(0, 3, 0, 4);
            _fewTextBox.Name = "_fewTextBox";
            _fewTextBox.TabIndex = 3;
            _fewTextBox.TextChanged += OnInputChanged;
            //
            // _manyLbl
            //
            _manyLbl.Anchor = AnchorStyles.Left;
            _manyLbl.AutoSize = true;
            _manyLbl.Margin = new Padding(0, 6, 6, 4);
            _manyLbl.Name = "_manyLbl";
            _manyLbl.TabIndex = 4;
            _manyLbl.Text = "Много (5+):";
            //
            // _manyTextBox
            //
            _manyTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _manyTextBox.Margin = new Padding(0, 3, 0, 4);
            _manyTextBox.Name = "_manyTextBox";
            _manyTextBox.TabIndex = 5;
            _manyTextBox.TextChanged += OnInputChanged;
            //
            // _previewCaptionLbl
            //
            _previewCaptionLbl.Anchor = AnchorStyles.Left;
            _previewCaptionLbl.AutoSize = true;
            _previewCaptionLbl.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _previewCaptionLbl.Margin = new Padding(0, 8, 6, 4);
            _previewCaptionLbl.Name = "_previewCaptionLbl";
            _previewCaptionLbl.TabIndex = 6;
            _previewCaptionLbl.Text = "Предпросмотр:";
            //
            // _previewValueLbl
            //
            _previewValueLbl.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _previewValueLbl.AutoEllipsis = true;
            _previewValueLbl.ForeColor = Color.Gray;
            _previewValueLbl.Margin = new Padding(0, 8, 0, 4);
            _previewValueLbl.MinimumSize = new Size(0, 18);
            _previewValueLbl.Name = "_previewValueLbl";
            _previewValueLbl.TabIndex = 7;
            _previewValueLbl.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _resetDefaultsButton
            //
            _resetDefaultsButton.Anchor = AnchorStyles.Left;
            _resetDefaultsButton.AutoSize = true;
            _mainLayout.SetColumnSpan(_resetDefaultsButton, 2);
            _resetDefaultsButton.Margin = new Padding(0, 12, 0, 0);
            _resetDefaultsButton.Name = "_resetDefaultsButton";
            _resetDefaultsButton.TabIndex = 8;
            _resetDefaultsButton.Text = "По умолчанию";
            _resetDefaultsButton.UseVisualStyleBackColor = true;
            _resetDefaultsButton.Click += OnResetDefaultsClicked;
            //
            // _buttonsFlow
            //
            _buttonsFlow.Anchor = AnchorStyles.Right;
            _buttonsFlow.AutoSize = true;
            _mainLayout.SetColumnSpan(_buttonsFlow, 2);
            _buttonsFlow.Controls.Add(_okButton);
            _buttonsFlow.Controls.Add(_cancelButton);
            _buttonsFlow.Margin = new Padding(0, 8, 0, 0);
            _buttonsFlow.Name = "_buttonsFlow";
            _buttonsFlow.TabIndex = 9;
            //
            // _okButton
            //
            _okButton.AutoSize = true;
            _okButton.DialogResult = DialogResult.OK;
            _okButton.Margin = new Padding(0, 0, 6, 0);
            _okButton.MinimumSize = new Size(80, 25);
            _okButton.Name = "_okButton";
            _okButton.TabIndex = 0;
            _okButton.Text = "Сохранить";
            _okButton.UseVisualStyleBackColor = true;
            //
            // _cancelButton
            //
            _cancelButton.AutoSize = true;
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Margin = new Padding(0);
            _cancelButton.MinimumSize = new Size(80, 25);
            _cancelButton.Name = "_cancelButton";
            _cancelButton.TabIndex = 1;
            _cancelButton.Text = "Отмена";
            _cancelButton.UseVisualStyleBackColor = true;
            //
            // PointTermDialog
            //
            AcceptButton = _okButton;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = _cancelButton;
            ClientSize = new Size(440, 240);
            Controls.Add(_mainLayout);
            MinimizeBox = false;
            MinimumSize = new Size(380, 240);
            Name = "PointTermDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "🏷 Названия баллов";
            _mainLayout.ResumeLayout(false);
            _mainLayout.PerformLayout();
            _buttonsFlow.ResumeLayout(false);
            _buttonsFlow.PerformLayout();
            ResumeLayout(false);
        }
    }
}
