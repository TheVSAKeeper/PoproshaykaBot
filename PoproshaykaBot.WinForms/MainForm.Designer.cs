namespace PoproshaykaBot.WinForms;

partial class MainForm
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    /// Кнопка подключения бота
    /// </summary>
    private Button _connectButton;

    /// <summary>
    /// Текстовое поле для отображения логов
    /// </summary>
    private TextBox _logTextBox;

    /// <summary>
    /// Метка для логов
    /// </summary>
    private Label _logLabel;

    /// <summary>
    /// Кнопка настроек
    /// </summary>
    private Button _settingsButton;

    /// <summary>
    /// Прогресс-бар для индикации процесса подключения
    /// </summary>
    private ProgressBar _connectionProgressBar;

    /// <summary>
    /// Метка для отображения статуса подключения
    /// </summary>
    private Label _connectionStatusLabel;

    /// <summary>
    ///  Clean up any resources being used.
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
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        this._connectButton = new Button();
        this._logTextBox = new TextBox();
        this._logLabel = new Label();
        this._settingsButton = new Button();
        this._connectionProgressBar = new ProgressBar();
        this._connectionStatusLabel = new Label();
        this.SuspendLayout();
        //
        // _connectButton
        //
        this._connectButton.Location = new Point(350, 20);
        this._connectButton.Name = "_connectButton";
        this._connectButton.Size = new Size(120, 40);
        this._connectButton.TabIndex = 0;
        this._connectButton.Text = "Подключить бота";
        this._connectButton.UseVisualStyleBackColor = true;
        this._connectButton.Click += new EventHandler(this.OnConnectButtonClicked);
        //
        // _logLabel
        //
        this._logLabel.AutoSize = true;
        this._logLabel.Location = new Point(12, 80);
        this._logLabel.Name = "_logLabel";
        this._logLabel.Size = new Size(35, 15);
        this._logLabel.TabIndex = 1;
        this._logLabel.Text = "Логи:";
        //
        // _logTextBox
        //
        this._logTextBox.Location = new Point(12, 100);
        this._logTextBox.Multiline = true;
        this._logTextBox.Name = "_logTextBox";
        this._logTextBox.ReadOnly = true;
        this._logTextBox.ScrollBars = ScrollBars.Vertical;
        this._logTextBox.Size = new Size(776, 338);
        this._logTextBox.TabIndex = 2;
        //
        // _settingsButton
        //
        this._settingsButton.Location = new Point(480, 20);
        this._settingsButton.Name = "_settingsButton";
        this._settingsButton.Size = new Size(80, 40);
        this._settingsButton.TabIndex = 3;
        this._settingsButton.Text = "Настройки";
        this._settingsButton.UseVisualStyleBackColor = true;
        this._settingsButton.Click += new EventHandler(this.OnSettingsButtonClicked);
        //
        // _connectionProgressBar
        //
        this._connectionProgressBar.Location = new Point(12, 20);
        this._connectionProgressBar.Name = "_connectionProgressBar";
        this._connectionProgressBar.Size = new Size(320, 20);
        this._connectionProgressBar.TabIndex = 4;
        this._connectionProgressBar.Style = ProgressBarStyle.Marquee;
        this._connectionProgressBar.MarqueeAnimationSpeed = 30;
        this._connectionProgressBar.Visible = false;
        //
        // _connectionStatusLabel
        //
        this._connectionStatusLabel.AutoSize = true;
        this._connectionStatusLabel.Location = new Point(12, 45);
        this._connectionStatusLabel.Name = "_connectionStatusLabel";
        this._connectionStatusLabel.Size = new Size(0, 15);
        this._connectionStatusLabel.TabIndex = 5;
        this._connectionStatusLabel.Text = "";
        this._connectionStatusLabel.Visible = false;
        //
        // MainForm
        //
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(800, 450);
        this.Controls.Add(this._connectButton);
        this.Controls.Add(this._settingsButton);
        this.Controls.Add(this._logLabel);
        this.Controls.Add(this._logTextBox);
        this.Controls.Add(this._connectionProgressBar);
        this.Controls.Add(this._connectionStatusLabel);
        this.Name = "MainForm";
        this.Text = "Попрощайка Бот - Управление";
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    #endregion
}
