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
            _split = new System.Windows.Forms.SplitContainer();
            _leftLayout = new System.Windows.Forms.TableLayoutPanel();
            _leftButtonsFlow = new System.Windows.Forms.FlowLayoutPanel();
            _rightLayout = new System.Windows.Forms.TableLayoutPanel();
            _profilesListBox = new System.Windows.Forms.ListBox();
            _addButton = new System.Windows.Forms.Button();
            _removeButton = new System.Windows.Forms.Button();
            _duplicateButton = new System.Windows.Forms.Button();
            _importButton = new System.Windows.Forms.Button();
            _applyNowButton = new System.Windows.Forms.Button();
            _nameTextBox = new System.Windows.Forms.TextBox();
            _titleTextBox = new System.Windows.Forms.TextBox();
            _gameBox = new GameAutocompleteBox();
            _tagsTextBox = new System.Windows.Forms.TextBox();
            _languageComboBox = new System.Windows.Forms.ComboBox();
            _nameLabel = new System.Windows.Forms.Label();
            _titleLabel = new System.Windows.Forms.Label();
            _gameLabel = new System.Windows.Forms.Label();
            _tagsLabel = new System.Windows.Forms.Label();
            _languageLabel = new System.Windows.Forms.Label();

            ((System.ComponentModel.ISupportInitialize)_split).BeginInit();
            _split.Panel1.SuspendLayout();
            _split.Panel2.SuspendLayout();
            _split.SuspendLayout();
            _leftLayout.SuspendLayout();
            _leftButtonsFlow.SuspendLayout();
            _rightLayout.SuspendLayout();
            SuspendLayout();

            _split.Name = "_split";
            _split.Dock = System.Windows.Forms.DockStyle.Fill;
            _split.Size = new System.Drawing.Size(700, 440);
            _split.SplitterWidth = 6;
            _split.SplitterDistance = 250;
            _split.Panel1MinSize = 120;
            _split.Panel2MinSize = 120;

            _leftLayout.Name = "_leftLayout";
            _leftLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            _leftLayout.ColumnCount = 1;
            _leftLayout.RowCount = 2;
            _leftLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            _leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            _leftLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            _leftLayout.Padding = new System.Windows.Forms.Padding(4);

            _leftButtonsFlow.Name = "_leftButtonsFlow";
            _leftButtonsFlow.Dock = System.Windows.Forms.DockStyle.Fill;
            _leftButtonsFlow.AutoSize = true;
            _leftButtonsFlow.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            _leftButtonsFlow.WrapContents = true;
            _leftButtonsFlow.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight;
            _leftButtonsFlow.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);

            _addButton.Name = "_addButton";
            _addButton.Text = "+ Добавить";
            _addButton.AutoSize = true;
            _addButton.Margin = new System.Windows.Forms.Padding(0, 0, 4, 4);

            _removeButton.Name = "_removeButton";
            _removeButton.Text = "- Удалить";
            _removeButton.AutoSize = true;
            _removeButton.Margin = new System.Windows.Forms.Padding(0, 0, 4, 4);

            _duplicateButton.Name = "_duplicateButton";
            _duplicateButton.Text = "⎘ Дублировать";
            _duplicateButton.AutoSize = true;
            _duplicateButton.Margin = new System.Windows.Forms.Padding(0, 0, 4, 4);

            _importButton.Name = "_importButton";
            _importButton.Text = "⇪ Импорт…";
            _importButton.AutoSize = true;
            _importButton.Margin = new System.Windows.Forms.Padding(0, 0, 0, 4);

            _leftButtonsFlow.Controls.Add(_addButton);
            _leftButtonsFlow.Controls.Add(_removeButton);
            _leftButtonsFlow.Controls.Add(_duplicateButton);
            _leftButtonsFlow.Controls.Add(_importButton);

            _profilesListBox.Name = "_profilesListBox";
            _profilesListBox.Dock = System.Windows.Forms.DockStyle.Fill;
            _profilesListBox.IntegralHeight = false;

            _leftLayout.Controls.Add(_leftButtonsFlow, 0, 0);
            _leftLayout.Controls.Add(_profilesListBox, 0, 1);

            _split.Panel1.Controls.Add(_leftLayout);

            _rightLayout.Name = "_rightLayout";
            _rightLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            _rightLayout.ColumnCount = 2;
            _rightLayout.RowCount = 6;
            _rightLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            _rightLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            _rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            _rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            _rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 160F));
            _rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            _rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            _rightLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            _rightLayout.Padding = new System.Windows.Forms.Padding(8);

            var labelAnchor = System.Windows.Forms.AnchorStyles.Left;
            var fieldAnchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            var labelMargin = new System.Windows.Forms.Padding(0, 6, 6, 4);
            var fieldMargin = new System.Windows.Forms.Padding(0, 3, 0, 4);

            _nameLabel.Name = "_nameLabel";
            _nameLabel.Text = "Name:";
            _nameLabel.AutoSize = true;
            _nameLabel.Anchor = labelAnchor;
            _nameLabel.Margin = labelMargin;

            _nameTextBox.Name = "_nameTextBox";
            _nameTextBox.Anchor = fieldAnchor;
            _nameTextBox.Margin = fieldMargin;

            _titleLabel.Name = "_titleLabel";
            _titleLabel.Text = "Title:";
            _titleLabel.AutoSize = true;
            _titleLabel.Anchor = labelAnchor;
            _titleLabel.Margin = labelMargin;

            _titleTextBox.Name = "_titleTextBox";
            _titleTextBox.Anchor = fieldAnchor;
            _titleTextBox.Margin = fieldMargin;

            _gameLabel.Name = "_gameLabel";
            _gameLabel.Text = "Game:";
            _gameLabel.AutoSize = true;
            _gameLabel.Anchor = labelAnchor;
            _gameLabel.Margin = labelMargin;

            _gameBox.Name = "_gameBox";
            _gameBox.Dock = System.Windows.Forms.DockStyle.Fill;
            _gameBox.Margin = new System.Windows.Forms.Padding(0, 3, 0, 4);

            _tagsLabel.Name = "_tagsLabel";
            _tagsLabel.Text = "Tags:";
            _tagsLabel.AutoSize = true;
            _tagsLabel.Anchor = labelAnchor;
            _tagsLabel.Margin = labelMargin;

            _tagsTextBox.Name = "_tagsTextBox";
            _tagsTextBox.Anchor = fieldAnchor;
            _tagsTextBox.Margin = fieldMargin;

            _languageLabel.Name = "_languageLabel";
            _languageLabel.Text = "Lang:";
            _languageLabel.AutoSize = true;
            _languageLabel.Anchor = labelAnchor;
            _languageLabel.Margin = labelMargin;

            _languageComboBox.Name = "_languageComboBox";
            _languageComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            _languageComboBox.Anchor = fieldAnchor;
            _languageComboBox.Margin = fieldMargin;

            _applyNowButton.Name = "_applyNowButton";
            _applyNowButton.Text = "Применить сейчас";
            _applyNowButton.AutoSize = true;
            _applyNowButton.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            _applyNowButton.Margin = new System.Windows.Forms.Padding(0, 8, 0, 0);
            _applyNowButton.MinimumSize = new System.Drawing.Size(0, 28);

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

            _split.Panel2.Controls.Add(_rightLayout);

            _profilesListBox.SelectedIndexChanged += OnSelectionChanged;
            _addButton.Click += OnAddClicked;
            _removeButton.Click += OnRemoveClicked;
            _duplicateButton.Click += OnDuplicateClicked;
            _importButton.Click += OnImportClicked;
            _applyNowButton.Click += OnApplyNowClicked;
            _nameTextBox.TextChanged += OnEditorChanged;
            _titleTextBox.TextChanged += OnEditorChanged;
            _tagsTextBox.TextChanged += OnEditorChanged;
            _languageComboBox.SelectedIndexChanged += OnEditorChanged;
            _gameBox.SelectionChanged += OnEditorChanged;

            Controls.Add(_split);
            Name = "BroadcastProfilesSettingsControl";
            Size = new System.Drawing.Size(700, 440);
            MinimumSize = new System.Drawing.Size(560, 360);

            _rightLayout.ResumeLayout(false);
            _rightLayout.PerformLayout();
            _leftButtonsFlow.ResumeLayout(false);
            _leftButtonsFlow.PerformLayout();
            _leftLayout.ResumeLayout(false);
            _leftLayout.PerformLayout();
            _split.Panel1.ResumeLayout(false);
            _split.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_split).EndInit();
            _split.ResumeLayout(false);
            ResumeLayout(false);
        }
    }
}
