namespace PoproshaykaBot.WinForms.Polls;

partial class PollsControlPanel
{
    private System.ComponentModel.IContainer components = null;

    private Panel _header;
    private FlowLayoutPanel _headerFlow;
    private Button _adHocButton;
    private Button _fromProfileButton;
    private Label _statusLabel;
    private FlowLayoutPanel _cardsFlow;
    private Label _emptyLabel;
    private System.Windows.Forms.Timer _statusResetTimer;
    private System.Windows.Forms.Timer _liveRefreshTimer;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        _header = new Panel();
        _headerFlow = new FlowLayoutPanel();
        _adHocButton = new Button();
        _fromProfileButton = new Button();
        _statusLabel = new Label();
        _cardsFlow = new FlowLayoutPanel();
        _emptyLabel = new Label();
        _statusResetTimer = new System.Windows.Forms.Timer(components);
        _liveRefreshTimer = new System.Windows.Forms.Timer(components);
        _header.SuspendLayout();
        _headerFlow.SuspendLayout();
        SuspendLayout();
        // 
        // _header
        // 
        _header.Controls.Add(_headerFlow);
        _header.Dock = DockStyle.Top;
        _header.Location = new Point(0, 0);
        _header.Name = "_header";
        _header.Padding = new Padding(4);
        _header.Size = new Size(528, 36);
        _header.TabIndex = 2;
        // 
        // _headerFlow
        // 
        _headerFlow.Controls.Add(_adHocButton);
        _headerFlow.Controls.Add(_fromProfileButton);
        _headerFlow.Controls.Add(_statusLabel);
        _headerFlow.Dock = DockStyle.Fill;
        _headerFlow.Location = new Point(4, 4);
        _headerFlow.Name = "_headerFlow";
        _headerFlow.Size = new Size(520, 28);
        _headerFlow.TabIndex = 0;
        _headerFlow.WrapContents = false;
        // 
        // _adHocButton
        // 
        _adHocButton.AutoSize = true;
        _adHocButton.Location = new Point(0, 0);
        _adHocButton.Margin = new Padding(0, 0, 4, 0);
        _adHocButton.Name = "_adHocButton";
        _adHocButton.Size = new Size(108, 25);
        _adHocButton.TabIndex = 0;
        _adHocButton.Text = "+ Создать опрос";
        _adHocButton.UseVisualStyleBackColor = true;
        _adHocButton.Click += OnAdHocClicked;
        // 
        // _fromProfileButton
        // 
        _fromProfileButton.AutoSize = true;
        _fromProfileButton.Location = new Point(112, 0);
        _fromProfileButton.Margin = new Padding(0, 0, 8, 0);
        _fromProfileButton.Name = "_fromProfileButton";
        _fromProfileButton.Size = new Size(93, 25);
        _fromProfileButton.TabIndex = 1;
        _fromProfileButton.Text = "Из профиля…";
        _fromProfileButton.UseVisualStyleBackColor = true;
        _fromProfileButton.Click += OnFromProfileClicked;
        // 
        // _statusLabel
        // 
        _statusLabel.Anchor = AnchorStyles.Right;
        _statusLabel.AutoSize = true;
        _statusLabel.Location = new Point(213, 8);
        _statusLabel.Margin = new Padding(0, 6, 0, 0);
        _statusLabel.Name = "_statusLabel";
        _statusLabel.Size = new Size(0, 15);
        _statusLabel.TabIndex = 2;
        // 
        // _cardsFlow
        // 
        _cardsFlow.AutoScroll = true;
        _cardsFlow.Dock = DockStyle.Fill;
        _cardsFlow.FlowDirection = FlowDirection.TopDown;
        _cardsFlow.Location = new Point(0, 36);
        _cardsFlow.Name = "_cardsFlow";
        _cardsFlow.Padding = new Padding(4);
        _cardsFlow.Size = new Size(528, 279);
        _cardsFlow.TabIndex = 0;
        _cardsFlow.WrapContents = false;
        _cardsFlow.ClientSizeChanged += OnCardsFlowClientSizeChanged;
        // 
        // _emptyLabel
        // 
        _emptyLabel.Dock = DockStyle.Fill;
        _emptyLabel.ForeColor = SystemColors.GrayText;
        _emptyLabel.Location = new Point(0, 36);
        _emptyLabel.Name = "_emptyLabel";
        _emptyLabel.Size = new Size(528, 279);
        _emptyLabel.TabIndex = 1;
        _emptyLabel.Text = "Нет активного голосования. Нажмите «+ Создать опрос» или «Из профиля…».";
        _emptyLabel.TextAlign = ContentAlignment.MiddleCenter;
        // 
        // _statusResetTimer
        // 
        _statusResetTimer.Interval = 5000;
        _statusResetTimer.Tick += OnStatusResetTimerTick;
        // 
        // _liveRefreshTimer
        // 
        _liveRefreshTimer.Interval = 1000;
        _liveRefreshTimer.Tick += OnLiveRefreshTimerTick;
        // 
        // PollsControlPanel
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        Controls.Add(_cardsFlow);
        Controls.Add(_emptyLabel);
        Controls.Add(_header);
        MinimumSize = new Size(320, 180);
        Name = "PollsControlPanel";
        Size = new Size(528, 315);
        _header.ResumeLayout(false);
        _headerFlow.ResumeLayout(false);
        _headerFlow.PerformLayout();
        ResumeLayout(false);
    }
}
