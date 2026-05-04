namespace PoproshaykaBot.WinForms.Forms.Polls;

partial class PollsControlPanel
{
    private System.ComponentModel.IContainer components = null;

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
        _cardsFlow = new FlowLayoutPanel();
        _emptyLabel = new Label();
        _statusResetTimer = new System.Windows.Forms.Timer(components);
        _liveRefreshTimer = new System.Windows.Forms.Timer(components);
        SuspendLayout();
        //
        // _cardsFlow
        //
        _cardsFlow.AutoScroll = true;
        _cardsFlow.Dock = DockStyle.Fill;
        _cardsFlow.FlowDirection = FlowDirection.TopDown;
        _cardsFlow.Location = new Point(0, 0);
        _cardsFlow.Name = "_cardsFlow";
        _cardsFlow.Padding = new Padding(4);
        _cardsFlow.Size = new Size(528, 315);
        _cardsFlow.TabIndex = 0;
        _cardsFlow.WrapContents = false;
        _cardsFlow.ClientSizeChanged += OnCardsFlowClientSizeChanged;
        //
        // _emptyLabel
        //
        _emptyLabel.Dock = DockStyle.Fill;
        _emptyLabel.ForeColor = SystemColors.GrayText;
        _emptyLabel.Location = new Point(0, 0);
        _emptyLabel.Name = "_emptyLabel";
        _emptyLabel.Size = new Size(528, 315);
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
        MinimumSize = new Size(320, 180);
        Name = "PollsControlPanel";
        Size = new Size(528, 315);
        ResumeLayout(false);
    }
}
