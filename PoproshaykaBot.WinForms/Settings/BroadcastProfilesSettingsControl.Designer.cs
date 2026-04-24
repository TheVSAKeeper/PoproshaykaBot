namespace PoproshaykaBot.WinForms.Settings
{
    partial class BroadcastProfilesSettingsControl
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.SplitContainer _split;
        private System.Windows.Forms.TableLayoutPanel _leftLayout;
        private System.Windows.Forms.FlowLayoutPanel _leftButtonsFlow;
        private System.Windows.Forms.TableLayoutPanel _rightLayout;
        private System.Windows.Forms.ListBox _profilesListBox;
        private System.Windows.Forms.Button _addButton;
        private System.Windows.Forms.Button _removeButton;
        private System.Windows.Forms.Button _duplicateButton;
        private System.Windows.Forms.Button _importButton;
        private System.Windows.Forms.Button _applyNowButton;
        private System.Windows.Forms.TextBox _nameTextBox;
        private System.Windows.Forms.TextBox _titleTextBox;
        private GameAutocompleteBox _gameBox;
        private System.Windows.Forms.TextBox _tagsTextBox;
        private System.Windows.Forms.ComboBox _languageComboBox;
        private System.Windows.Forms.Label _nameLabel;
        private System.Windows.Forms.Label _titleLabel;
        private System.Windows.Forms.Label _gameLabel;
        private System.Windows.Forms.Label _tagsLabel;
        private System.Windows.Forms.Label _languageLabel;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            _split = new SplitContainer();
            _leftLayout = new TableLayoutPanel();
            _leftButtonsFlow = new FlowLayoutPanel();
            _addButton = new Button();
            _removeButton = new Button();
            _duplicateButton = new Button();
            _importButton = new Button();
            _profilesListBox = new ListBox();
            _rightLayout = new TableLayoutPanel();
            _nameLabel = new Label();
            _nameTextBox = new TextBox();
            _titleLabel = new Label();
            _titleTextBox = new TextBox();
            _gameLabel = new Label();
            _gameBox = new GameAutocompleteBox();
            _tagsLabel = new Label();
            _tagsTextBox = new TextBox();
            _languageLabel = new Label();
            _languageComboBox = new ComboBox();
            _applyNowButton = new Button();
            ((System.ComponentModel.ISupportInitialize)_split).BeginInit();
            _split.Panel1.SuspendLayout();
            _split.Panel2.SuspendLayout();
            _split.SuspendLayout();
            _leftLayout.SuspendLayout();
            _leftButtonsFlow.SuspendLayout();
            _rightLayout.SuspendLayout();
            SuspendLayout();
            // 
            // _split
            // 
            _split.Dock = DockStyle.Fill;
            _split.Location = new Point(0, 0);
            _split.Margin = new Padding(6, 7, 6, 7);
            _split.Name = "_split";
            // 
            // _split.Panel1
            // 
            _split.Panel1.Controls.Add(_leftLayout);
            _split.Panel1MinSize = 120;
            // 
            // _split.Panel2
            // 
            _split.Panel2.Controls.Add(_rightLayout);
            _split.Panel2MinSize = 120;
            _split.Size = new Size(1757, 1069);
            _split.SplitterDistance = 627;
            _split.SplitterWidth = 13;
            _split.TabIndex = 0;
            // 
            // _leftLayout
            // 
            _leftLayout.ColumnCount = 1;
            _leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _leftLayout.Controls.Add(_leftButtonsFlow, 0, 0);
            _leftLayout.Controls.Add(_profilesListBox, 0, 1);
            _leftLayout.Dock = DockStyle.Fill;
            _leftLayout.Location = new Point(0, 0);
            _leftLayout.Margin = new Padding(6, 7, 6, 7);
            _leftLayout.Name = "_leftLayout";
            _leftLayout.Padding = new Padding(9, 10, 9, 10);
            _leftLayout.RowCount = 2;
            _leftLayout.RowStyles.Add(new RowStyle());
            _leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _leftLayout.Size = new Size(627, 1069);
            _leftLayout.TabIndex = 0;
            // 
            // _leftButtonsFlow
            // 
            _leftButtonsFlow.AutoSize = true;
            _leftButtonsFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _leftButtonsFlow.Controls.Add(_addButton);
            _leftButtonsFlow.Controls.Add(_removeButton);
            _leftButtonsFlow.Controls.Add(_duplicateButton);
            _leftButtonsFlow.Controls.Add(_importButton);
            _leftButtonsFlow.Dock = DockStyle.Fill;
            _leftButtonsFlow.Location = new Point(9, 10);
            _leftButtonsFlow.Margin = new Padding(0, 0, 0, 10);
            _leftButtonsFlow.Name = "_leftButtonsFlow";
            _leftButtonsFlow.Size = new Size(609, 504);
            _leftButtonsFlow.TabIndex = 0;
            // 
            // _addButton
            // 
            _addButton.AutoSize = true;
            _addButton.Location = new Point(0, 0);
            _addButton.Margin = new Padding(0, 0, 9, 10);
            _addButton.Name = "_addButton";
            _addButton.Size = new Size(369, 116);
            _addButton.TabIndex = 0;
            _addButton.Text = "+ Добавить";
            _addButton.Click += OnAddClicked;
            // 
            // _removeButton
            // 
            _removeButton.AutoSize = true;
            _removeButton.Location = new Point(0, 126);
            _removeButton.Margin = new Padding(0, 0, 9, 10);
            _removeButton.Name = "_removeButton";
            _removeButton.Size = new Size(309, 116);
            _removeButton.TabIndex = 1;
            _removeButton.Text = "- Удалить";
            _removeButton.Click += OnRemoveClicked;
            // 
            // _duplicateButton
            // 
            _duplicateButton.AutoSize = true;
            _duplicateButton.Location = new Point(0, 252);
            _duplicateButton.Margin = new Padding(0, 0, 9, 10);
            _duplicateButton.Name = "_duplicateButton";
            _duplicateButton.Size = new Size(467, 116);
            _duplicateButton.TabIndex = 2;
            _duplicateButton.Text = "⎘ Дублировать";
            _duplicateButton.Click += OnDuplicateClicked;
            // 
            // _importButton
            // 
            _importButton.AutoSize = true;
            _importButton.Location = new Point(0, 378);
            _importButton.Margin = new Padding(0, 0, 0, 10);
            _importButton.Name = "_importButton";
            _importButton.Size = new Size(362, 116);
            _importButton.TabIndex = 3;
            _importButton.Text = "⇪ Импорт…";
            _importButton.Click += OnImportClicked;
            // 
            // _profilesListBox
            // 
            _profilesListBox.Dock = DockStyle.Fill;
            _profilesListBox.IntegralHeight = false;
            _profilesListBox.ItemHeight = 37;
            _profilesListBox.Location = new Point(15, 531);
            _profilesListBox.Margin = new Padding(6, 7, 6, 7);
            _profilesListBox.Name = "_profilesListBox";
            _profilesListBox.Size = new Size(597, 521);
            _profilesListBox.TabIndex = 1;
            _profilesListBox.SelectedIndexChanged += OnSelectionChanged;
            // 
            // _rightLayout
            // 
            _rightLayout.ColumnCount = 2;
            _rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 171F));
            _rightLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _rightLayout.Controls.Add(_nameLabel, 0, 0);
            _rightLayout.Controls.Add(_nameTextBox, 1, 0);
            _rightLayout.Controls.Add(_titleLabel, 0, 1);
            _rightLayout.Controls.Add(_titleTextBox, 1, 1);
            _rightLayout.Controls.Add(_gameLabel, 0, 2);
            _rightLayout.Controls.Add(_gameBox, 1, 2);
            _rightLayout.Controls.Add(_tagsLabel, 0, 3);
            _rightLayout.Controls.Add(_tagsTextBox, 1, 3);
            _rightLayout.Controls.Add(_languageLabel, 0, 4);
            _rightLayout.Controls.Add(_languageComboBox, 1, 4);
            _rightLayout.Controls.Add(_applyNowButton, 1, 5);
            _rightLayout.Dock = DockStyle.Fill;
            _rightLayout.Location = new Point(0, 0);
            _rightLayout.Margin = new Padding(6, 7, 6, 7);
            _rightLayout.Name = "_rightLayout";
            _rightLayout.Padding = new Padding(17, 20, 17, 20);
            _rightLayout.RowCount = 6;
            _rightLayout.RowStyles.Add(new RowStyle());
            _rightLayout.RowStyles.Add(new RowStyle());
            _rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 395F));
            _rightLayout.RowStyles.Add(new RowStyle());
            _rightLayout.RowStyles.Add(new RowStyle());
            _rightLayout.RowStyles.Add(new RowStyle());
            _rightLayout.Size = new Size(1117, 1069);
            _rightLayout.TabIndex = 0;
            // 
            // _nameLabel
            // 
            _nameLabel.Anchor = AnchorStyles.Left;
            _nameLabel.AutoSize = true;
            _nameLabel.Location = new Point(17, 35);
            _nameLabel.Margin = new Padding(0, 15, 13, 10);
            _nameLabel.Name = "_nameLabel";
            _nameLabel.Size = new Size(94, 37);
            _nameLabel.TabIndex = 0;
            _nameLabel.Text = "Name:";
            // 
            // _nameTextBox
            // 
            _nameTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _nameTextBox.Location = new Point(188, 28);
            _nameTextBox.Margin = new Padding(0, 7, 0, 10);
            _nameTextBox.Name = "_nameTextBox";
            _nameTextBox.Size = new Size(912, 43);
            _nameTextBox.TabIndex = 1;
            _nameTextBox.TextChanged += OnEditorChanged;
            // 
            // _titleLabel
            // 
            _titleLabel.Anchor = AnchorStyles.Left;
            _titleLabel.AutoSize = true;
            _titleLabel.Location = new Point(17, 97);
            _titleLabel.Margin = new Padding(0, 15, 13, 10);
            _titleLabel.Name = "_titleLabel";
            _titleLabel.Size = new Size(74, 37);
            _titleLabel.TabIndex = 2;
            _titleLabel.Text = "Title:";
            // 
            // _titleTextBox
            // 
            _titleTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _titleTextBox.Location = new Point(188, 90);
            _titleTextBox.Margin = new Padding(0, 7, 0, 10);
            _titleTextBox.Name = "_titleTextBox";
            _titleTextBox.Size = new Size(912, 43);
            _titleTextBox.TabIndex = 3;
            _titleTextBox.TextChanged += OnEditorChanged;
            // 
            // _gameLabel
            // 
            _gameLabel.Anchor = AnchorStyles.Left;
            _gameLabel.AutoSize = true;
            _gameLabel.Location = new Point(17, 325);
            _gameLabel.Margin = new Padding(0, 15, 13, 10);
            _gameLabel.Name = "_gameLabel";
            _gameLabel.Size = new Size(93, 37);
            _gameLabel.TabIndex = 4;
            _gameLabel.Text = "Game:";
            // 
            // _gameBox
            // 
            _gameBox.Dock = DockStyle.Fill;
            _gameBox.Location = new Point(188, 151);
            _gameBox.Margin = new Padding(0, 7, 0, 10);
            _gameBox.Name = "_gameBox";
            _gameBox.Size = new Size(912, 378);
            _gameBox.TabIndex = 5;
            _gameBox.SelectionChanged += OnEditorChanged;
            // 
            // _tagsLabel
            // 
            _tagsLabel.Anchor = AnchorStyles.Left;
            _tagsLabel.AutoSize = true;
            _tagsLabel.Location = new Point(17, 554);
            _tagsLabel.Margin = new Padding(0, 15, 13, 10);
            _tagsLabel.Name = "_tagsLabel";
            _tagsLabel.Size = new Size(75, 37);
            _tagsLabel.TabIndex = 6;
            _tagsLabel.Text = "Tags:";
            // 
            // _tagsTextBox
            // 
            _tagsTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _tagsTextBox.Location = new Point(188, 547);
            _tagsTextBox.Margin = new Padding(0, 7, 0, 10);
            _tagsTextBox.Name = "_tagsTextBox";
            _tagsTextBox.Size = new Size(912, 43);
            _tagsTextBox.TabIndex = 7;
            _tagsTextBox.TextChanged += OnEditorChanged;
            // 
            // _languageLabel
            // 
            _languageLabel.Anchor = AnchorStyles.Left;
            _languageLabel.AutoSize = true;
            _languageLabel.Location = new Point(17, 616);
            _languageLabel.Margin = new Padding(0, 15, 13, 10);
            _languageLabel.Name = "_languageLabel";
            _languageLabel.Size = new Size(81, 37);
            _languageLabel.TabIndex = 8;
            _languageLabel.Text = "Lang:";
            // 
            // _languageComboBox
            // 
            _languageComboBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _languageComboBox.Location = new Point(188, 608);
            _languageComboBox.Margin = new Padding(0, 7, 0, 10);
            _languageComboBox.Name = "_languageComboBox";
            _languageComboBox.Size = new Size(912, 45);
            _languageComboBox.TabIndex = 9;
            _languageComboBox.SelectedIndexChanged += OnEditorChanged;
            // 
            // _applyNowButton
            // 
            _applyNowButton.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _applyNowButton.AutoSize = true;
            _applyNowButton.Location = new Point(188, 808);
            _applyNowButton.Margin = new Padding(0, 20, 0, 0);
            _applyNowButton.MinimumSize = new Size(0, 69);
            _applyNowButton.Name = "_applyNowButton";
            _applyNowButton.Size = new Size(912, 116);
            _applyNowButton.TabIndex = 10;
            _applyNowButton.Text = "Применить сейчас";
            _applyNowButton.Click += OnApplyNowClicked;
            // 
            // BroadcastProfilesSettingsControl
            // 
            AutoScaleDimensions = new SizeF(15F, 37F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_split);
            Margin = new Padding(6, 7, 6, 7);
            MinimumSize = new Size(1200, 888);
            Name = "BroadcastProfilesSettingsControl";
            Size = new Size(1757, 1069);
            _split.Panel1.ResumeLayout(false);
            _split.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_split).EndInit();
            _split.ResumeLayout(false);
            _leftLayout.ResumeLayout(false);
            _leftLayout.PerformLayout();
            _leftButtonsFlow.ResumeLayout(false);
            _leftButtonsFlow.PerformLayout();
            _rightLayout.ResumeLayout(false);
            _rightLayout.PerformLayout();
            ResumeLayout(false);
        }
    }
}
