namespace PoproshaykaBot.WinForms.Forms.Settings;

partial class ObsIntegrationSettingsControl
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

    #region Windows Form Designer generated code

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _rootLayout = new TableLayoutPanel();
        _enabledCheckBox = new CheckBox();
        _subTabControl = new TabControl();
        _connectionTabPage = new TabPage();
        _connectionLayout = new TableLayoutPanel();
        _autoConnectCheckBox = new CheckBox();
        _hostLabel = new Label();
        _hostTextBox = new TextBox();
        _portLabel = new Label();
        _portNumeric = new NumericUpDown();
        _passwordLabel = new Label();
        _passwordTextBox = new TextBox();
        _connectionButtonPanel = new FlowLayoutPanel();
        _testConnectionButton = new Button();
        _reconnectButton = new Button();
        _loadScenesButton = new Button();
        _browserSourceTabPage = new TabPage();
        _browserSourceLayout = new TableLayoutPanel();
        _autoProvisionCheckBox = new CheckBox();
        _sceneLabel = new Label();
        _sceneComboBox = new ComboBox();
        _sourceNameLabel = new Label();
        _sourceNameTextBox = new TextBox();
        _sizeLabel = new Label();
        _sizeFlowPanel = new FlowLayoutPanel();
        _widthNumeric = new NumericUpDown();
        _sizeSeparatorLabel = new Label();
        _heightNumeric = new NumericUpDown();
        _overlayUrlLabel = new Label();
        _overlayUrlTextBox = new TextBox();
        _browserSourceButtonPanel = new FlowLayoutPanel();
        _provisionButton = new Button();
        _copyUrlButton = new Button();
        _chatRefreshTabPage = new TabPage();
        _chatRefreshLayout = new TableLayoutPanel();
        _refreshOnStreamStartCheckBox = new CheckBox();
        _chatRefreshSourcesLabel = new Label();
        _chatRefreshSourcesCheckedListBox = new CheckedListBox();
        _chatRefreshButtonPanel = new FlowLayoutPanel();
        _refreshChatNowButton = new Button();
        _dashboardWidgetTabPage = new TabPage();
        _dashboardWidgetLayout = new TableLayoutPanel();
        _volumeMeterDelayLabel = new Label();
        _volumeMeterDelayNumeric = new NumericUpDown();
        _sourcesLabel = new Label();
        _sourcesCheckedListBox = new CheckedListBox();
        _syncTabPage = new TabPage();
        _syncLayout = new TableLayoutPanel();
        _syncSceneOnProfileCheckBox = new CheckBox();
        _syncProfileOnSceneCheckBox = new CheckBox();
        _statusLabel = new Label();
        _hintToolTip = new ToolTip(components);
        _rootLayout.SuspendLayout();
        _subTabControl.SuspendLayout();
        _connectionTabPage.SuspendLayout();
        _connectionLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_portNumeric).BeginInit();
        _connectionButtonPanel.SuspendLayout();
        _browserSourceTabPage.SuspendLayout();
        _browserSourceLayout.SuspendLayout();
        _sizeFlowPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_widthNumeric).BeginInit();
        ((System.ComponentModel.ISupportInitialize)_heightNumeric).BeginInit();
        _browserSourceButtonPanel.SuspendLayout();
        _chatRefreshTabPage.SuspendLayout();
        _chatRefreshLayout.SuspendLayout();
        _chatRefreshButtonPanel.SuspendLayout();
        _dashboardWidgetTabPage.SuspendLayout();
        _dashboardWidgetLayout.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)_volumeMeterDelayNumeric).BeginInit();
        _syncTabPage.SuspendLayout();
        _syncLayout.SuspendLayout();
        SuspendLayout();
        //
        // _rootLayout
        //
        _rootLayout.ColumnCount = 1;
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _rootLayout.Controls.Add(_enabledCheckBox, 0, 0);
        _rootLayout.Controls.Add(_subTabControl, 0, 1);
        _rootLayout.Controls.Add(_statusLabel, 0, 2);
        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.Location = new Point(0, 0);
        _rootLayout.Name = "_rootLayout";
        _rootLayout.Padding = new Padding(4, 4, 4, 4);
        _rootLayout.RowCount = 3;
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
        _rootLayout.Size = new Size(600, 560);
        _rootLayout.TabIndex = 0;
        //
        // _enabledCheckBox
        //
        _enabledCheckBox.AutoSize = true;
        _enabledCheckBox.Dock = DockStyle.Fill;
        _enabledCheckBox.Location = new Point(7, 7);
        _enabledCheckBox.Name = "_enabledCheckBox";
        _enabledCheckBox.Size = new Size(586, 22);
        _enabledCheckBox.TabIndex = 0;
        _enabledCheckBox.Text = "Включить интеграцию с OBS";
        _enabledCheckBox.UseVisualStyleBackColor = true;
        _enabledCheckBox.CheckedChanged += OnSettingChanged;
        //
        // _subTabControl
        //
        _subTabControl.Controls.Add(_connectionTabPage);
        _subTabControl.Controls.Add(_browserSourceTabPage);
        _subTabControl.Controls.Add(_chatRefreshTabPage);
        _subTabControl.Controls.Add(_dashboardWidgetTabPage);
        _subTabControl.Controls.Add(_syncTabPage);
        _subTabControl.Dock = DockStyle.Fill;
        _subTabControl.Location = new Point(7, 35);
        _subTabControl.Name = "_subTabControl";
        _subTabControl.Padding = new Point(0, 0);
        _subTabControl.SelectedIndex = 0;
        _subTabControl.Size = new Size(586, 462);
        _subTabControl.TabIndex = 1;
        //
        // _connectionTabPage
        //
        _connectionTabPage.Controls.Add(_connectionLayout);
        _connectionTabPage.Location = new Point(4, 24);
        _connectionTabPage.Name = "_connectionTabPage";
        _connectionTabPage.Padding = new Padding(8, 8, 8, 8);
        _connectionTabPage.Size = new Size(578, 434);
        _connectionTabPage.TabIndex = 0;
        _connectionTabPage.Text = "Подключение";
        _connectionTabPage.UseVisualStyleBackColor = true;
        //
        // _connectionLayout
        //
        _connectionLayout.ColumnCount = 2;
        _connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        _connectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _connectionLayout.Controls.Add(_autoConnectCheckBox, 0, 0);
        _connectionLayout.Controls.Add(_hostLabel, 0, 1);
        _connectionLayout.Controls.Add(_hostTextBox, 1, 1);
        _connectionLayout.Controls.Add(_portLabel, 0, 2);
        _connectionLayout.Controls.Add(_portNumeric, 1, 2);
        _connectionLayout.Controls.Add(_passwordLabel, 0, 3);
        _connectionLayout.Controls.Add(_passwordTextBox, 1, 3);
        _connectionLayout.Controls.Add(_connectionButtonPanel, 0, 5);
        _connectionLayout.Dock = DockStyle.Fill;
        _connectionLayout.Location = new Point(8, 8);
        _connectionLayout.Name = "_connectionLayout";
        _connectionLayout.RowCount = 6;
        _connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        _connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _connectionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _connectionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        _connectionLayout.Size = new Size(562, 418);
        _connectionLayout.TabIndex = 0;
        //
        // _autoConnectCheckBox
        //
        _autoConnectCheckBox.AutoSize = true;
        _connectionLayout.SetColumnSpan(_autoConnectCheckBox, 2);
        _autoConnectCheckBox.Dock = DockStyle.Fill;
        _autoConnectCheckBox.Location = new Point(3, 3);
        _autoConnectCheckBox.Name = "_autoConnectCheckBox";
        _autoConnectCheckBox.Size = new Size(556, 22);
        _autoConnectCheckBox.TabIndex = 0;
        _autoConnectCheckBox.Text = "Подключаться к OBS при запуске приложения";
        _autoConnectCheckBox.UseVisualStyleBackColor = true;
        _autoConnectCheckBox.CheckedChanged += OnSettingChanged;
        //
        // _hostLabel
        //
        _hostLabel.AutoSize = true;
        _hostLabel.Dock = DockStyle.Fill;
        _hostLabel.Location = new Point(0, 28);
        _hostLabel.Margin = new Padding(0, 0, 6, 4);
        _hostLabel.Name = "_hostLabel";
        _hostLabel.Size = new Size(144, 28);
        _hostLabel.TabIndex = 1;
        _hostLabel.Text = "Хост:";
        _hostLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _hostTextBox
        //
        _hostTextBox.Dock = DockStyle.Fill;
        _hostTextBox.Location = new Point(150, 31);
        _hostTextBox.Margin = new Padding(0, 3, 0, 4);
        _hostTextBox.Name = "_hostTextBox";
        _hostTextBox.PlaceholderText = "127.0.0.1";
        _hostTextBox.Size = new Size(412, 23);
        _hostTextBox.TabIndex = 2;
        _hostTextBox.TextChanged += OnConnectionFieldChanged;
        //
        // _portLabel
        //
        _portLabel.AutoSize = true;
        _portLabel.Dock = DockStyle.Fill;
        _portLabel.Location = new Point(0, 60);
        _portLabel.Margin = new Padding(0, 0, 6, 4);
        _portLabel.Name = "_portLabel";
        _portLabel.Size = new Size(144, 28);
        _portLabel.TabIndex = 3;
        _portLabel.Text = "Порт:";
        _portLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _portNumeric
        //
        _portNumeric.Dock = DockStyle.Left;
        _portNumeric.Location = new Point(150, 63);
        _portNumeric.Margin = new Padding(0, 3, 0, 4);
        _portNumeric.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
        _portNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _portNumeric.Name = "_portNumeric";
        _portNumeric.Size = new Size(100, 23);
        _portNumeric.TabIndex = 4;
        _portNumeric.Value = new decimal(new int[] { 4455, 0, 0, 0 });
        _portNumeric.ValueChanged += OnConnectionFieldChanged;
        //
        // _passwordLabel
        //
        _passwordLabel.AutoSize = true;
        _passwordLabel.Dock = DockStyle.Fill;
        _passwordLabel.Location = new Point(0, 92);
        _passwordLabel.Margin = new Padding(0, 0, 6, 4);
        _passwordLabel.Name = "_passwordLabel";
        _passwordLabel.Size = new Size(144, 28);
        _passwordLabel.TabIndex = 5;
        _passwordLabel.Text = "Пароль:";
        _passwordLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _passwordTextBox
        //
        _passwordTextBox.Dock = DockStyle.Fill;
        _passwordTextBox.Location = new Point(150, 95);
        _passwordTextBox.Margin = new Padding(0, 3, 0, 4);
        _passwordTextBox.Name = "_passwordTextBox";
        _passwordTextBox.Size = new Size(412, 23);
        _passwordTextBox.TabIndex = 6;
        _passwordTextBox.UseSystemPasswordChar = true;
        _passwordTextBox.TextChanged += OnConnectionFieldChanged;
        //
        // _connectionButtonPanel
        //
        _connectionLayout.SetColumnSpan(_connectionButtonPanel, 2);
        _connectionButtonPanel.Controls.Add(_testConnectionButton);
        _connectionButtonPanel.Controls.Add(_reconnectButton);
        _connectionButtonPanel.Controls.Add(_loadScenesButton);
        _connectionButtonPanel.Dock = DockStyle.Fill;
        _connectionButtonPanel.Location = new Point(0, 384);
        _connectionButtonPanel.Margin = new Padding(0, 0, 0, 0);
        _connectionButtonPanel.Name = "_connectionButtonPanel";
        _connectionButtonPanel.Size = new Size(562, 34);
        _connectionButtonPanel.TabIndex = 7;
        //
        // _testConnectionButton
        //
        _testConnectionButton.Location = new Point(0, 0);
        _testConnectionButton.Margin = new Padding(0, 0, 3, 0);
        _testConnectionButton.Name = "_testConnectionButton";
        _testConnectionButton.Size = new Size(102, 27);
        _testConnectionButton.TabIndex = 0;
        _testConnectionButton.Text = "Проверить";
        _testConnectionButton.UseVisualStyleBackColor = true;
        _testConnectionButton.Click += OnTestConnectionButtonClicked;
        //
        // _reconnectButton
        //
        _reconnectButton.Location = new Point(105, 0);
        _reconnectButton.Margin = new Padding(0, 0, 3, 0);
        _reconnectButton.Name = "_reconnectButton";
        _reconnectButton.Size = new Size(125, 27);
        _reconnectButton.TabIndex = 1;
        _reconnectButton.Text = "Переподключить";
        _reconnectButton.UseVisualStyleBackColor = true;
        _reconnectButton.Click += OnReconnectButtonClicked;
        //
        // _loadScenesButton
        //
        _loadScenesButton.Location = new Point(233, 0);
        _loadScenesButton.Margin = new Padding(0, 0, 3, 0);
        _loadScenesButton.Name = "_loadScenesButton";
        _loadScenesButton.Size = new Size(105, 27);
        _loadScenesButton.TabIndex = 2;
        _loadScenesButton.Text = "Списки";
        _loadScenesButton.UseVisualStyleBackColor = true;
        _loadScenesButton.Click += OnLoadScenesButtonClicked;
        //
        // _browserSourceTabPage
        //
        _browserSourceTabPage.Controls.Add(_browserSourceLayout);
        _browserSourceTabPage.Location = new Point(4, 24);
        _browserSourceTabPage.Name = "_browserSourceTabPage";
        _browserSourceTabPage.Padding = new Padding(8, 8, 8, 8);
        _browserSourceTabPage.Size = new Size(578, 434);
        _browserSourceTabPage.TabIndex = 1;
        _browserSourceTabPage.Text = "Browser Source";
        _browserSourceTabPage.UseVisualStyleBackColor = true;
        //
        // _browserSourceLayout
        //
        _browserSourceLayout.ColumnCount = 2;
        _browserSourceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        _browserSourceLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _browserSourceLayout.Controls.Add(_autoProvisionCheckBox, 0, 0);
        _browserSourceLayout.Controls.Add(_sceneLabel, 0, 1);
        _browserSourceLayout.Controls.Add(_sceneComboBox, 1, 1);
        _browserSourceLayout.Controls.Add(_sourceNameLabel, 0, 2);
        _browserSourceLayout.Controls.Add(_sourceNameTextBox, 1, 2);
        _browserSourceLayout.Controls.Add(_sizeLabel, 0, 3);
        _browserSourceLayout.Controls.Add(_sizeFlowPanel, 1, 3);
        _browserSourceLayout.Controls.Add(_overlayUrlLabel, 0, 4);
        _browserSourceLayout.Controls.Add(_overlayUrlTextBox, 1, 4);
        _browserSourceLayout.Controls.Add(_browserSourceButtonPanel, 0, 6);
        _browserSourceLayout.Dock = DockStyle.Fill;
        _browserSourceLayout.Location = new Point(8, 8);
        _browserSourceLayout.Name = "_browserSourceLayout";
        _browserSourceLayout.RowCount = 7;
        _browserSourceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        _browserSourceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _browserSourceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _browserSourceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _browserSourceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _browserSourceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _browserSourceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        _browserSourceLayout.Size = new Size(562, 418);
        _browserSourceLayout.TabIndex = 0;
        //
        // _autoProvisionCheckBox
        //
        _autoProvisionCheckBox.AutoSize = true;
        _browserSourceLayout.SetColumnSpan(_autoProvisionCheckBox, 2);
        _autoProvisionCheckBox.Dock = DockStyle.Fill;
        _autoProvisionCheckBox.Location = new Point(3, 3);
        _autoProvisionCheckBox.Name = "_autoProvisionCheckBox";
        _autoProvisionCheckBox.Size = new Size(556, 22);
        _autoProvisionCheckBox.TabIndex = 0;
        _autoProvisionCheckBox.Text = "Автоматически создавать или обновлять Browser Source";
        _autoProvisionCheckBox.UseVisualStyleBackColor = true;
        _autoProvisionCheckBox.CheckedChanged += OnSettingChanged;
        //
        // _sceneLabel
        //
        _sceneLabel.AutoSize = true;
        _sceneLabel.Dock = DockStyle.Fill;
        _sceneLabel.Location = new Point(0, 28);
        _sceneLabel.Margin = new Padding(0, 0, 6, 4);
        _sceneLabel.Name = "_sceneLabel";
        _sceneLabel.Size = new Size(144, 28);
        _sceneLabel.TabIndex = 1;
        _sceneLabel.Text = "Сцена:";
        _sceneLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _sceneComboBox
        //
        _sceneComboBox.Dock = DockStyle.Fill;
        _sceneComboBox.FormattingEnabled = true;
        _sceneComboBox.Location = new Point(150, 31);
        _sceneComboBox.Margin = new Padding(0, 3, 0, 4);
        _sceneComboBox.Name = "_sceneComboBox";
        _sceneComboBox.Size = new Size(412, 23);
        _sceneComboBox.TabIndex = 2;
        _sceneComboBox.SelectedIndexChanged += OnSettingChanged;
        _sceneComboBox.TextChanged += OnSettingChanged;
        //
        // _sourceNameLabel
        //
        _sourceNameLabel.AutoSize = true;
        _sourceNameLabel.Dock = DockStyle.Fill;
        _sourceNameLabel.Location = new Point(0, 60);
        _sourceNameLabel.Margin = new Padding(0, 0, 6, 4);
        _sourceNameLabel.Name = "_sourceNameLabel";
        _sourceNameLabel.Size = new Size(144, 28);
        _sourceNameLabel.TabIndex = 3;
        _sourceNameLabel.Text = "Источник:";
        _sourceNameLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _sourceNameTextBox
        //
        _sourceNameTextBox.Dock = DockStyle.Fill;
        _sourceNameTextBox.Location = new Point(150, 63);
        _sourceNameTextBox.Margin = new Padding(0, 3, 0, 4);
        _sourceNameTextBox.Name = "_sourceNameTextBox";
        _sourceNameTextBox.PlaceholderText = "PoproshaykaBot Chat";
        _sourceNameTextBox.Size = new Size(412, 23);
        _sourceNameTextBox.TabIndex = 4;
        _sourceNameTextBox.TextChanged += OnSettingChanged;
        //
        // _sizeLabel
        //
        _sizeLabel.AutoSize = true;
        _sizeLabel.Dock = DockStyle.Fill;
        _sizeLabel.Location = new Point(0, 92);
        _sizeLabel.Margin = new Padding(0, 0, 6, 4);
        _sizeLabel.Name = "_sizeLabel";
        _sizeLabel.Size = new Size(144, 28);
        _sizeLabel.TabIndex = 5;
        _sizeLabel.Text = "Размер:";
        _sizeLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _sizeFlowPanel
        //
        _sizeFlowPanel.Controls.Add(_widthNumeric);
        _sizeFlowPanel.Controls.Add(_sizeSeparatorLabel);
        _sizeFlowPanel.Controls.Add(_heightNumeric);
        _sizeFlowPanel.Dock = DockStyle.Fill;
        _sizeFlowPanel.Location = new Point(150, 92);
        _sizeFlowPanel.Margin = new Padding(0, 0, 0, 4);
        _sizeFlowPanel.Name = "_sizeFlowPanel";
        _sizeFlowPanel.Size = new Size(412, 28);
        _sizeFlowPanel.TabIndex = 6;
        //
        // _widthNumeric
        //
        _widthNumeric.Location = new Point(0, 3);
        _widthNumeric.Margin = new Padding(0, 3, 4, 0);
        _widthNumeric.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
        _widthNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _widthNumeric.Name = "_widthNumeric";
        _widthNumeric.Size = new Size(90, 23);
        _widthNumeric.TabIndex = 0;
        _widthNumeric.Value = new decimal(new int[] { 1920, 0, 0, 0 });
        _widthNumeric.ValueChanged += OnSettingChanged;
        //
        // _sizeSeparatorLabel
        //
        _sizeSeparatorLabel.AutoSize = true;
        _sizeSeparatorLabel.Location = new Point(94, 6);
        _sizeSeparatorLabel.Margin = new Padding(0, 6, 4, 0);
        _sizeSeparatorLabel.Name = "_sizeSeparatorLabel";
        _sizeSeparatorLabel.Size = new Size(12, 15);
        _sizeSeparatorLabel.TabIndex = 1;
        _sizeSeparatorLabel.Text = "×";
        //
        // _heightNumeric
        //
        _heightNumeric.Location = new Point(110, 3);
        _heightNumeric.Margin = new Padding(0, 3, 0, 0);
        _heightNumeric.Maximum = new decimal(new int[] { 10000, 0, 0, 0 });
        _heightNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _heightNumeric.Name = "_heightNumeric";
        _heightNumeric.Size = new Size(90, 23);
        _heightNumeric.TabIndex = 2;
        _heightNumeric.Value = new decimal(new int[] { 1080, 0, 0, 0 });
        _heightNumeric.ValueChanged += OnSettingChanged;
        //
        // _overlayUrlLabel
        //
        _overlayUrlLabel.AutoSize = true;
        _overlayUrlLabel.Dock = DockStyle.Fill;
        _overlayUrlLabel.Location = new Point(0, 124);
        _overlayUrlLabel.Margin = new Padding(0, 0, 6, 4);
        _overlayUrlLabel.Name = "_overlayUrlLabel";
        _overlayUrlLabel.Size = new Size(144, 28);
        _overlayUrlLabel.TabIndex = 7;
        _overlayUrlLabel.Text = "URL оверлея:";
        _overlayUrlLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _overlayUrlTextBox
        //
        _overlayUrlTextBox.Dock = DockStyle.Fill;
        _overlayUrlTextBox.Location = new Point(150, 127);
        _overlayUrlTextBox.Margin = new Padding(0, 3, 0, 4);
        _overlayUrlTextBox.Name = "_overlayUrlTextBox";
        _overlayUrlTextBox.ReadOnly = true;
        _overlayUrlTextBox.Size = new Size(412, 23);
        _overlayUrlTextBox.TabIndex = 8;
        //
        // _browserSourceButtonPanel
        //
        _browserSourceLayout.SetColumnSpan(_browserSourceButtonPanel, 2);
        _browserSourceButtonPanel.Controls.Add(_provisionButton);
        _browserSourceButtonPanel.Controls.Add(_copyUrlButton);
        _browserSourceButtonPanel.Dock = DockStyle.Fill;
        _browserSourceButtonPanel.Location = new Point(0, 384);
        _browserSourceButtonPanel.Margin = new Padding(0, 0, 0, 0);
        _browserSourceButtonPanel.Name = "_browserSourceButtonPanel";
        _browserSourceButtonPanel.Size = new Size(562, 34);
        _browserSourceButtonPanel.TabIndex = 9;
        //
        // _provisionButton
        //
        _provisionButton.Location = new Point(0, 0);
        _provisionButton.Margin = new Padding(0, 0, 3, 0);
        _provisionButton.Name = "_provisionButton";
        _provisionButton.Size = new Size(130, 27);
        _provisionButton.TabIndex = 0;
        _provisionButton.Text = "Создать источник";
        _provisionButton.UseVisualStyleBackColor = true;
        _provisionButton.Click += OnProvisionButtonClicked;
        //
        // _copyUrlButton
        //
        _copyUrlButton.Location = new Point(133, 0);
        _copyUrlButton.Margin = new Padding(0, 0, 3, 0);
        _copyUrlButton.Name = "_copyUrlButton";
        _copyUrlButton.Size = new Size(110, 27);
        _copyUrlButton.TabIndex = 1;
        _copyUrlButton.Text = "Копировать URL";
        _copyUrlButton.UseVisualStyleBackColor = true;
        _copyUrlButton.Click += OnCopyUrlButtonClicked;
        //
        // _chatRefreshTabPage
        //
        _chatRefreshTabPage.Controls.Add(_chatRefreshLayout);
        _chatRefreshTabPage.Location = new Point(4, 24);
        _chatRefreshTabPage.Name = "_chatRefreshTabPage";
        _chatRefreshTabPage.Padding = new Padding(8, 8, 8, 8);
        _chatRefreshTabPage.Size = new Size(578, 434);
        _chatRefreshTabPage.TabIndex = 2;
        _chatRefreshTabPage.Text = "Чат-источники";
        _chatRefreshTabPage.UseVisualStyleBackColor = true;
        //
        // _chatRefreshLayout
        //
        _chatRefreshLayout.ColumnCount = 2;
        _chatRefreshLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        _chatRefreshLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _chatRefreshLayout.Controls.Add(_refreshOnStreamStartCheckBox, 0, 0);
        _chatRefreshLayout.Controls.Add(_chatRefreshSourcesLabel, 0, 1);
        _chatRefreshLayout.Controls.Add(_chatRefreshSourcesCheckedListBox, 1, 1);
        _chatRefreshLayout.Controls.Add(_chatRefreshButtonPanel, 0, 2);
        _chatRefreshLayout.Dock = DockStyle.Fill;
        _chatRefreshLayout.Location = new Point(8, 8);
        _chatRefreshLayout.Name = "_chatRefreshLayout";
        _chatRefreshLayout.RowCount = 3;
        _chatRefreshLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        _chatRefreshLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _chatRefreshLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        _chatRefreshLayout.Size = new Size(562, 418);
        _chatRefreshLayout.TabIndex = 0;
        //
        // _refreshOnStreamStartCheckBox
        //
        _refreshOnStreamStartCheckBox.AutoSize = true;
        _chatRefreshLayout.SetColumnSpan(_refreshOnStreamStartCheckBox, 2);
        _refreshOnStreamStartCheckBox.Dock = DockStyle.Fill;
        _refreshOnStreamStartCheckBox.Location = new Point(3, 3);
        _refreshOnStreamStartCheckBox.Name = "_refreshOnStreamStartCheckBox";
        _refreshOnStreamStartCheckBox.Size = new Size(556, 22);
        _refreshOnStreamStartCheckBox.TabIndex = 0;
        _refreshOnStreamStartCheckBox.Text = "Жёстко обновлять чат-источники при старте эфира в OBS";
        _refreshOnStreamStartCheckBox.UseVisualStyleBackColor = true;
        _refreshOnStreamStartCheckBox.CheckedChanged += OnSettingChanged;
        //
        // _chatRefreshSourcesLabel
        //
        _chatRefreshSourcesLabel.AutoSize = true;
        _chatRefreshSourcesLabel.Dock = DockStyle.Fill;
        _chatRefreshSourcesLabel.Location = new Point(0, 28);
        _chatRefreshSourcesLabel.Margin = new Padding(0, 0, 6, 4);
        _chatRefreshSourcesLabel.Name = "_chatRefreshSourcesLabel";
        _chatRefreshSourcesLabel.Size = new Size(144, 318);
        _chatRefreshSourcesLabel.TabIndex = 1;
        _chatRefreshSourcesLabel.Text = "Чат-источники для refresh:";
        _chatRefreshSourcesLabel.TextAlign = ContentAlignment.TopLeft;
        //
        // _chatRefreshSourcesCheckedListBox
        //
        _chatRefreshSourcesCheckedListBox.CheckOnClick = true;
        _chatRefreshSourcesCheckedListBox.Dock = DockStyle.Fill;
        _chatRefreshSourcesCheckedListBox.FormattingEnabled = true;
        _chatRefreshSourcesCheckedListBox.IntegralHeight = false;
        _chatRefreshSourcesCheckedListBox.Location = new Point(150, 31);
        _chatRefreshSourcesCheckedListBox.Margin = new Padding(0, 3, 0, 4);
        _chatRefreshSourcesCheckedListBox.Name = "_chatRefreshSourcesCheckedListBox";
        _chatRefreshSourcesCheckedListBox.Size = new Size(412, 312);
        _chatRefreshSourcesCheckedListBox.TabIndex = 2;
        _chatRefreshSourcesCheckedListBox.ItemCheck += OnSettingChanged;
        //
        // _chatRefreshButtonPanel
        //
        _chatRefreshLayout.SetColumnSpan(_chatRefreshButtonPanel, 2);
        _chatRefreshButtonPanel.Controls.Add(_refreshChatNowButton);
        _chatRefreshButtonPanel.Dock = DockStyle.Fill;
        _chatRefreshButtonPanel.Location = new Point(0, 384);
        _chatRefreshButtonPanel.Margin = new Padding(0, 0, 0, 0);
        _chatRefreshButtonPanel.Name = "_chatRefreshButtonPanel";
        _chatRefreshButtonPanel.Size = new Size(562, 34);
        _chatRefreshButtonPanel.TabIndex = 3;
        //
        // _refreshChatNowButton
        //
        _refreshChatNowButton.Location = new Point(0, 0);
        _refreshChatNowButton.Margin = new Padding(0, 0, 3, 0);
        _refreshChatNowButton.Name = "_refreshChatNowButton";
        _refreshChatNowButton.Size = new Size(140, 27);
        _refreshChatNowButton.TabIndex = 0;
        _refreshChatNowButton.Text = "Обновить чат сейчас";
        _refreshChatNowButton.UseVisualStyleBackColor = true;
        _refreshChatNowButton.Click += OnRefreshChatNowButtonClicked;
        //
        // _dashboardWidgetTabPage
        //
        _dashboardWidgetTabPage.Controls.Add(_dashboardWidgetLayout);
        _dashboardWidgetTabPage.Location = new Point(4, 24);
        _dashboardWidgetTabPage.Name = "_dashboardWidgetTabPage";
        _dashboardWidgetTabPage.Padding = new Padding(8, 8, 8, 8);
        _dashboardWidgetTabPage.Size = new Size(578, 434);
        _dashboardWidgetTabPage.TabIndex = 3;
        _dashboardWidgetTabPage.Text = "Дашборд";
        _dashboardWidgetTabPage.UseVisualStyleBackColor = true;
        //
        // _dashboardWidgetLayout
        //
        _dashboardWidgetLayout.ColumnCount = 2;
        _dashboardWidgetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        _dashboardWidgetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _dashboardWidgetLayout.Controls.Add(_volumeMeterDelayLabel, 0, 0);
        _dashboardWidgetLayout.Controls.Add(_volumeMeterDelayNumeric, 1, 0);
        _dashboardWidgetLayout.Controls.Add(_sourcesLabel, 0, 1);
        _dashboardWidgetLayout.Controls.Add(_sourcesCheckedListBox, 1, 1);
        _dashboardWidgetLayout.Dock = DockStyle.Fill;
        _dashboardWidgetLayout.Location = new Point(8, 8);
        _dashboardWidgetLayout.Name = "_dashboardWidgetLayout";
        _dashboardWidgetLayout.RowCount = 2;
        _dashboardWidgetLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
        _dashboardWidgetLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _dashboardWidgetLayout.Size = new Size(562, 418);
        _dashboardWidgetLayout.TabIndex = 0;
        //
        // _volumeMeterDelayLabel
        //
        _volumeMeterDelayLabel.AutoSize = true;
        _volumeMeterDelayLabel.Dock = DockStyle.Fill;
        _volumeMeterDelayLabel.Location = new Point(0, 0);
        _volumeMeterDelayLabel.Margin = new Padding(0, 0, 6, 4);
        _volumeMeterDelayLabel.Name = "_volumeMeterDelayLabel";
        _volumeMeterDelayLabel.Size = new Size(144, 28);
        _volumeMeterDelayLabel.TabIndex = 0;
        _volumeMeterDelayLabel.Text = "Задержка шкалы, мс:";
        _volumeMeterDelayLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _volumeMeterDelayNumeric
        //
        _volumeMeterDelayNumeric.Dock = DockStyle.Left;
        _volumeMeterDelayNumeric.Increment = new decimal(new int[] { 10, 0, 0, 0 });
        _volumeMeterDelayNumeric.Location = new Point(150, 3);
        _volumeMeterDelayNumeric.Margin = new Padding(0, 3, 0, 4);
        _volumeMeterDelayNumeric.Maximum = new decimal(new int[] { 1000, 0, 0, 0 });
        _volumeMeterDelayNumeric.Minimum = new decimal(new int[] { 30, 0, 0, 0 });
        _volumeMeterDelayNumeric.Name = "_volumeMeterDelayNumeric";
        _volumeMeterDelayNumeric.Size = new Size(100, 23);
        _volumeMeterDelayNumeric.TabIndex = 1;
        _volumeMeterDelayNumeric.Value = new decimal(new int[] { 120, 0, 0, 0 });
        _volumeMeterDelayNumeric.ValueChanged += OnSettingChanged;
        //
        // _sourcesLabel
        //
        _sourcesLabel.AutoSize = true;
        _sourcesLabel.Dock = DockStyle.Fill;
        _sourcesLabel.Location = new Point(0, 32);
        _sourcesLabel.Margin = new Padding(0, 0, 6, 4);
        _sourcesLabel.Name = "_sourcesLabel";
        _sourcesLabel.Size = new Size(144, 382);
        _sourcesLabel.TabIndex = 2;
        _sourcesLabel.Text = "Источники виджета:";
        _sourcesLabel.TextAlign = ContentAlignment.TopLeft;
        //
        // _sourcesCheckedListBox
        //
        _sourcesCheckedListBox.CheckOnClick = true;
        _sourcesCheckedListBox.Dock = DockStyle.Fill;
        _sourcesCheckedListBox.FormattingEnabled = true;
        _sourcesCheckedListBox.IntegralHeight = false;
        _sourcesCheckedListBox.Location = new Point(150, 35);
        _sourcesCheckedListBox.Margin = new Padding(0, 3, 0, 4);
        _sourcesCheckedListBox.Name = "_sourcesCheckedListBox";
        _sourcesCheckedListBox.Size = new Size(412, 379);
        _sourcesCheckedListBox.TabIndex = 3;
        _sourcesCheckedListBox.ItemCheck += OnSettingChanged;
        //
        // _syncTabPage
        //
        _syncTabPage.Controls.Add(_syncLayout);
        _syncTabPage.Location = new Point(4, 24);
        _syncTabPage.Name = "_syncTabPage";
        _syncTabPage.Padding = new Padding(8, 8, 8, 8);
        _syncTabPage.Size = new Size(578, 434);
        _syncTabPage.TabIndex = 4;
        _syncTabPage.Text = "Синхронизация";
        _syncTabPage.UseVisualStyleBackColor = true;
        //
        // _syncLayout
        //
        _syncLayout.ColumnCount = 1;
        _syncLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _syncLayout.Controls.Add(_syncSceneOnProfileCheckBox, 0, 0);
        _syncLayout.Controls.Add(_syncProfileOnSceneCheckBox, 0, 1);
        _syncLayout.Dock = DockStyle.Fill;
        _syncLayout.Location = new Point(8, 8);
        _syncLayout.Name = "_syncLayout";
        _syncLayout.RowCount = 3;
        _syncLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        _syncLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        _syncLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _syncLayout.Size = new Size(562, 418);
        _syncLayout.TabIndex = 0;
        //
        // _syncSceneOnProfileCheckBox
        //
        _syncSceneOnProfileCheckBox.AutoSize = true;
        _syncSceneOnProfileCheckBox.Dock = DockStyle.Fill;
        _syncSceneOnProfileCheckBox.Location = new Point(3, 3);
        _syncSceneOnProfileCheckBox.Name = "_syncSceneOnProfileCheckBox";
        _syncSceneOnProfileCheckBox.Size = new Size(556, 22);
        _syncSceneOnProfileCheckBox.TabIndex = 0;
        _syncSceneOnProfileCheckBox.Text = "Переключать сцену OBS при смене профиля";
        _syncSceneOnProfileCheckBox.UseVisualStyleBackColor = true;
        _syncSceneOnProfileCheckBox.CheckedChanged += OnSettingChanged;
        //
        // _syncProfileOnSceneCheckBox
        //
        _syncProfileOnSceneCheckBox.AutoSize = true;
        _syncProfileOnSceneCheckBox.Dock = DockStyle.Fill;
        _syncProfileOnSceneCheckBox.Location = new Point(3, 31);
        _syncProfileOnSceneCheckBox.Name = "_syncProfileOnSceneCheckBox";
        _syncProfileOnSceneCheckBox.Size = new Size(556, 22);
        _syncProfileOnSceneCheckBox.TabIndex = 1;
        _syncProfileOnSceneCheckBox.Text = "Применять профиль при смене сцены OBS";
        _syncProfileOnSceneCheckBox.UseVisualStyleBackColor = true;
        _syncProfileOnSceneCheckBox.CheckedChanged += OnSettingChanged;
        //
        // _statusLabel
        //
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.ForeColor = Color.Gray;
        _statusLabel.Location = new Point(7, 527);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Size = new Size(586, 30);
        _statusLabel.TabIndex = 2;
        _statusLabel.Text = "● Не проверено";
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // _hintToolTip
        //
        _hintToolTip.SetToolTip(_sourcesCheckedListBox,
            "Отметьте аудио-источники OBS для виджета. Пусто = авто-определение микрофона.");
        _hintToolTip.SetToolTip(_chatRefreshSourcesCheckedListBox,
            "Выберите Browser Source-источники чата для жёсткого refresh. Пусто = использовать поле \"Источник\".");
        //
        // ObsIntegrationSettingsControl
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_rootLayout);
        Name = "ObsIntegrationSettingsControl";
        Size = new Size(600, 560);
        _rootLayout.ResumeLayout(false);
        _rootLayout.PerformLayout();
        _subTabControl.ResumeLayout(false);
        _connectionTabPage.ResumeLayout(false);
        _connectionLayout.ResumeLayout(false);
        _connectionLayout.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_portNumeric).EndInit();
        _connectionButtonPanel.ResumeLayout(false);
        _browserSourceTabPage.ResumeLayout(false);
        _browserSourceLayout.ResumeLayout(false);
        _browserSourceLayout.PerformLayout();
        _sizeFlowPanel.ResumeLayout(false);
        _sizeFlowPanel.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_widthNumeric).EndInit();
        ((System.ComponentModel.ISupportInitialize)_heightNumeric).EndInit();
        _browserSourceButtonPanel.ResumeLayout(false);
        _chatRefreshTabPage.ResumeLayout(false);
        _chatRefreshLayout.ResumeLayout(false);
        _chatRefreshLayout.PerformLayout();
        _chatRefreshButtonPanel.ResumeLayout(false);
        _dashboardWidgetTabPage.ResumeLayout(false);
        _dashboardWidgetLayout.ResumeLayout(false);
        _dashboardWidgetLayout.PerformLayout();
        ((System.ComponentModel.ISupportInitialize)_volumeMeterDelayNumeric).EndInit();
        _syncTabPage.ResumeLayout(false);
        _syncLayout.ResumeLayout(false);
        _syncLayout.PerformLayout();
        ResumeLayout(false);
    }

    #endregion

    private TableLayoutPanel _rootLayout;
    private CheckBox _enabledCheckBox;
    private TabControl _subTabControl;
    private TabPage _connectionTabPage;
    private TableLayoutPanel _connectionLayout;
    private CheckBox _autoConnectCheckBox;
    private Label _hostLabel;
    private TextBox _hostTextBox;
    private Label _portLabel;
    private NumericUpDown _portNumeric;
    private Label _passwordLabel;
    private TextBox _passwordTextBox;
    private FlowLayoutPanel _connectionButtonPanel;
    private Button _testConnectionButton;
    private Button _reconnectButton;
    private Button _loadScenesButton;
    private TabPage _browserSourceTabPage;
    private TableLayoutPanel _browserSourceLayout;
    private CheckBox _autoProvisionCheckBox;
    private Label _sceneLabel;
    private ComboBox _sceneComboBox;
    private Label _sourceNameLabel;
    private TextBox _sourceNameTextBox;
    private Label _sizeLabel;
    private FlowLayoutPanel _sizeFlowPanel;
    private NumericUpDown _widthNumeric;
    private Label _sizeSeparatorLabel;
    private NumericUpDown _heightNumeric;
    private Label _overlayUrlLabel;
    private TextBox _overlayUrlTextBox;
    private FlowLayoutPanel _browserSourceButtonPanel;
    private Button _provisionButton;
    private Button _copyUrlButton;
    private TabPage _chatRefreshTabPage;
    private TableLayoutPanel _chatRefreshLayout;
    private CheckBox _refreshOnStreamStartCheckBox;
    private Label _chatRefreshSourcesLabel;
    private CheckedListBox _chatRefreshSourcesCheckedListBox;
    private FlowLayoutPanel _chatRefreshButtonPanel;
    private Button _refreshChatNowButton;
    private TabPage _dashboardWidgetTabPage;
    private TableLayoutPanel _dashboardWidgetLayout;
    private Label _volumeMeterDelayLabel;
    private NumericUpDown _volumeMeterDelayNumeric;
    private Label _sourcesLabel;
    private CheckedListBox _sourcesCheckedListBox;
    private TabPage _syncTabPage;
    private TableLayoutPanel _syncLayout;
    private CheckBox _syncSceneOnProfileCheckBox;
    private CheckBox _syncProfileOnSceneCheckBox;
    private Label _statusLabel;
    private ToolTip _hintToolTip;
}
