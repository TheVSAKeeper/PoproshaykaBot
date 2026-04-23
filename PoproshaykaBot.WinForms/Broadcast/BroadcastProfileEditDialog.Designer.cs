using PoproshaykaBot.WinForms.Settings;

namespace PoproshaykaBot.WinForms.Broadcast
{
    partial class BroadcastProfileEditDialog
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.TableLayoutPanel _mainLayout;
        private System.Windows.Forms.Label _nameLbl;
        private System.Windows.Forms.TextBox _nameTextBox;
        private System.Windows.Forms.Label _titleLbl;
        private System.Windows.Forms.TextBox _titleTextBox;
        private System.Windows.Forms.Label _gameLbl;
        private GameAutocompleteBox _gameBox;
        private System.Windows.Forms.Label _tagsLbl;
        private System.Windows.Forms.TextBox _tagsTextBox;
        private System.Windows.Forms.Label _languageLbl;
        private System.Windows.Forms.ComboBox _languageComboBox;
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
            _nameLbl = new Label();
            _nameTextBox = new TextBox();
            _titleLbl = new Label();
            _titleTextBox = new TextBox();
            _gameLbl = new Label();
            _gameBox = new GameAutocompleteBox();
            _tagsLbl = new Label();
            _tagsTextBox = new TextBox();
            _languageLbl = new Label();
            _languageComboBox = new ComboBox();
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
            _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
            _mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _mainLayout.Controls.Add(_nameLbl, 0, 0);
            _mainLayout.Controls.Add(_nameTextBox, 1, 0);
            _mainLayout.Controls.Add(_titleLbl, 0, 1);
            _mainLayout.Controls.Add(_titleTextBox, 1, 1);
            _mainLayout.Controls.Add(_gameLbl, 0, 2);
            _mainLayout.Controls.Add(_gameBox, 1, 2);
            _mainLayout.Controls.Add(_tagsLbl, 0, 3);
            _mainLayout.Controls.Add(_tagsTextBox, 1, 3);
            _mainLayout.Controls.Add(_languageLbl, 0, 4);
            _mainLayout.Controls.Add(_languageComboBox, 1, 4);
            _mainLayout.Controls.Add(_buttonsFlow, 0, 5);
            _mainLayout.Dock = DockStyle.Fill;
            _mainLayout.Location = new Point(0, 0);
            _mainLayout.Name = "_mainLayout";
            _mainLayout.Padding = new Padding(8);
            _mainLayout.RowCount = 6;
            _mainLayout.RowStyles.Add(new RowStyle());
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            _mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120F));
            _mainLayout.RowStyles.Add(new RowStyle());
            _mainLayout.RowStyles.Add(new RowStyle());
            _mainLayout.RowStyles.Add(new RowStyle());
            _mainLayout.Size = new Size(509, 320);
            _mainLayout.TabIndex = 0;
            // 
            // _nameLbl
            // 
            _nameLbl.Anchor = AnchorStyles.Left;
            _nameLbl.AutoSize = true;
            _nameLbl.Location = new Point(8, 16);
            _nameLbl.Margin = new Padding(0, 6, 6, 4);
            _nameLbl.Name = "_nameLbl";
            _nameLbl.Size = new Size(34, 15);
            _nameLbl.TabIndex = 0;
            _nameLbl.Text = "Имя:";
            // 
            // _nameTextBox
            // 
            _nameTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _nameTextBox.Location = new Point(88, 11);
            _nameTextBox.Margin = new Padding(0, 3, 0, 4);
            _nameTextBox.Name = "_nameTextBox";
            _nameTextBox.Size = new Size(413, 23);
            _nameTextBox.TabIndex = 1;
            // 
            // _titleLbl
            // 
            _titleLbl.Anchor = AnchorStyles.Left;
            _titleLbl.AutoSize = true;
            _titleLbl.Location = new Point(8, 62);
            _titleLbl.Margin = new Padding(0, 6, 6, 4);
            _titleLbl.Name = "_titleLbl";
            _titleLbl.Size = new Size(68, 15);
            _titleLbl.TabIndex = 2;
            _titleLbl.Text = "Заголовок:";
            // 
            // _titleTextBox
            // 
            _titleTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _titleTextBox.Location = new Point(88, 41);
            _titleTextBox.Margin = new Padding(0, 3, 0, 4);
            _titleTextBox.MinimumSize = new Size(0, 54);
            _titleTextBox.Multiline = true;
            _titleTextBox.Name = "_titleTextBox";
            _titleTextBox.ScrollBars = ScrollBars.Vertical;
            _titleTextBox.Size = new Size(413, 54);
            _titleTextBox.TabIndex = 3;
            // 
            // _gameLbl
            // 
            _gameLbl.Anchor = AnchorStyles.Left;
            _gameLbl.AutoSize = true;
            _gameLbl.Location = new Point(8, 153);
            _gameLbl.Margin = new Padding(0, 6, 6, 4);
            _gameLbl.Name = "_gameLbl";
            _gameLbl.Size = new Size(37, 15);
            _gameLbl.TabIndex = 4;
            _gameLbl.Text = "Игра:";
            // 
            // _gameBox
            // 
            _gameBox.Dock = DockStyle.Fill;
            _gameBox.Location = new Point(88, 103);
            _gameBox.Margin = new Padding(0, 3, 0, 4);
            _gameBox.Name = "_gameBox";
            _gameBox.Size = new Size(413, 113);
            _gameBox.TabIndex = 5;
            // 
            // _tagsLbl
            // 
            _tagsLbl.Anchor = AnchorStyles.Left;
            _tagsLbl.AutoSize = true;
            _tagsLbl.Location = new Point(8, 228);
            _tagsLbl.Margin = new Padding(0, 6, 6, 4);
            _tagsLbl.Name = "_tagsLbl";
            _tagsLbl.Size = new Size(34, 15);
            _tagsLbl.TabIndex = 6;
            _tagsLbl.Text = "Теги:";
            // 
            // _tagsTextBox
            // 
            _tagsTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _tagsTextBox.Location = new Point(88, 223);
            _tagsTextBox.Margin = new Padding(0, 3, 0, 4);
            _tagsTextBox.Name = "_tagsTextBox";
            _tagsTextBox.PlaceholderText = "через запятую";
            _tagsTextBox.Size = new Size(413, 23);
            _tagsTextBox.TabIndex = 7;
            // 
            // _languageLbl
            // 
            _languageLbl.Anchor = AnchorStyles.Left;
            _languageLbl.AutoSize = true;
            _languageLbl.Location = new Point(8, 258);
            _languageLbl.Margin = new Padding(0, 6, 6, 4);
            _languageLbl.Name = "_languageLbl";
            _languageLbl.Size = new Size(37, 15);
            _languageLbl.TabIndex = 8;
            _languageLbl.Text = "Язык:";
            // 
            // _languageComboBox
            // 
            _languageComboBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _languageComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            _languageComboBox.Location = new Point(88, 253);
            _languageComboBox.Margin = new Padding(0, 3, 0, 4);
            _languageComboBox.Name = "_languageComboBox";
            _languageComboBox.Size = new Size(413, 23);
            _languageComboBox.TabIndex = 9;
            // 
            // _buttonsFlow
            // 
            _buttonsFlow.Anchor = AnchorStyles.Right;
            _buttonsFlow.AutoSize = true;
            _mainLayout.SetColumnSpan(_buttonsFlow, 2);
            _buttonsFlow.Controls.Add(_okButton);
            _buttonsFlow.Controls.Add(_cancelButton);
            _buttonsFlow.Location = new Point(345, 288);
            _buttonsFlow.Margin = new Padding(0, 8, 0, 0);
            _buttonsFlow.Name = "_buttonsFlow";
            _buttonsFlow.Size = new Size(156, 25);
            _buttonsFlow.TabIndex = 10;
            // 
            // _okButton
            // 
            _okButton.AutoSize = true;
            _okButton.DialogResult = DialogResult.OK;
            _okButton.Enabled = false;
            _okButton.Location = new Point(0, 0);
            _okButton.Margin = new Padding(0, 0, 6, 0);
            _okButton.Name = "_okButton";
            _okButton.Size = new Size(75, 25);
            _okButton.TabIndex = 0;
            _okButton.Text = "OK";
            // 
            // _cancelButton
            // 
            _cancelButton.AutoSize = true;
            _cancelButton.DialogResult = DialogResult.Cancel;
            _cancelButton.Location = new Point(81, 0);
            _cancelButton.Margin = new Padding(0);
            _cancelButton.Name = "_cancelButton";
            _cancelButton.Size = new Size(75, 25);
            _cancelButton.TabIndex = 1;
            _cancelButton.Text = "Отмена";
            // 
            // BroadcastProfileEditDialog
            // 
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            ClientSize = new Size(509, 320);
            Controls.Add(_mainLayout);
            MinimizeBox = false;
            MinimumSize = new Size(420, 320);
            Name = "BroadcastProfileEditDialog";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "Редактировать профиль";
            _mainLayout.ResumeLayout(false);
            _mainLayout.PerformLayout();
            _buttonsFlow.ResumeLayout(false);
            _buttonsFlow.PerformLayout();
            ResumeLayout(false);
        }
    }
}
