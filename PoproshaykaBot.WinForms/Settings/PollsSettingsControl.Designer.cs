namespace PoproshaykaBot.WinForms.Settings
{
    partial class PollsSettingsControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TableLayoutPanel _rootLayout;
        private System.Windows.Forms.GroupBox _templatesGroup;
        private System.Windows.Forms.TableLayoutPanel _templatesLayout;
        private System.Windows.Forms.CheckBox _startEnabledCheckBox;
        private System.Windows.Forms.TextBox _startTemplateTextBox;
        private System.Windows.Forms.CheckBox _progressEnabledCheckBox;
        private System.Windows.Forms.TextBox _progressTemplateTextBox;
        private System.Windows.Forms.CheckBox _endEnabledCheckBox;
        private System.Windows.Forms.TextBox _endTemplateTextBox;
        private System.Windows.Forms.CheckBox _terminatedEnabledCheckBox;
        private System.Windows.Forms.TextBox _terminatedTemplateTextBox;
        private System.Windows.Forms.CheckBox _archivedEnabledCheckBox;
        private System.Windows.Forms.TextBox _archivedTemplateTextBox;
        private System.Windows.Forms.FlowLayoutPanel _bottomFlow;
        private System.Windows.Forms.Label _progressIntervalLabel;
        private System.Windows.Forms.NumericUpDown _progressIntervalNumeric;
        private System.Windows.Forms.CheckBox _killSwitchCheckBox;
        private System.Windows.Forms.Label _historyMaxLabel;
        private System.Windows.Forms.NumericUpDown _historyMaxNumeric;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _rootLayout = new TableLayoutPanel();
            _templatesGroup = new GroupBox();
            _templatesLayout = new TableLayoutPanel();
            _startEnabledCheckBox = new CheckBox();
            _startTemplateTextBox = new TextBox();
            _progressEnabledCheckBox = new CheckBox();
            _progressTemplateTextBox = new TextBox();
            _endEnabledCheckBox = new CheckBox();
            _endTemplateTextBox = new TextBox();
            _terminatedEnabledCheckBox = new CheckBox();
            _terminatedTemplateTextBox = new TextBox();
            _archivedEnabledCheckBox = new CheckBox();
            _archivedTemplateTextBox = new TextBox();
            _bottomFlow = new FlowLayoutPanel();
            _progressIntervalLabel = new Label();
            _progressIntervalNumeric = new NumericUpDown();
            _historyMaxLabel = new Label();
            _historyMaxNumeric = new NumericUpDown();
            _killSwitchCheckBox = new CheckBox();
            _rootLayout.SuspendLayout();
            _templatesGroup.SuspendLayout();
            _templatesLayout.SuspendLayout();
            _bottomFlow.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_progressIntervalNumeric).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_historyMaxNumeric).BeginInit();
            SuspendLayout();
            //
            // _rootLayout
            //
            _rootLayout.ColumnCount = 1;
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _rootLayout.Controls.Add(_templatesGroup, 0, 0);
            _rootLayout.Controls.Add(_bottomFlow, 0, 1);
            _rootLayout.Dock = DockStyle.Fill;
            _rootLayout.Location = new Point(0, 0);
            _rootLayout.Name = "_rootLayout";
            _rootLayout.RowCount = 2;
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _rootLayout.RowStyles.Add(new RowStyle());
            _rootLayout.Size = new Size(1100, 711);
            _rootLayout.TabIndex = 0;
            //
            // _templatesGroup
            //
            _templatesGroup.Controls.Add(_templatesLayout);
            _templatesGroup.Dock = DockStyle.Fill;
            _templatesGroup.Location = new Point(3, 3);
            _templatesGroup.Name = "_templatesGroup";
            _templatesGroup.Padding = new Padding(8);
            _templatesGroup.Size = new Size(1094, 575);
            _templatesGroup.TabIndex = 0;
            _templatesGroup.TabStop = false;
            _templatesGroup.Text = "Автопост в чат";
            //
            // _templatesLayout
            //
            _templatesLayout.ColumnCount = 2;
            _templatesLayout.ColumnStyles.Add(new ColumnStyle());
            _templatesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _templatesLayout.Controls.Add(_startEnabledCheckBox, 0, 0);
            _templatesLayout.Controls.Add(_startTemplateTextBox, 1, 0);
            _templatesLayout.Controls.Add(_progressEnabledCheckBox, 0, 1);
            _templatesLayout.Controls.Add(_progressTemplateTextBox, 1, 1);
            _templatesLayout.Controls.Add(_endEnabledCheckBox, 0, 2);
            _templatesLayout.Controls.Add(_endTemplateTextBox, 1, 2);
            _templatesLayout.Controls.Add(_terminatedEnabledCheckBox, 0, 3);
            _templatesLayout.Controls.Add(_terminatedTemplateTextBox, 1, 3);
            _templatesLayout.Controls.Add(_archivedEnabledCheckBox, 0, 4);
            _templatesLayout.Controls.Add(_archivedTemplateTextBox, 1, 4);
            _templatesLayout.Dock = DockStyle.Fill;
            _templatesLayout.Location = new Point(8, 24);
            _templatesLayout.Name = "_templatesLayout";
            _templatesLayout.RowCount = 5;
            _templatesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            _templatesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            _templatesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            _templatesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            _templatesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 20F));
            _templatesLayout.Size = new Size(1078, 543);
            _templatesLayout.TabIndex = 0;
            //
            // _startEnabledCheckBox
            //
            _startEnabledCheckBox.AutoSize = true;
            _startEnabledCheckBox.Margin = new Padding(3, 7, 6, 3);
            _startEnabledCheckBox.Name = "_startEnabledCheckBox";
            _startEnabledCheckBox.Size = new Size(57, 19);
            _startEnabledCheckBox.TabIndex = 0;
            _startEnabledCheckBox.Text = "Старт";
            _startEnabledCheckBox.CheckedChanged += OnEditorChanged;
            //
            // _startTemplateTextBox
            //
            _startTemplateTextBox.Dock = DockStyle.Fill;
            _startTemplateTextBox.Margin = new Padding(0, 3, 0, 3);
            _startTemplateTextBox.Name = "_startTemplateTextBox";
            _startTemplateTextBox.Size = new Size(981, 23);
            _startTemplateTextBox.TabIndex = 1;
            _startTemplateTextBox.TextChanged += OnEditorChanged;
            //
            // _progressEnabledCheckBox
            //
            _progressEnabledCheckBox.AutoSize = true;
            _progressEnabledCheckBox.Margin = new Padding(3, 7, 6, 3);
            _progressEnabledCheckBox.Name = "_progressEnabledCheckBox";
            _progressEnabledCheckBox.Size = new Size(79, 19);
            _progressEnabledCheckBox.TabIndex = 2;
            _progressEnabledCheckBox.Text = "Прогресс";
            _progressEnabledCheckBox.CheckedChanged += OnEditorChanged;
            //
            // _progressTemplateTextBox
            //
            _progressTemplateTextBox.Dock = DockStyle.Fill;
            _progressTemplateTextBox.Margin = new Padding(0, 3, 0, 3);
            _progressTemplateTextBox.Name = "_progressTemplateTextBox";
            _progressTemplateTextBox.Size = new Size(981, 23);
            _progressTemplateTextBox.TabIndex = 3;
            _progressTemplateTextBox.TextChanged += OnEditorChanged;
            //
            // _endEnabledCheckBox
            //
            _endEnabledCheckBox.AutoSize = true;
            _endEnabledCheckBox.Margin = new Padding(3, 7, 6, 3);
            _endEnabledCheckBox.Name = "_endEnabledCheckBox";
            _endEnabledCheckBox.Size = new Size(62, 19);
            _endEnabledCheckBox.TabIndex = 4;
            _endEnabledCheckBox.Text = "Финал";
            _endEnabledCheckBox.CheckedChanged += OnEditorChanged;
            //
            // _endTemplateTextBox
            //
            _endTemplateTextBox.Dock = DockStyle.Fill;
            _endTemplateTextBox.Margin = new Padding(0, 3, 0, 3);
            _endTemplateTextBox.Name = "_endTemplateTextBox";
            _endTemplateTextBox.Size = new Size(981, 23);
            _endTemplateTextBox.TabIndex = 5;
            _endTemplateTextBox.TextChanged += OnEditorChanged;
            //
            // _terminatedEnabledCheckBox
            //
            _terminatedEnabledCheckBox.AutoSize = true;
            _terminatedEnabledCheckBox.Margin = new Padding(3, 7, 6, 3);
            _terminatedEnabledCheckBox.Name = "_terminatedEnabledCheckBox";
            _terminatedEnabledCheckBox.Size = new Size(88, 19);
            _terminatedEnabledCheckBox.TabIndex = 6;
            _terminatedEnabledCheckBox.Text = "Досрочное";
            _terminatedEnabledCheckBox.CheckedChanged += OnEditorChanged;
            //
            // _terminatedTemplateTextBox
            //
            _terminatedTemplateTextBox.Dock = DockStyle.Fill;
            _terminatedTemplateTextBox.Margin = new Padding(0, 3, 0, 3);
            _terminatedTemplateTextBox.Name = "_terminatedTemplateTextBox";
            _terminatedTemplateTextBox.Size = new Size(981, 23);
            _terminatedTemplateTextBox.TabIndex = 7;
            _terminatedTemplateTextBox.TextChanged += OnEditorChanged;
            //
            // _archivedEnabledCheckBox
            //
            _archivedEnabledCheckBox.AutoSize = true;
            _archivedEnabledCheckBox.Margin = new Padding(3, 7, 6, 3);
            _archivedEnabledCheckBox.Name = "_archivedEnabledCheckBox";
            _archivedEnabledCheckBox.Size = new Size(60, 19);
            _archivedEnabledCheckBox.TabIndex = 8;
            _archivedEnabledCheckBox.Text = "Архив";
            _archivedEnabledCheckBox.CheckedChanged += OnEditorChanged;
            //
            // _archivedTemplateTextBox
            //
            _archivedTemplateTextBox.Dock = DockStyle.Fill;
            _archivedTemplateTextBox.Margin = new Padding(0, 3, 0, 3);
            _archivedTemplateTextBox.Name = "_archivedTemplateTextBox";
            _archivedTemplateTextBox.Size = new Size(981, 23);
            _archivedTemplateTextBox.TabIndex = 9;
            _archivedTemplateTextBox.TextChanged += OnEditorChanged;
            //
            // _bottomFlow
            //
            _bottomFlow.AutoSize = true;
            _bottomFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _bottomFlow.Controls.Add(_progressIntervalLabel);
            _bottomFlow.Controls.Add(_progressIntervalNumeric);
            _bottomFlow.Controls.Add(_historyMaxLabel);
            _bottomFlow.Controls.Add(_historyMaxNumeric);
            _bottomFlow.Controls.Add(_killSwitchCheckBox);
            _bottomFlow.Dock = DockStyle.Fill;
            _bottomFlow.Location = new Point(3, 584);
            _bottomFlow.Name = "_bottomFlow";
            _bottomFlow.Size = new Size(1094, 31);
            _bottomFlow.TabIndex = 1;
            _bottomFlow.WrapContents = false;
            //
            // _progressIntervalLabel
            //
            _progressIntervalLabel.AutoSize = true;
            _progressIntervalLabel.Margin = new Padding(3, 8, 3, 0);
            _progressIntervalLabel.Name = "_progressIntervalLabel";
            _progressIntervalLabel.Size = new Size(131, 15);
            _progressIntervalLabel.TabIndex = 0;
            _progressIntervalLabel.Text = "Promo-интервал (сек):";
            //
            // _progressIntervalNumeric
            //
            _progressIntervalNumeric.Margin = new Padding(3, 4, 3, 3);
            _progressIntervalNumeric.Maximum = new decimal(new int[] { 3600, 0, 0, 0 });
            _progressIntervalNumeric.Minimum = new decimal(new int[] { 15, 0, 0, 0 });
            _progressIntervalNumeric.Name = "_progressIntervalNumeric";
            _progressIntervalNumeric.Size = new Size(80, 23);
            _progressIntervalNumeric.TabIndex = 1;
            _progressIntervalNumeric.Value = new decimal(new int[] { 60, 0, 0, 0 });
            _progressIntervalNumeric.ValueChanged += OnEditorChanged;
            //
            // _historyMaxLabel
            //
            _historyMaxLabel.AutoSize = true;
            _historyMaxLabel.Margin = new Padding(20, 8, 3, 0);
            _historyMaxLabel.Name = "_historyMaxLabel";
            _historyMaxLabel.Size = new Size(138, 15);
            _historyMaxLabel.TabIndex = 2;
            _historyMaxLabel.Text = "Макс. записей истории:";
            //
            // _historyMaxNumeric
            //
            _historyMaxNumeric.Margin = new Padding(3, 4, 3, 3);
            _historyMaxNumeric.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
            _historyMaxNumeric.Minimum = new decimal(new int[] { 10, 0, 0, 0 });
            _historyMaxNumeric.Name = "_historyMaxNumeric";
            _historyMaxNumeric.Size = new Size(80, 23);
            _historyMaxNumeric.TabIndex = 3;
            _historyMaxNumeric.Value = new decimal(new int[] { 500, 0, 0, 0 });
            _historyMaxNumeric.ValueChanged += OnEditorChanged;
            //
            // _killSwitchCheckBox
            //
            _killSwitchCheckBox.AutoSize = true;
            _killSwitchCheckBox.Margin = new Padding(20, 8, 3, 3);
            _killSwitchCheckBox.Name = "_killSwitchCheckBox";
            _killSwitchCheckBox.Size = new Size(253, 19);
            _killSwitchCheckBox.TabIndex = 4;
            _killSwitchCheckBox.Text = "Отключить авто-голосования на сегодня";
            _killSwitchCheckBox.CheckedChanged += OnEditorChanged;
            //
            // PollsSettingsControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_rootLayout);
            Name = "PollsSettingsControl";
            Size = new Size(1100, 711);
            _rootLayout.ResumeLayout(false);
            _rootLayout.PerformLayout();
            _templatesGroup.ResumeLayout(false);
            _templatesLayout.ResumeLayout(false);
            _templatesLayout.PerformLayout();
            _bottomFlow.ResumeLayout(false);
            _bottomFlow.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_progressIntervalNumeric).EndInit();
            ((System.ComponentModel.ISupportInitialize)_historyMaxNumeric).EndInit();
            ResumeLayout(false);
        }
    }
}
