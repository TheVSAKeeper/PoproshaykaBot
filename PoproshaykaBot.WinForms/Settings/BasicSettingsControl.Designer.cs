namespace PoproshaykaBot.WinForms.Settings
{
    partial class BasicSettingsControl
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
            _basicTableLayout = new TableLayoutPanel();
            _channelLabel = new Label();
            _channelFlow = new FlowLayoutPanel();
            _channelTextBox = new TextBox();
            _channelResetButton = new Button();
            _basicTableLayout.SuspendLayout();
            _channelFlow.SuspendLayout();
            SuspendLayout();
            //
            // _basicTableLayout
            //
            _basicTableLayout.ColumnCount = 2;
            _basicTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _basicTableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _basicTableLayout.Controls.Add(_channelLabel, 0, 0);
            _basicTableLayout.Controls.Add(_channelFlow, 1, 0);
            _basicTableLayout.Dock = DockStyle.Fill;
            _basicTableLayout.Name = "_basicTableLayout";
            _basicTableLayout.Padding = new Padding(5);
            _basicTableLayout.RowCount = 2;
            _basicTableLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _basicTableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _basicTableLayout.TabIndex = 0;
            //
            // _channelLabel
            //
            _channelLabel.AutoSize = true;
            _channelLabel.Dock = DockStyle.Fill;
            _channelLabel.Margin = new Padding(0, 0, 6, 0);
            _channelLabel.Name = "_channelLabel";
            _channelLabel.TabIndex = 0;
            _channelLabel.Text = "Канал:";
            _channelLabel.TextAlign = ContentAlignment.MiddleLeft;
            //
            // _channelFlow
            //
            _channelFlow.AutoSize = true;
            _channelFlow.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _channelFlow.Controls.Add(_channelTextBox);
            _channelFlow.Controls.Add(_channelResetButton);
            _channelFlow.Dock = DockStyle.Fill;
            _channelFlow.FlowDirection = FlowDirection.LeftToRight;
            _channelFlow.Margin = new Padding(0);
            _channelFlow.Name = "_channelFlow";
            _channelFlow.TabIndex = 1;
            _channelFlow.WrapContents = false;
            //
            // _channelTextBox
            //
            _channelTextBox.Margin = new Padding(0, 3, 6, 0);
            _channelTextBox.MinimumSize = new Size(260, 0);
            _channelTextBox.Name = "_channelTextBox";
            _channelTextBox.TabIndex = 0;
            _channelTextBox.TextChanged += OnSettingChanged;
            //
            // _channelResetButton
            //
            _channelResetButton.Margin = new Padding(0);
            _channelResetButton.MinimumSize = new Size(28, 28);
            _channelResetButton.Name = "_channelResetButton";
            _channelResetButton.TabIndex = 1;
            _channelResetButton.Text = "↺";
            _channelResetButton.UseVisualStyleBackColor = true;
            _channelResetButton.Click += OnChannelResetButtonClicked;
            //
            // BasicSettingsControl
            //
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(_basicTableLayout);
            Name = "BasicSettingsControl";
            Size = new Size(548, 60);
            _basicTableLayout.ResumeLayout(false);
            _basicTableLayout.PerformLayout();
            _channelFlow.ResumeLayout(false);
            _channelFlow.PerformLayout();
            ResumeLayout(false);
        }

        #endregion

        private TableLayoutPanel _basicTableLayout;
        private Label _channelLabel;
        private FlowLayoutPanel _channelFlow;
        private TextBox _channelTextBox;
        private Button _channelResetButton;
    }
}
