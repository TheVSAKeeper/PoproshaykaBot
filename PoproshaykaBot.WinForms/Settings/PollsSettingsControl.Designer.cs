namespace PoproshaykaBot.WinForms.Settings
{
    partial class PollsSettingsControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TableLayoutPanel _rootLayout;
        private System.Windows.Forms.GroupBox _profilesGroup;
        private System.Windows.Forms.TableLayoutPanel _profilesLayout;
        private System.Windows.Forms.ListBox _profilesListBox;
        private System.Windows.Forms.FlowLayoutPanel _profilesButtonsFlow;
        private System.Windows.Forms.Button _addButton;
        private System.Windows.Forms.Button _removeButton;
        private System.Windows.Forms.Button _duplicateButton;
        private System.Windows.Forms.Button _startButton;
        private System.Windows.Forms.TableLayoutPanel _editorLayout;
        private System.Windows.Forms.Label _nameLabel;
        private System.Windows.Forms.TextBox _nameTextBox;
        private System.Windows.Forms.Label _titleLabel;
        private System.Windows.Forms.TextBox _titleTextBox;
        private System.Windows.Forms.Label _choicesLabel;
        private System.Windows.Forms.TextBox _choicesTextBox;
        private System.Windows.Forms.Label _durationLabel;
        private System.Windows.Forms.NumericUpDown _durationNumeric;
        private System.Windows.Forms.CheckBox _channelPointsCheckBox;
        private System.Windows.Forms.NumericUpDown _channelPointsPerVoteNumeric;
        private System.Windows.Forms.Label _autoTriggerLabel;
        private System.Windows.Forms.ComboBox _autoTriggerComboBox;
        private System.Windows.Forms.Label _cooldownLabel;
        private System.Windows.Forms.NumericUpDown _cooldownNumeric;
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
            _profilesGroup = new GroupBox();
            _profilesLayout = new TableLayoutPanel();
            _profilesListBox = new ListBox();
            _editorLayout = new TableLayoutPanel();
            _nameLabel = new Label();
            _nameTextBox = new TextBox();
            _titleLabel = new Label();
            _titleTextBox = new TextBox();
            _durationLabel = new Label();
            _durationNumeric = new NumericUpDown();
            _channelPointsCheckBox = new CheckBox();
            _channelPointsPerVoteNumeric = new NumericUpDown();
            _autoTriggerLabel = new Label();
            _autoTriggerComboBox = new ComboBox();
            _cooldownLabel = new Label();
            _cooldownNumeric = new NumericUpDown();
            _choicesLabel = new Label();
            _choicesTextBox = new TextBox();
            _profilesButtonsFlow = new FlowLayoutPanel();
            _addButton = new Button();
            _removeButton = new Button();
            _duplicateButton = new Button();
            _startButton = new Button();
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
            bottomFlow = new FlowLayoutPanel();
            _progressIntervalLabel = new Label();
            _progressIntervalNumeric = new NumericUpDown();
            _historyMaxLabel = new Label();
            _historyMaxNumeric = new NumericUpDown();
            _killSwitchCheckBox = new CheckBox();
            _rootLayout.SuspendLayout();
            _profilesGroup.SuspendLayout();
            _profilesLayout.SuspendLayout();
            _editorLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_durationNumeric).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_channelPointsPerVoteNumeric).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_cooldownNumeric).BeginInit();
            _profilesButtonsFlow.SuspendLayout();
            _templatesGroup.SuspendLayout();
            _templatesLayout.SuspendLayout();
            bottomFlow.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_progressIntervalNumeric).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_historyMaxNumeric).BeginInit();
            SuspendLayout();
            // 
            // _rootLayout
            // 
            _rootLayout.ColumnCount = 1;
            _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _rootLayout.Controls.Add(_profilesGroup, 0, 0);
            _rootLayout.Controls.Add(_templatesGroup, 0, 1);
            _rootLayout.Controls.Add(bottomFlow, 0, 2);
            _rootLayout.Dock = DockStyle.Fill;
            _rootLayout.Location = new Point(0, 0);
            _rootLayout.Name = "_rootLayout";
            _rootLayout.RowCount = 4;
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
            _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
            _rootLayout.RowStyles.Add(new RowStyle());
            _rootLayout.RowStyles.Add(new RowStyle());
            _rootLayout.Size = new Size(1100, 711);
            _rootLayout.TabIndex = 0;
            // 
            // _profilesGroup
            // 
            _profilesGroup.Controls.Add(_profilesLayout);
            _profilesGroup.Dock = DockStyle.Fill;
            _profilesGroup.Location = new Point(3, 3);
            _profilesGroup.Name = "_profilesGroup";
            _profilesGroup.Padding = new Padding(8);
            _profilesGroup.Size = new Size(1094, 326);
            _profilesGroup.TabIndex = 0;
            _profilesGroup.TabStop = false;
            _profilesGroup.Text = "Профили голосования";
            // 
            // _profilesLayout
            // 
            _profilesLayout.ColumnCount = 2;
            _profilesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            _profilesLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
            _profilesLayout.Controls.Add(_profilesListBox, 0, 0);
            _profilesLayout.Controls.Add(_editorLayout, 1, 0);
            _profilesLayout.Controls.Add(_profilesButtonsFlow, 0, 1);
            _profilesLayout.Dock = DockStyle.Fill;
            _profilesLayout.Location = new Point(8, 24);
            _profilesLayout.Name = "_profilesLayout";
            _profilesLayout.RowCount = 2;
            _profilesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _profilesLayout.RowStyles.Add(new RowStyle());
            _profilesLayout.Size = new Size(1078, 294);
            _profilesLayout.TabIndex = 0;
            // 
            // _profilesListBox
            // 
            _profilesListBox.Dock = DockStyle.Fill;
            _profilesListBox.FormattingEnabled = true;
            _profilesListBox.IntegralHeight = false;
            _profilesListBox.ItemHeight = 15;
            _profilesListBox.Location = new Point(3, 3);
            _profilesListBox.Name = "_profilesListBox";
            _profilesListBox.Size = new Size(371, 251);
            _profilesListBox.TabIndex = 0;
            _profilesListBox.SelectedIndexChanged += OnProfileSelectionChanged;
            // 
            // _editorLayout
            // 
            _editorLayout.ColumnCount = 2;
            _editorLayout.ColumnStyles.Add(new ColumnStyle());
            _editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _editorLayout.Controls.Add(_nameLabel, 0, 0);
            _editorLayout.Controls.Add(_nameTextBox, 1, 0);
            _editorLayout.Controls.Add(_titleLabel, 0, 1);
            _editorLayout.Controls.Add(_titleTextBox, 1, 1);
            _editorLayout.Controls.Add(_durationLabel, 0, 2);
            _editorLayout.Controls.Add(_durationNumeric, 1, 2);
            _editorLayout.Controls.Add(_channelPointsCheckBox, 0, 3);
            _editorLayout.Controls.Add(_channelPointsPerVoteNumeric, 1, 3);
            _editorLayout.Controls.Add(_autoTriggerLabel, 0, 4);
            _editorLayout.Controls.Add(_autoTriggerComboBox, 1, 4);
            _editorLayout.Controls.Add(_cooldownLabel, 0, 5);
            _editorLayout.Controls.Add(_cooldownNumeric, 1, 5);
            _editorLayout.Controls.Add(_choicesLabel, 0, 6);
            _editorLayout.Controls.Add(_choicesTextBox, 1, 6);
            _editorLayout.Dock = DockStyle.Fill;
            _editorLayout.Location = new Point(380, 3);
            _editorLayout.Name = "_editorLayout";
            _editorLayout.RowCount = 7;
            _editorLayout.RowStyles.Add(new RowStyle());
            _editorLayout.RowStyles.Add(new RowStyle());
            _editorLayout.RowStyles.Add(new RowStyle());
            _editorLayout.RowStyles.Add(new RowStyle());
            _editorLayout.RowStyles.Add(new RowStyle());
            _editorLayout.RowStyles.Add(new RowStyle());
            _editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _editorLayout.Size = new Size(695, 251);
            _editorLayout.TabIndex = 1;
            // 
            // _nameLabel
            // 
            _nameLabel.AutoSize = true;
            _nameLabel.Location = new Point(3, 7);
            _nameLabel.Margin = new Padding(3, 7, 6, 3);
            _nameLabel.Name = "_nameLabel";
            _nameLabel.Size = new Size(34, 15);
            _nameLabel.TabIndex = 0;
            _nameLabel.Text = "Имя:";
            // 
            // _nameTextBox
            // 
            _nameTextBox.Dock = DockStyle.Fill;
            _nameTextBox.Location = new Point(184, 3);
            _nameTextBox.Margin = new Padding(0, 3, 0, 3);
            _nameTextBox.Name = "_nameTextBox";
            _nameTextBox.Size = new Size(511, 23);
            _nameTextBox.TabIndex = 1;
            _nameTextBox.TextChanged += OnEditorChanged;
            // 
            // _titleLabel
            // 
            _titleLabel.AutoSize = true;
            _titleLabel.Location = new Point(3, 36);
            _titleLabel.Margin = new Padding(3, 7, 6, 3);
            _titleLabel.Name = "_titleLabel";
            _titleLabel.Size = new Size(68, 15);
            _titleLabel.TabIndex = 2;
            _titleLabel.Text = "Заголовок:";
            // 
            // _titleTextBox
            // 
            _titleTextBox.Dock = DockStyle.Fill;
            _titleTextBox.Location = new Point(184, 32);
            _titleTextBox.Margin = new Padding(0, 3, 0, 3);
            _titleTextBox.Name = "_titleTextBox";
            _titleTextBox.Size = new Size(511, 23);
            _titleTextBox.TabIndex = 3;
            _titleTextBox.TextChanged += OnEditorChanged;
            // 
            // _durationLabel
            // 
            _durationLabel.AutoSize = true;
            _durationLabel.Location = new Point(3, 65);
            _durationLabel.Margin = new Padding(3, 7, 6, 3);
            _durationLabel.Name = "_durationLabel";
            _durationLabel.Size = new Size(116, 15);
            _durationLabel.TabIndex = 4;
            _durationLabel.Text = "Длительность (сек):";
            // 
            // _durationNumeric
            // 
            _durationNumeric.Location = new Point(187, 61);
            _durationNumeric.Maximum = new decimal(new int[] { 1800, 0, 0, 0 });
            _durationNumeric.Minimum = new decimal(new int[] { 15, 0, 0, 0 });
            _durationNumeric.Name = "_durationNumeric";
            _durationNumeric.Size = new Size(100, 23);
            _durationNumeric.TabIndex = 5;
            _durationNumeric.Value = new decimal(new int[] { 60, 0, 0, 0 });
            _durationNumeric.ValueChanged += OnEditorChanged;
            // 
            // _channelPointsCheckBox
            // 
            _channelPointsCheckBox.AutoSize = true;
            _channelPointsCheckBox.Location = new Point(3, 94);
            _channelPointsCheckBox.Margin = new Padding(3, 7, 6, 3);
            _channelPointsCheckBox.Name = "_channelPointsCheckBox";
            _channelPointsCheckBox.Size = new Size(109, 19);
            _channelPointsCheckBox.TabIndex = 6;
            _channelPointsCheckBox.Text = "Channel Points:";
            _channelPointsCheckBox.CheckedChanged += OnChannelPointsCheckedChanged;
            // 
            // _channelPointsPerVoteNumeric
            // 
            _channelPointsPerVoteNumeric.Enabled = false;
            _channelPointsPerVoteNumeric.Location = new Point(187, 90);
            _channelPointsPerVoteNumeric.Maximum = new decimal(new int[] { 1000000, 0, 0, 0 });
            _channelPointsPerVoteNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            _channelPointsPerVoteNumeric.Name = "_channelPointsPerVoteNumeric";
            _channelPointsPerVoteNumeric.Size = new Size(120, 23);
            _channelPointsPerVoteNumeric.TabIndex = 7;
            _channelPointsPerVoteNumeric.Value = new decimal(new int[] { 100, 0, 0, 0 });
            _channelPointsPerVoteNumeric.ValueChanged += OnEditorChanged;
            // 
            // _autoTriggerLabel
            // 
            _autoTriggerLabel.AutoSize = true;
            _autoTriggerLabel.Location = new Point(3, 123);
            _autoTriggerLabel.Margin = new Padding(3, 7, 6, 3);
            _autoTriggerLabel.Name = "_autoTriggerLabel";
            _autoTriggerLabel.Size = new Size(83, 15);
            _autoTriggerLabel.TabIndex = 8;
            _autoTriggerLabel.Text = "Авто-триггер:";
            // 
            // _autoTriggerComboBox
            // 
            _autoTriggerComboBox.Dock = DockStyle.Fill;
            _autoTriggerComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _autoTriggerComboBox.Location = new Point(184, 119);
            _autoTriggerComboBox.Margin = new Padding(0, 3, 0, 3);
            _autoTriggerComboBox.Name = "_autoTriggerComboBox";
            _autoTriggerComboBox.Size = new Size(511, 23);
            _autoTriggerComboBox.TabIndex = 9;
            _autoTriggerComboBox.SelectedIndexChanged += OnEditorChanged;
            // 
            // _cooldownLabel
            // 
            _cooldownLabel.AutoSize = true;
            _cooldownLabel.Location = new Point(3, 152);
            _cooldownLabel.Margin = new Padding(3, 7, 6, 3);
            _cooldownLabel.Name = "_cooldownLabel";
            _cooldownLabel.Size = new Size(99, 15);
            _cooldownLabel.TabIndex = 10;
            _cooldownLabel.Text = "Cooldown (мин):";
            // 
            // _cooldownNumeric
            // 
            _cooldownNumeric.Location = new Point(187, 148);
            _cooldownNumeric.Maximum = new decimal(new int[] { 1440, 0, 0, 0 });
            _cooldownNumeric.Name = "_cooldownNumeric";
            _cooldownNumeric.Size = new Size(100, 23);
            _cooldownNumeric.TabIndex = 11;
            _cooldownNumeric.ValueChanged += OnEditorChanged;
            // 
            // _choicesLabel
            // 
            _choicesLabel.AutoSize = true;
            _choicesLabel.Location = new Point(3, 181);
            _choicesLabel.Margin = new Padding(3, 7, 6, 3);
            _choicesLabel.Name = "_choicesLabel";
            _choicesLabel.Size = new Size(175, 15);
            _choicesLabel.TabIndex = 12;
            _choicesLabel.Text = "Варианты (один в строке, 2–5):";
            // 
            // _choicesTextBox
            // 
            _choicesTextBox.Dock = DockStyle.Fill;
            _choicesTextBox.Location = new Point(184, 177);
            _choicesTextBox.Margin = new Padding(0, 3, 0, 3);
            _choicesTextBox.MinimumSize = new Size(0, 80);
            _choicesTextBox.Multiline = true;
            _choicesTextBox.Name = "_choicesTextBox";
            _choicesTextBox.ScrollBars = ScrollBars.Vertical;
            _choicesTextBox.Size = new Size(511, 80);
            _choicesTextBox.TabIndex = 13;
            _choicesTextBox.TextChanged += OnEditorChanged;
            // 
            // _profilesButtonsFlow
            // 
            _profilesButtonsFlow.AutoSize = true;
            _profilesButtonsFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _profilesLayout.SetColumnSpan(_profilesButtonsFlow, 2);
            _profilesButtonsFlow.Controls.Add(_addButton);
            _profilesButtonsFlow.Controls.Add(_removeButton);
            _profilesButtonsFlow.Controls.Add(_duplicateButton);
            _profilesButtonsFlow.Controls.Add(_startButton);
            _profilesButtonsFlow.Dock = DockStyle.Fill;
            _profilesButtonsFlow.Location = new Point(3, 260);
            _profilesButtonsFlow.Name = "_profilesButtonsFlow";
            _profilesButtonsFlow.Size = new Size(1072, 31);
            _profilesButtonsFlow.TabIndex = 2;
            _profilesButtonsFlow.WrapContents = false;
            // 
            // _addButton
            // 
            _addButton.AutoSize = true;
            _addButton.Location = new Point(3, 3);
            _addButton.Name = "_addButton";
            _addButton.Size = new Size(80, 25);
            _addButton.TabIndex = 0;
            _addButton.Text = "+ Добавить";
            _addButton.Click += OnAddClicked;
            // 
            // _removeButton
            // 
            _removeButton.AutoSize = true;
            _removeButton.Location = new Point(89, 3);
            _removeButton.Name = "_removeButton";
            _removeButton.Size = new Size(75, 25);
            _removeButton.TabIndex = 1;
            _removeButton.Text = "− Удалить";
            _removeButton.Click += OnRemoveClicked;
            // 
            // _duplicateButton
            // 
            _duplicateButton.AutoSize = true;
            _duplicateButton.Location = new Point(170, 3);
            _duplicateButton.Name = "_duplicateButton";
            _duplicateButton.Size = new Size(75, 25);
            _duplicateButton.TabIndex = 2;
            _duplicateButton.Text = "⎘ Дубль";
            _duplicateButton.Click += OnDuplicateClicked;
            // 
            // _startButton
            // 
            _startButton.AutoSize = true;
            _startButton.Location = new Point(268, 3);
            _startButton.Margin = new Padding(20, 3, 3, 3);
            _startButton.Name = "_startButton";
            _startButton.Size = new Size(85, 25);
            _startButton.TabIndex = 3;
            _startButton.Text = "▶ Запустить";
            _startButton.Click += OnStartClicked;
            // 
            // _templatesGroup
            // 
            _templatesGroup.Controls.Add(_templatesLayout);
            _templatesGroup.Dock = DockStyle.Fill;
            _templatesGroup.Location = new Point(3, 335);
            _templatesGroup.Name = "_templatesGroup";
            _templatesGroup.Padding = new Padding(8);
            _templatesGroup.Size = new Size(1094, 266);
            _templatesGroup.TabIndex = 1;
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
            _templatesLayout.Size = new Size(1078, 234);
            _templatesLayout.TabIndex = 0;
            // 
            // _startEnabledCheckBox
            // 
            _startEnabledCheckBox.AutoSize = true;
            _startEnabledCheckBox.Location = new Point(3, 7);
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
            _startTemplateTextBox.Location = new Point(97, 3);
            _startTemplateTextBox.Margin = new Padding(0, 3, 0, 3);
            _startTemplateTextBox.Name = "_startTemplateTextBox";
            _startTemplateTextBox.Size = new Size(981, 23);
            _startTemplateTextBox.TabIndex = 1;
            _startTemplateTextBox.TextChanged += OnEditorChanged;
            // 
            // _progressEnabledCheckBox
            // 
            _progressEnabledCheckBox.AutoSize = true;
            _progressEnabledCheckBox.Location = new Point(3, 53);
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
            _progressTemplateTextBox.Location = new Point(97, 49);
            _progressTemplateTextBox.Margin = new Padding(0, 3, 0, 3);
            _progressTemplateTextBox.Name = "_progressTemplateTextBox";
            _progressTemplateTextBox.Size = new Size(981, 23);
            _progressTemplateTextBox.TabIndex = 3;
            _progressTemplateTextBox.TextChanged += OnEditorChanged;
            // 
            // _endEnabledCheckBox
            // 
            _endEnabledCheckBox.AutoSize = true;
            _endEnabledCheckBox.Location = new Point(3, 99);
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
            _endTemplateTextBox.Location = new Point(97, 95);
            _endTemplateTextBox.Margin = new Padding(0, 3, 0, 3);
            _endTemplateTextBox.Name = "_endTemplateTextBox";
            _endTemplateTextBox.Size = new Size(981, 23);
            _endTemplateTextBox.TabIndex = 5;
            _endTemplateTextBox.TextChanged += OnEditorChanged;
            // 
            // _terminatedEnabledCheckBox
            // 
            _terminatedEnabledCheckBox.AutoSize = true;
            _terminatedEnabledCheckBox.Location = new Point(3, 145);
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
            _terminatedTemplateTextBox.Location = new Point(97, 141);
            _terminatedTemplateTextBox.Margin = new Padding(0, 3, 0, 3);
            _terminatedTemplateTextBox.Name = "_terminatedTemplateTextBox";
            _terminatedTemplateTextBox.Size = new Size(981, 23);
            _terminatedTemplateTextBox.TabIndex = 7;
            _terminatedTemplateTextBox.TextChanged += OnEditorChanged;
            // 
            // _archivedEnabledCheckBox
            // 
            _archivedEnabledCheckBox.AutoSize = true;
            _archivedEnabledCheckBox.Location = new Point(3, 191);
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
            _archivedTemplateTextBox.Location = new Point(97, 187);
            _archivedTemplateTextBox.Margin = new Padding(0, 3, 0, 3);
            _archivedTemplateTextBox.Name = "_archivedTemplateTextBox";
            _archivedTemplateTextBox.Size = new Size(981, 23);
            _archivedTemplateTextBox.TabIndex = 9;
            _archivedTemplateTextBox.TextChanged += OnEditorChanged;
            // 
            // bottomFlow
            // 
            bottomFlow.Controls.Add(_progressIntervalLabel);
            bottomFlow.Controls.Add(_progressIntervalNumeric);
            bottomFlow.Controls.Add(_historyMaxLabel);
            bottomFlow.Controls.Add(_historyMaxNumeric);
            bottomFlow.Controls.Add(_killSwitchCheckBox);
            bottomFlow.Location = new Point(3, 607);
            bottomFlow.Name = "bottomFlow";
            bottomFlow.Size = new Size(200, 100);
            bottomFlow.TabIndex = 2;
            // 
            // _progressIntervalLabel
            // 
            _progressIntervalLabel.AutoSize = true;
            _progressIntervalLabel.Location = new Point(3, 6);
            _progressIntervalLabel.Margin = new Padding(3, 6, 3, 0);
            _progressIntervalLabel.Name = "_progressIntervalLabel";
            _progressIntervalLabel.Size = new Size(131, 15);
            _progressIntervalLabel.TabIndex = 0;
            _progressIntervalLabel.Text = "Promo-интервал (сек):";
            // 
            // _progressIntervalNumeric
            // 
            _progressIntervalNumeric.Location = new Point(3, 24);
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
            _historyMaxLabel.Location = new Point(20, 56);
            _historyMaxLabel.Margin = new Padding(20, 6, 3, 0);
            _historyMaxLabel.Name = "_historyMaxLabel";
            _historyMaxLabel.Size = new Size(138, 15);
            _historyMaxLabel.TabIndex = 2;
            _historyMaxLabel.Text = "Макс. записей истории:";
            // 
            // _historyMaxNumeric
            // 
            _historyMaxNumeric.Location = new Point(3, 74);
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
            _killSwitchCheckBox.Location = new Point(20, 106);
            _killSwitchCheckBox.Margin = new Padding(20, 6, 3, 3);
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
            _profilesGroup.ResumeLayout(false);
            _profilesLayout.ResumeLayout(false);
            _profilesLayout.PerformLayout();
            _editorLayout.ResumeLayout(false);
            _editorLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_durationNumeric).EndInit();
            ((System.ComponentModel.ISupportInitialize)_channelPointsPerVoteNumeric).EndInit();
            ((System.ComponentModel.ISupportInitialize)_cooldownNumeric).EndInit();
            _profilesButtonsFlow.ResumeLayout(false);
            _profilesButtonsFlow.PerformLayout();
            _templatesGroup.ResumeLayout(false);
            _templatesLayout.ResumeLayout(false);
            _templatesLayout.PerformLayout();
            bottomFlow.ResumeLayout(false);
            bottomFlow.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_progressIntervalNumeric).EndInit();
            ((System.ComponentModel.ISupportInitialize)_historyMaxNumeric).EndInit();
            ResumeLayout(false);
        }

        private FlowLayoutPanel bottomFlow;
    }
}
