namespace PoproshaykaBot.WinForms.Settings
{
    partial class MiscSettingsControl
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            _mainTableLayout = new TableLayoutPanel();
            _settingsFolderGroupBox = new GroupBox();
            _settingsFolderTableLayout = new TableLayoutPanel();
            _settingsFolderLabel = new Label();
            _openSettingsFolderButton = new Button();
            _actionsGroupBox = new GroupBox();
            _actionsTableLayout = new TableLayoutPanel();
            _resetAllSettingsLabel = new Label();
            _resetAllSettingsButton = new Button();
            _aboutGroupBox = new GroupBox();
            _aboutTableLayout = new TableLayoutPanel();
            _aboutLabel = new Label();
            _aboutButton = new Button();
            _mainTableLayout.SuspendLayout();
            _settingsFolderGroupBox.SuspendLayout();
            _settingsFolderTableLayout.SuspendLayout();
            _actionsGroupBox.SuspendLayout();
            _actionsTableLayout.SuspendLayout();
            _aboutGroupBox.SuspendLayout();
            _aboutTableLayout.SuspendLayout();
            SuspendLayout();
            // 
            // _mainTableLayout
            // 
            _mainTableLayout.ColumnCount = 1;
            _mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _mainTableLayout.Controls.Add(_settingsFolderGroupBox, 0, 0);
            _mainTableLayout.Controls.Add(_actionsGroupBox, 0, 1);
            _mainTableLayout.Controls.Add(_aboutGroupBox, 0, 2);
            _mainTableLayout.Dock = DockStyle.Fill;
            _mainTableLayout.Location = new Point(0, 0);
            _mainTableLayout.Name = "_mainTableLayout";
            _mainTableLayout.RowCount = 4;
            _mainTableLayout.RowStyles.Add(new RowStyle());
            _mainTableLayout.RowStyles.Add(new RowStyle());
            _mainTableLayout.RowStyles.Add(new RowStyle());
            _mainTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _mainTableLayout.Size = new Size(546, 549);
            _mainTableLayout.TabIndex = 0;
            // 
            // _settingsFolderGroupBox
            // 
            _settingsFolderGroupBox.AutoSize = true;
            _settingsFolderGroupBox.Controls.Add(_settingsFolderTableLayout);
            _settingsFolderGroupBox.Dock = DockStyle.Fill;
            _settingsFolderGroupBox.Location = new Point(3, 3);
            _settingsFolderGroupBox.Name = "_settingsFolderGroupBox";
            _settingsFolderGroupBox.Padding = new Padding(10);
            _settingsFolderGroupBox.Size = new Size(540, 89);
            _settingsFolderGroupBox.TabIndex = 0;
            _settingsFolderGroupBox.TabStop = false;
            _settingsFolderGroupBox.Text = "Папка с настройками";
            // 
            // _settingsFolderTableLayout
            // 
            _settingsFolderTableLayout.AutoSize = true;
            _settingsFolderTableLayout.ColumnCount = 2;
            _settingsFolderTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _settingsFolderTableLayout.ColumnStyles.Add(new ColumnStyle());
            _settingsFolderTableLayout.Controls.Add(_settingsFolderLabel, 0, 0);
            _settingsFolderTableLayout.Controls.Add(_openSettingsFolderButton, 1, 0);
            _settingsFolderTableLayout.Dock = DockStyle.Fill;
            _settingsFolderTableLayout.Location = new Point(10, 26);
            _settingsFolderTableLayout.Name = "_settingsFolderTableLayout";
            _settingsFolderTableLayout.RowCount = 1;
            _settingsFolderTableLayout.RowStyles.Add(new RowStyle());
            _settingsFolderTableLayout.Size = new Size(520, 53);
            _settingsFolderTableLayout.TabIndex = 0;
            // 
            // _settingsFolderLabel
            // 
            _settingsFolderLabel.Anchor = AnchorStyles.Left;
            _settingsFolderLabel.AutoSize = true;
            _settingsFolderLabel.Location = new Point(3, 19);
            _settingsFolderLabel.Name = "_settingsFolderLabel";
            _settingsFolderLabel.Size = new Size(332, 15);
            _settingsFolderLabel.TabIndex = 0;
            _settingsFolderLabel.Text = "Открыть папку с файлами настроек и логами приложения";
            // 
            // _openSettingsFolderButton
            // 
            _openSettingsFolderButton.AutoSize = true;
            _openSettingsFolderButton.Location = new Point(418, 3);
            _openSettingsFolderButton.MinimumSize = new Size(92, 0);
            _openSettingsFolderButton.Name = "_openSettingsFolderButton";
            _openSettingsFolderButton.Size = new Size(99, 47);
            _openSettingsFolderButton.TabIndex = 1;
            _openSettingsFolderButton.Text = "Открыть папку";
            _openSettingsFolderButton.UseVisualStyleBackColor = true;
            _openSettingsFolderButton.Click += OnOpenSettingsFolderButtonClicked;
            // 
            // _actionsGroupBox
            // 
            _actionsGroupBox.AutoSize = true;
            _actionsGroupBox.Controls.Add(_actionsTableLayout);
            _actionsGroupBox.Dock = DockStyle.Fill;
            _actionsGroupBox.Location = new Point(3, 98);
            _actionsGroupBox.Name = "_actionsGroupBox";
            _actionsGroupBox.Padding = new Padding(10);
            _actionsGroupBox.Size = new Size(540, 89);
            _actionsGroupBox.TabIndex = 1;
            _actionsGroupBox.TabStop = false;
            _actionsGroupBox.Text = "Действия";
            // 
            // _actionsTableLayout
            // 
            _actionsTableLayout.AutoSize = true;
            _actionsTableLayout.ColumnCount = 2;
            _actionsTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _actionsTableLayout.ColumnStyles.Add(new ColumnStyle());
            _actionsTableLayout.Controls.Add(_resetAllSettingsLabel, 0, 0);
            _actionsTableLayout.Controls.Add(_resetAllSettingsButton, 1, 0);
            _actionsTableLayout.Dock = DockStyle.Fill;
            _actionsTableLayout.Location = new Point(10, 26);
            _actionsTableLayout.Name = "_actionsTableLayout";
            _actionsTableLayout.RowCount = 1;
            _actionsTableLayout.RowStyles.Add(new RowStyle());
            _actionsTableLayout.Size = new Size(520, 53);
            _actionsTableLayout.TabIndex = 0;
            // 
            // _resetAllSettingsLabel
            // 
            _resetAllSettingsLabel.Anchor = AnchorStyles.Left;
            _resetAllSettingsLabel.AutoSize = true;
            _resetAllSettingsLabel.Location = new Point(3, 19);
            _resetAllSettingsLabel.Name = "_resetAllSettingsLabel";
            _resetAllSettingsLabel.Size = new Size(300, 15);
            _resetAllSettingsLabel.TabIndex = 0;
            _resetAllSettingsLabel.Text = "Сбросить все настройки к значениям по умолчанию";
            // 
            // _resetAllSettingsButton
            // 
            _resetAllSettingsButton.AutoSize = true;
            _resetAllSettingsButton.Location = new Point(386, 3);
            _resetAllSettingsButton.MinimumSize = new Size(92, 0);
            _resetAllSettingsButton.Name = "_resetAllSettingsButton";
            _resetAllSettingsButton.Size = new Size(131, 47);
            _resetAllSettingsButton.TabIndex = 1;
            _resetAllSettingsButton.Text = "Сбросить настройки";
            _resetAllSettingsButton.UseVisualStyleBackColor = true;
            _resetAllSettingsButton.Click += OnResetAllSettingsButtonClicked;
            // 
            // _aboutGroupBox
            // 
            _aboutGroupBox.AutoSize = true;
            _aboutGroupBox.Controls.Add(_aboutTableLayout);
            _aboutGroupBox.Dock = DockStyle.Fill;
            _aboutGroupBox.Location = new Point(3, 193);
            _aboutGroupBox.Name = "_aboutGroupBox";
            _aboutGroupBox.Padding = new Padding(10);
            _aboutGroupBox.Size = new Size(540, 89);
            _aboutGroupBox.TabIndex = 2;
            _aboutGroupBox.TabStop = false;
            _aboutGroupBox.Text = "О программе";
            // 
            // _aboutTableLayout
            // 
            _aboutTableLayout.AutoSize = true;
            _aboutTableLayout.ColumnCount = 2;
            _aboutTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _aboutTableLayout.ColumnStyles.Add(new ColumnStyle());
            _aboutTableLayout.Controls.Add(_aboutLabel, 0, 0);
            _aboutTableLayout.Controls.Add(_aboutButton, 1, 0);
            _aboutTableLayout.Dock = DockStyle.Fill;
            _aboutTableLayout.Location = new Point(10, 26);
            _aboutTableLayout.Name = "_aboutTableLayout";
            _aboutTableLayout.RowCount = 1;
            _aboutTableLayout.RowStyles.Add(new RowStyle());
            _aboutTableLayout.Size = new Size(520, 53);
            _aboutTableLayout.TabIndex = 0;
            // 
            // _aboutLabel
            // 
            _aboutLabel.Anchor = AnchorStyles.Left;
            _aboutLabel.AutoSize = true;
            _aboutLabel.Location = new Point(3, 19);
            _aboutLabel.Name = "_aboutLabel";
            _aboutLabel.Size = new Size(202, 15);
            _aboutLabel.TabIndex = 0;
            _aboutLabel.Text = "Информация о версии программы";
            // 
            // _aboutButton
            // 
            _aboutButton.AutoSize = true;
            _aboutButton.Location = new Point(425, 3);
            _aboutButton.MinimumSize = new Size(92, 0);
            _aboutButton.Name = "_aboutButton";
            _aboutButton.Size = new Size(92, 47);
            _aboutButton.TabIndex = 1;
            _aboutButton.Text = "О программе";
            _aboutButton.UseVisualStyleBackColor = true;
            _aboutButton.Click += OnAboutButtonClicked;
            // 
            // MiscSettingsControl
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_mainTableLayout);
            Name = "MiscSettingsControl";
            Size = new Size(546, 549);
            _mainTableLayout.ResumeLayout(false);
            _mainTableLayout.PerformLayout();
            _settingsFolderGroupBox.ResumeLayout(false);
            _settingsFolderGroupBox.PerformLayout();
            _settingsFolderTableLayout.ResumeLayout(false);
            _settingsFolderTableLayout.PerformLayout();
            _actionsGroupBox.ResumeLayout(false);
            _actionsGroupBox.PerformLayout();
            _actionsTableLayout.ResumeLayout(false);
            _actionsTableLayout.PerformLayout();
            _aboutGroupBox.ResumeLayout(false);
            _aboutGroupBox.PerformLayout();
            _aboutTableLayout.ResumeLayout(false);
            _aboutTableLayout.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel _mainTableLayout;
        private GroupBox _settingsFolderGroupBox;
        private TableLayoutPanel _settingsFolderTableLayout;
        private Label _settingsFolderLabel;
        private Button _openSettingsFolderButton;
        private GroupBox _actionsGroupBox;
        private TableLayoutPanel _actionsTableLayout;
        private Label _resetAllSettingsLabel;
        private Button _resetAllSettingsButton;
        private GroupBox _aboutGroupBox;
        private TableLayoutPanel _aboutTableLayout;
        private Label _aboutLabel;
        private Button _aboutButton;
    }
}
