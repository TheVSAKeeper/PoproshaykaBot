namespace PoproshaykaBot.WinForms.Forms.Polls;

partial class PollProfileEditDialog
{
    private System.ComponentModel.IContainer components = null;

    private TableLayoutPanel _mainLayout;
    private Label _nameLbl;
    private TextBox _nameTextBox;
    private Label _titleLbl;
    private TextBox _titleTextBox;
    private Label _choicesLbl;
    private TextBox _choicesTextBox;
    private Label _durationLbl;
    private NumericUpDown _durationNumeric;
    private Label _channelPointsLbl;
    private TableLayoutPanel _channelPointsLayout;
    private CheckBox _channelPointsCheckBox;
    private NumericUpDown _channelPointsPerVoteNumeric;
    private Label _autoTriggerLbl;
    private ComboBox _autoTriggerComboBox;
    private Label _cooldownLbl;
    private NumericUpDown _cooldownNumeric;
    private FlowLayoutPanel _buttonsFlow;
    private Button _saveAsProfileButton;
    private Button _runButton;
    private Button _cancelButton;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _mainLayout = new TableLayoutPanel();
        _nameLbl = new Label();
        _nameTextBox = new TextBox();
        _titleLbl = new Label();
        _titleTextBox = new TextBox();
        _choicesLbl = new Label();
        _choicesTextBox = new TextBox();
        _durationLbl = new Label();
        _durationNumeric = new NumericUpDown();
        _channelPointsLbl = new Label();
        _channelPointsLayout = new TableLayoutPanel();
        _channelPointsCheckBox = new CheckBox();
        _channelPointsPerVoteNumeric = new NumericUpDown();
        _autoTriggerLbl = new Label();
        _autoTriggerComboBox = new ComboBox();
        _cooldownLbl = new Label();
        _cooldownNumeric = new NumericUpDown();
        _buttonsFlow = new FlowLayoutPanel();
        _saveAsProfileButton = new Button();
        _runButton = new Button();
        _cancelButton = new Button();
        _mainLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_durationNumeric).BeginInit();
        _channelPointsLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_channelPointsPerVoteNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_cooldownNumeric).BeginInit();
        _buttonsFlow.SuspendLayout();
        SuspendLayout();
        // 
        // _mainLayout
        // 
        _mainLayout.ColumnCount = 2;
        _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _mainLayout.Controls.Add(_nameLbl, 0, 0);
        _mainLayout.Controls.Add(_nameTextBox, 1, 0);
        _mainLayout.Controls.Add(_titleLbl, 0, 1);
        _mainLayout.Controls.Add(_titleTextBox, 1, 1);
        _mainLayout.Controls.Add(_choicesLbl, 0, 2);
        _mainLayout.Controls.Add(_choicesTextBox, 1, 2);
        _mainLayout.Controls.Add(_durationLbl, 0, 3);
        _mainLayout.Controls.Add(_durationNumeric, 1, 3);
        _mainLayout.Controls.Add(_channelPointsLbl, 0, 4);
        _mainLayout.Controls.Add(_channelPointsLayout, 1, 4);
        _mainLayout.Controls.Add(_autoTriggerLbl, 0, 5);
        _mainLayout.Controls.Add(_autoTriggerComboBox, 1, 5);
        _mainLayout.Controls.Add(_cooldownLbl, 0, 6);
        _mainLayout.Controls.Add(_cooldownNumeric, 1, 6);
        _mainLayout.Controls.Add(_buttonsFlow, 0, 7);
        _mainLayout.Dock = DockStyle.Fill;
        _mainLayout.Location = new Point(0, 0);
        _mainLayout.Name = "_mainLayout";
        _mainLayout.Padding = new Padding(8);
        _mainLayout.RowCount = 8;
        _mainLayout.RowStyles.Add(new RowStyle());
        _mainLayout.RowStyles.Add(new RowStyle());
        _mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _mainLayout.RowStyles.Add(new RowStyle());
        _mainLayout.RowStyles.Add(new RowStyle());
        _mainLayout.RowStyles.Add(new RowStyle());
        _mainLayout.RowStyles.Add(new RowStyle());
        _mainLayout.RowStyles.Add(new RowStyle());
        _mainLayout.Size = new Size(702, 527);
        _mainLayout.TabIndex = 0;
        // 
        // _nameLbl
        // 
        _nameLbl.Anchor = AnchorStyles.Left;
        _nameLbl.AutoSize = true;
        _nameLbl.Location = new Point(8, 16);
        _nameLbl.Margin = new Padding(0, 6, 6, 4);
        _nameLbl.Name = "_nameLbl";
        _nameLbl.Size = new Size(87, 15);
        _nameLbl.TabIndex = 0;
        _nameLbl.Text = "Имя профиля:";
        // 
        // _nameTextBox
        // 
        _nameTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _nameTextBox.Location = new Point(138, 11);
        _nameTextBox.Margin = new Padding(0, 3, 0, 4);
        _nameTextBox.Name = "_nameTextBox";
        _nameTextBox.PlaceholderText = "Заполните, чтобы сохранить как профиль";
        _nameTextBox.Size = new Size(556, 23);
        _nameTextBox.TabIndex = 0;
        _nameTextBox.TextChanged += OnFieldChanged;
        // 
        // _titleLbl
        // 
        _titleLbl.Anchor = AnchorStyles.Left;
        _titleLbl.AutoSize = true;
        _titleLbl.Location = new Point(8, 46);
        _titleLbl.Margin = new Padding(0, 6, 6, 4);
        _titleLbl.Name = "_titleLbl";
        _titleLbl.Size = new Size(51, 15);
        _titleLbl.TabIndex = 1;
        _titleLbl.Text = "Вопрос:";
        // 
        // _titleTextBox
        // 
        _titleTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _titleTextBox.Location = new Point(138, 41);
        _titleTextBox.Margin = new Padding(0, 3, 0, 4);
        _titleTextBox.Name = "_titleTextBox";
        _titleTextBox.PlaceholderText = "Например: «Какой следующий босс?»";
        _titleTextBox.Size = new Size(556, 23);
        _titleTextBox.TabIndex = 1;
        _titleTextBox.TextChanged += OnFieldChanged;
        // 
        // _choicesLbl
        // 
        _choicesLbl.AutoSize = true;
        _choicesLbl.Location = new Point(8, 74);
        _choicesLbl.Margin = new Padding(0, 6, 6, 4);
        _choicesLbl.Name = "_choicesLbl";
        _choicesLbl.Size = new Size(64, 15);
        _choicesLbl.TabIndex = 2;
        _choicesLbl.Text = "Варианты:";
        // 
        // _choicesTextBox
        // 
        _choicesTextBox.Dock = DockStyle.Fill;
        _choicesTextBox.Location = new Point(138, 71);
        _choicesTextBox.Margin = new Padding(0, 3, 0, 4);
        _choicesTextBox.Multiline = true;
        _choicesTextBox.Name = "_choicesTextBox";
        _choicesTextBox.PlaceholderText = "По одному варианту в строке (от 2 до 5)";
        _choicesTextBox.ScrollBars = ScrollBars.Vertical;
        _choicesTextBox.Size = new Size(556, 291);
        _choicesTextBox.TabIndex = 2;
        _choicesTextBox.TextChanged += OnFieldChanged;
        // 
        // _durationLbl
        // 
        _durationLbl.Anchor = AnchorStyles.Left;
        _durationLbl.AutoSize = true;
        _durationLbl.Location = new Point(8, 374);
        _durationLbl.Margin = new Padding(0, 6, 6, 4);
        _durationLbl.Name = "_durationLbl";
        _durationLbl.Size = new Size(116, 15);
        _durationLbl.TabIndex = 3;
        _durationLbl.Text = "Длительность (сек):";
        // 
        // _durationNumeric
        // 
        _durationNumeric.Anchor = AnchorStyles.Left;
        _durationNumeric.Location = new Point(138, 369);
        _durationNumeric.Margin = new Padding(0, 3, 0, 4);
        _durationNumeric.Maximum = new decimal(new int[] { 1800, 0, 0, 0 });
        _durationNumeric.Minimum = new decimal(new int[] { 15, 0, 0, 0 });
        _durationNumeric.Name = "_durationNumeric";
        _durationNumeric.Size = new Size(120, 23);
        _durationNumeric.TabIndex = 3;
        _durationNumeric.Value = new decimal(new int[] { 60, 0, 0, 0 });
        // 
        // _channelPointsLbl
        // 
        _channelPointsLbl.Anchor = AnchorStyles.Left;
        _channelPointsLbl.AutoSize = true;
        _channelPointsLbl.Location = new Point(8, 404);
        _channelPointsLbl.Margin = new Padding(0, 6, 6, 4);
        _channelPointsLbl.Name = "_channelPointsLbl";
        _channelPointsLbl.Size = new Size(90, 15);
        _channelPointsLbl.TabIndex = 4;
        _channelPointsLbl.Text = "Channel Points:";
        // 
        // _channelPointsLayout
        // 
        _channelPointsLayout.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _channelPointsLayout.AutoSize = true;
        _channelPointsLayout.ColumnCount = 2;
        _channelPointsLayout.ColumnStyles.Add(new ColumnStyle());
        _channelPointsLayout.ColumnStyles.Add(new ColumnStyle());
        _channelPointsLayout.Controls.Add(_channelPointsCheckBox, 0, 0);
        _channelPointsLayout.Controls.Add(_channelPointsPerVoteNumeric, 1, 0);
        _channelPointsLayout.Location = new Point(138, 396);
        _channelPointsLayout.Margin = new Padding(0);
        _channelPointsLayout.Name = "_channelPointsLayout";
        _channelPointsLayout.RowCount = 1;
        _channelPointsLayout.RowStyles.Add(new RowStyle());
        _channelPointsLayout.Size = new Size(556, 30);
        _channelPointsLayout.TabIndex = 4;
        // 
        // _channelPointsCheckBox
        // 
        _channelPointsCheckBox.Anchor = AnchorStyles.Left;
        _channelPointsCheckBox.AutoSize = true;
        _channelPointsCheckBox.Location = new Point(0, 6);
        _channelPointsCheckBox.Margin = new Padding(0, 6, 8, 4);
        _channelPointsCheckBox.Name = "_channelPointsCheckBox";
        _channelPointsCheckBox.Size = new Size(162, 19);
        _channelPointsCheckBox.TabIndex = 0;
        _channelPointsCheckBox.Text = "разрешить, цена голоса:";
        _channelPointsCheckBox.CheckedChanged += OnChannelPointsCheckedChanged;
        // 
        // _channelPointsPerVoteNumeric
        // 
        _channelPointsPerVoteNumeric.Anchor = AnchorStyles.Left;
        _channelPointsPerVoteNumeric.Enabled = false;
        _channelPointsPerVoteNumeric.Location = new Point(170, 3);
        _channelPointsPerVoteNumeric.Margin = new Padding(0, 3, 0, 4);
        _channelPointsPerVoteNumeric.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
        _channelPointsPerVoteNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _channelPointsPerVoteNumeric.Name = "_channelPointsPerVoteNumeric";
        _channelPointsPerVoteNumeric.Size = new Size(120, 23);
        _channelPointsPerVoteNumeric.TabIndex = 1;
        _channelPointsPerVoteNumeric.Value = new decimal(new int[] { 100, 0, 0, 0 });
        // 
        // _autoTriggerLbl
        // 
        _autoTriggerLbl.Anchor = AnchorStyles.Left;
        _autoTriggerLbl.AutoSize = true;
        _autoTriggerLbl.Location = new Point(8, 434);
        _autoTriggerLbl.Margin = new Padding(0, 6, 6, 4);
        _autoTriggerLbl.Name = "_autoTriggerLbl";
        _autoTriggerLbl.Size = new Size(83, 15);
        _autoTriggerLbl.TabIndex = 5;
        _autoTriggerLbl.Text = "Авто-триггер:";
        // 
        // _autoTriggerComboBox
        // 
        _autoTriggerComboBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _autoTriggerComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _autoTriggerComboBox.Location = new Point(138, 429);
        _autoTriggerComboBox.Margin = new Padding(0, 3, 0, 4);
        _autoTriggerComboBox.Name = "_autoTriggerComboBox";
        _autoTriggerComboBox.Size = new Size(556, 23);
        _autoTriggerComboBox.TabIndex = 5;
        // 
        // _cooldownLbl
        // 
        _cooldownLbl.Anchor = AnchorStyles.Left;
        _cooldownLbl.AutoSize = true;
        _cooldownLbl.Location = new Point(8, 464);
        _cooldownLbl.Margin = new Padding(0, 6, 6, 4);
        _cooldownLbl.Name = "_cooldownLbl";
        _cooldownLbl.Size = new Size(99, 15);
        _cooldownLbl.TabIndex = 6;
        _cooldownLbl.Text = "Cooldown (мин):";
        // 
        // _cooldownNumeric
        // 
        _cooldownNumeric.Anchor = AnchorStyles.Left;
        _cooldownNumeric.Location = new Point(138, 459);
        _cooldownNumeric.Margin = new Padding(0, 3, 0, 4);
        _cooldownNumeric.Maximum = new decimal(new int[] { 1440, 0, 0, 0 });
        _cooldownNumeric.Name = "_cooldownNumeric";
        _cooldownNumeric.Size = new Size(120, 23);
        _cooldownNumeric.TabIndex = 6;
        // 
        // _buttonsFlow
        // 
        _buttonsFlow.Anchor = AnchorStyles.Right;
        _buttonsFlow.AutoSize = true;
        _mainLayout.SetColumnSpan(_buttonsFlow, 2);
        _buttonsFlow.Controls.Add(_saveAsProfileButton);
        _buttonsFlow.Controls.Add(_runButton);
        _buttonsFlow.Controls.Add(_cancelButton);
        _buttonsFlow.Location = new Point(311, 494);
        _buttonsFlow.Margin = new Padding(0, 8, 0, 0);
        _buttonsFlow.Name = "_buttonsFlow";
        _buttonsFlow.Size = new Size(383, 25);
        _buttonsFlow.TabIndex = 7;
        // 
        // _saveAsProfileButton
        // 
        _saveAsProfileButton.AutoSize = true;
        _saveAsProfileButton.Enabled = false;
        _saveAsProfileButton.Location = new Point(0, 0);
        _saveAsProfileButton.Margin = new Padding(0, 0, 12, 0);
        _saveAsProfileButton.Name = "_saveAsProfileButton";
        _saveAsProfileButton.Size = new Size(180, 25);
        _saveAsProfileButton.TabIndex = 0;
        _saveAsProfileButton.Text = "💾 Сохранить как профиль";
        _saveAsProfileButton.Click += OnSaveAsProfileClicked;
        // 
        // _runButton
        // 
        _runButton.AutoSize = true;
        _runButton.Enabled = false;
        _runButton.Location = new Point(192, 0);
        _runButton.Margin = new Padding(0, 0, 6, 0);
        _runButton.Name = "_runButton";
        _runButton.Size = new Size(110, 25);
        _runButton.TabIndex = 1;
        _runButton.Text = "▶ Запустить";
        _runButton.Click += OnRunClicked;
        // 
        // _cancelButton
        // 
        _cancelButton.AutoSize = true;
        _cancelButton.DialogResult = DialogResult.Cancel;
        _cancelButton.Location = new Point(308, 0);
        _cancelButton.Margin = new Padding(0);
        _cancelButton.Name = "_cancelButton";
        _cancelButton.Size = new Size(75, 25);
        _cancelButton.TabIndex = 2;
        _cancelButton.Text = "Отмена";
        // 
        // PollProfileEditDialog
        // 
        AcceptButton = _runButton;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = _cancelButton;
        ClientSize = new Size(702, 527);
        Controls.Add(_mainLayout);
        MinimizeBox = false;
        MinimumSize = new Size(560, 460);
        Name = "PollProfileEditDialog";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Создать голосование";
        _mainLayout.ResumeLayout(false);
        _mainLayout.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_durationNumeric).EndInit();
        _channelPointsLayout.ResumeLayout(false);
        _channelPointsLayout.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_channelPointsPerVoteNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)_cooldownNumeric).EndInit();
        _buttonsFlow.ResumeLayout(false);
        _buttonsFlow.PerformLayout();
        ResumeLayout(false);
    }
}
