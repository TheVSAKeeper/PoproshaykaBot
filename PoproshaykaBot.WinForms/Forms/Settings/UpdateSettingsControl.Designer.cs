namespace PoproshaykaBot.WinForms.Forms.Settings
{
    partial class UpdateSettingsControl
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
            _statusGroupBox = new GroupBox();
            _statusTableLayout = new TableLayoutPanel();
            _currentVersionLabel = new Label();
            _statusLabel = new Label();
            _parametersGroupBox = new GroupBox();
            _parametersTableLayout = new TableLayoutPanel();
            _autoCheckCheckBox = new CheckBox();
            _intervalLabel = new Label();
            _intervalNumeric = new NumericUpDown();
            _autoInstallCheckBox = new CheckBox();
            _allowFrameworkDependentCheckBox = new CheckBox();
            _repositoryLabel = new Label();
            _repositoryTextBox = new TextBox();
            _repositoryHintLabel = new Label();
            _actionsGroupBox = new GroupBox();
            _actionsTableLayout = new TableLayoutPanel();
            _checkNowButton = new Button();
            _installButton = new Button();
            _mainTableLayout.SuspendLayout();
            _statusGroupBox.SuspendLayout();
            _statusTableLayout.SuspendLayout();
            _parametersGroupBox.SuspendLayout();
            _parametersTableLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_intervalNumeric).BeginInit();
            _actionsGroupBox.SuspendLayout();
            _actionsTableLayout.SuspendLayout();
            SuspendLayout();
            //
            // _mainTableLayout
            //
            _mainTableLayout.ColumnCount = 1;
            _mainTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _mainTableLayout.Controls.Add(_statusGroupBox, 0, 0);
            _mainTableLayout.Controls.Add(_parametersGroupBox, 0, 1);
            _mainTableLayout.Controls.Add(_actionsGroupBox, 0, 2);
            _mainTableLayout.Dock = DockStyle.Fill;
            _mainTableLayout.Location = new Point(0, 0);
            _mainTableLayout.Name = "_mainTableLayout";
            _mainTableLayout.RowCount = 4;
            _mainTableLayout.RowStyles.Add(new RowStyle());
            _mainTableLayout.RowStyles.Add(new RowStyle());
            _mainTableLayout.RowStyles.Add(new RowStyle());
            _mainTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _mainTableLayout.Size = new Size(593, 529);
            _mainTableLayout.TabIndex = 0;
            //
            // _statusGroupBox
            //
            _statusGroupBox.AutoSize = true;
            _statusGroupBox.Controls.Add(_statusTableLayout);
            _statusGroupBox.Dock = DockStyle.Fill;
            _statusGroupBox.Location = new Point(3, 3);
            _statusGroupBox.Name = "_statusGroupBox";
            _statusGroupBox.Padding = new Padding(10);
            _statusGroupBox.Size = new Size(587, 75);
            _statusGroupBox.TabIndex = 0;
            _statusGroupBox.TabStop = false;
            _statusGroupBox.Text = "Состояние";
            //
            // _statusTableLayout
            //
            _statusTableLayout.AutoSize = true;
            _statusTableLayout.ColumnCount = 1;
            _statusTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _statusTableLayout.Controls.Add(_currentVersionLabel, 0, 0);
            _statusTableLayout.Controls.Add(_statusLabel, 0, 1);
            _statusTableLayout.Dock = DockStyle.Fill;
            _statusTableLayout.Location = new Point(10, 26);
            _statusTableLayout.Name = "_statusTableLayout";
            _statusTableLayout.RowCount = 2;
            _statusTableLayout.RowStyles.Add(new RowStyle());
            _statusTableLayout.RowStyles.Add(new RowStyle());
            _statusTableLayout.Size = new Size(567, 39);
            _statusTableLayout.TabIndex = 0;
            //
            // _currentVersionLabel
            //
            _currentVersionLabel.Anchor = AnchorStyles.Left;
            _currentVersionLabel.AutoSize = true;
            _currentVersionLabel.Name = "_currentVersionLabel";
            _currentVersionLabel.TabIndex = 0;
            _currentVersionLabel.Text = "Текущая версия:";
            //
            // _statusLabel
            //
            _statusLabel.Anchor = AnchorStyles.Left;
            _statusLabel.AutoSize = true;
            _statusLabel.ForeColor = Color.Gray;
            _statusLabel.Name = "_statusLabel";
            _statusLabel.TabIndex = 1;
            _statusLabel.Text = "Проверка обновлений ещё не выполнялась.";
            //
            // _parametersGroupBox
            //
            _parametersGroupBox.AutoSize = true;
            _parametersGroupBox.Controls.Add(_parametersTableLayout);
            _parametersGroupBox.Dock = DockStyle.Fill;
            _parametersGroupBox.Location = new Point(3, 84);
            _parametersGroupBox.Name = "_parametersGroupBox";
            _parametersGroupBox.Padding = new Padding(10);
            _parametersGroupBox.Size = new Size(587, 113);
            _parametersGroupBox.TabIndex = 1;
            _parametersGroupBox.TabStop = false;
            _parametersGroupBox.Text = "Параметры";
            //
            // _parametersTableLayout
            //
            _parametersTableLayout.AutoSize = true;
            _parametersTableLayout.ColumnCount = 2;
            _parametersTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _parametersTableLayout.ColumnStyles.Add(new ColumnStyle());
            _parametersTableLayout.Controls.Add(_autoCheckCheckBox, 0, 0);
            _parametersTableLayout.Controls.Add(_intervalLabel, 0, 1);
            _parametersTableLayout.Controls.Add(_intervalNumeric, 1, 1);
            _parametersTableLayout.Controls.Add(_autoInstallCheckBox, 0, 2);
            _parametersTableLayout.Controls.Add(_allowFrameworkDependentCheckBox, 0, 3);
            _parametersTableLayout.Controls.Add(_repositoryLabel, 0, 4);
            _parametersTableLayout.Controls.Add(_repositoryTextBox, 0, 5);
            _parametersTableLayout.Controls.Add(_repositoryHintLabel, 0, 6);
            _parametersTableLayout.Dock = DockStyle.Fill;
            _parametersTableLayout.Location = new Point(10, 26);
            _parametersTableLayout.Name = "_parametersTableLayout";
            _parametersTableLayout.RowCount = 7;
            _parametersTableLayout.RowStyles.Add(new RowStyle());
            _parametersTableLayout.RowStyles.Add(new RowStyle());
            _parametersTableLayout.RowStyles.Add(new RowStyle());
            _parametersTableLayout.RowStyles.Add(new RowStyle());
            _parametersTableLayout.RowStyles.Add(new RowStyle());
            _parametersTableLayout.RowStyles.Add(new RowStyle());
            _parametersTableLayout.RowStyles.Add(new RowStyle());
            _parametersTableLayout.Size = new Size(567, 170);
            _parametersTableLayout.TabIndex = 0;
            //
            // _autoCheckCheckBox
            //
            _autoCheckCheckBox.Anchor = AnchorStyles.Left;
            _autoCheckCheckBox.AutoSize = true;
            _parametersTableLayout.SetColumnSpan(_autoCheckCheckBox, 2);
            _autoCheckCheckBox.Name = "_autoCheckCheckBox";
            _autoCheckCheckBox.TabIndex = 0;
            _autoCheckCheckBox.Text = "Проверять обновления автоматически";
            _autoCheckCheckBox.UseVisualStyleBackColor = true;
            _autoCheckCheckBox.CheckedChanged += OnSettingChanged;
            //
            // _intervalLabel
            //
            _intervalLabel.Anchor = AnchorStyles.Left;
            _intervalLabel.AutoSize = true;
            _intervalLabel.Name = "_intervalLabel";
            _intervalLabel.TabIndex = 1;
            _intervalLabel.Text = "Интервал проверки (часов):";
            //
            // _intervalNumeric
            //
            _intervalNumeric.Anchor = AnchorStyles.Left;
            _intervalNumeric.Maximum = new decimal(new int[] { 168, 0, 0, 0 });
            _intervalNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            _intervalNumeric.Name = "_intervalNumeric";
            _intervalNumeric.TabIndex = 2;
            _intervalNumeric.Value = new decimal(new int[] { 6, 0, 0, 0 });
            _intervalNumeric.ValueChanged += OnSettingChanged;
            //
            // _autoInstallCheckBox
            //
            _autoInstallCheckBox.Anchor = AnchorStyles.Left;
            _autoInstallCheckBox.AutoSize = true;
            _parametersTableLayout.SetColumnSpan(_autoInstallCheckBox, 2);
            _autoInstallCheckBox.Name = "_autoInstallCheckBox";
            _autoInstallCheckBox.TabIndex = 3;
            _autoInstallCheckBox.Text = "Устанавливать обновление автоматически при выходе из приложения";
            _autoInstallCheckBox.UseVisualStyleBackColor = true;
            _autoInstallCheckBox.CheckedChanged += OnSettingChanged;
            //
            // _allowFrameworkDependentCheckBox
            //
            _allowFrameworkDependentCheckBox.Anchor = AnchorStyles.Left;
            _allowFrameworkDependentCheckBox.AutoSize = true;
            _parametersTableLayout.SetColumnSpan(_allowFrameworkDependentCheckBox, 2);
            _allowFrameworkDependentCheckBox.Name = "_allowFrameworkDependentCheckBox";
            _allowFrameworkDependentCheckBox.TabIndex = 4;
            _allowFrameworkDependentCheckBox.Text = "Разрешить автообновление для сборки, требующей .NET (framework-dependent)";
            _allowFrameworkDependentCheckBox.UseVisualStyleBackColor = true;
            _allowFrameworkDependentCheckBox.Visible = false;
            _allowFrameworkDependentCheckBox.CheckedChanged += OnAllowFrameworkDependentChanged;
            //
            // _repositoryLabel
            //
            _repositoryLabel.Anchor = AnchorStyles.Left;
            _repositoryLabel.AutoSize = true;
            _parametersTableLayout.SetColumnSpan(_repositoryLabel, 2);
            _repositoryLabel.Margin = new Padding(3, 8, 3, 0);
            _repositoryLabel.Name = "_repositoryLabel";
            _repositoryLabel.TabIndex = 5;
            _repositoryLabel.Text = "Репозиторий обновлений (owner/repo):";
            //
            // _repositoryTextBox
            //
            _repositoryTextBox.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            _parametersTableLayout.SetColumnSpan(_repositoryTextBox, 2);
            _repositoryTextBox.Name = "_repositoryTextBox";
            _repositoryTextBox.TabIndex = 6;
            _repositoryTextBox.TextChanged += OnRepositoryTextChanged;
            //
            // _repositoryHintLabel
            //
            _repositoryHintLabel.Anchor = AnchorStyles.Left;
            _repositoryHintLabel.AutoSize = true;
            _parametersTableLayout.SetColumnSpan(_repositoryHintLabel, 2);
            _repositoryHintLabel.ForeColor = Color.Gray;
            _repositoryHintLabel.Name = "_repositoryHintLabel";
            _repositoryHintLabel.TabIndex = 7;
            //
            // _actionsGroupBox
            //
            _actionsGroupBox.AutoSize = true;
            _actionsGroupBox.Controls.Add(_actionsTableLayout);
            _actionsGroupBox.Dock = DockStyle.Fill;
            _actionsGroupBox.Location = new Point(3, 203);
            _actionsGroupBox.Name = "_actionsGroupBox";
            _actionsGroupBox.Padding = new Padding(10);
            _actionsGroupBox.Size = new Size(587, 75);
            _actionsGroupBox.TabIndex = 2;
            _actionsGroupBox.TabStop = false;
            _actionsGroupBox.Text = "Действия";
            //
            // _actionsTableLayout
            //
            _actionsTableLayout.AutoSize = true;
            _actionsTableLayout.ColumnCount = 3;
            _actionsTableLayout.ColumnStyles.Add(new ColumnStyle());
            _actionsTableLayout.ColumnStyles.Add(new ColumnStyle());
            _actionsTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _actionsTableLayout.Controls.Add(_checkNowButton, 0, 0);
            _actionsTableLayout.Controls.Add(_installButton, 1, 0);
            _actionsTableLayout.Dock = DockStyle.Fill;
            _actionsTableLayout.Location = new Point(10, 26);
            _actionsTableLayout.Name = "_actionsTableLayout";
            _actionsTableLayout.RowCount = 1;
            _actionsTableLayout.RowStyles.Add(new RowStyle());
            _actionsTableLayout.Size = new Size(567, 39);
            _actionsTableLayout.TabIndex = 0;
            //
            // _checkNowButton
            //
            _checkNowButton.AutoSize = true;
            _checkNowButton.MinimumSize = new Size(92, 0);
            _checkNowButton.Name = "_checkNowButton";
            _checkNowButton.TabIndex = 0;
            _checkNowButton.Text = "🔄 Проверить сейчас";
            _checkNowButton.UseVisualStyleBackColor = true;
            _checkNowButton.Click += OnCheckNowButtonClicked;
            //
            // _installButton
            //
            _installButton.AutoSize = true;
            _installButton.Enabled = false;
            _installButton.Margin = new Padding(6, 3, 3, 3);
            _installButton.MinimumSize = new Size(92, 0);
            _installButton.Name = "_installButton";
            _installButton.TabIndex = 1;
            _installButton.Text = "⬇️ Установить обновление";
            _installButton.UseVisualStyleBackColor = true;
            _installButton.Click += OnInstallButtonClicked;
            //
            // UpdateSettingsControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_mainTableLayout);
            Name = "UpdateSettingsControl";
            Size = new Size(593, 529);
            _mainTableLayout.ResumeLayout(false);
            _mainTableLayout.PerformLayout();
            _statusGroupBox.ResumeLayout(false);
            _statusGroupBox.PerformLayout();
            _statusTableLayout.ResumeLayout(false);
            _statusTableLayout.PerformLayout();
            _parametersGroupBox.ResumeLayout(false);
            _parametersGroupBox.PerformLayout();
            _parametersTableLayout.ResumeLayout(false);
            _parametersTableLayout.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)_intervalNumeric).EndInit();
            _actionsGroupBox.ResumeLayout(false);
            _actionsGroupBox.PerformLayout();
            _actionsTableLayout.ResumeLayout(false);
            _actionsTableLayout.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private TableLayoutPanel _mainTableLayout;
        private GroupBox _statusGroupBox;
        private TableLayoutPanel _statusTableLayout;
        private Label _currentVersionLabel;
        private Label _statusLabel;
        private GroupBox _parametersGroupBox;
        private TableLayoutPanel _parametersTableLayout;
        private CheckBox _autoCheckCheckBox;
        private Label _intervalLabel;
        private NumericUpDown _intervalNumeric;
        private CheckBox _autoInstallCheckBox;
        private CheckBox _allowFrameworkDependentCheckBox;
        private Label _repositoryLabel;
        private TextBox _repositoryTextBox;
        private Label _repositoryHintLabel;
        private GroupBox _actionsGroupBox;
        private TableLayoutPanel _actionsTableLayout;
        private Button _checkNowButton;
        private Button _installButton;
    }
}
