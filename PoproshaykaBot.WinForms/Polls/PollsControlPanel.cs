using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Polling;

namespace PoproshaykaBot.WinForms.Polls;

public partial class PollsControlPanel : UserControl
{
    private readonly List<IDisposable> _subs = [];
    private bool _initialized;

    public PollsControlPanel()
    {
        InitializeComponent();
    }

    [Inject]
    public PollProfilesManager Manager { get; internal init; } = null!;

    [Inject]
    public IPollController Controller { get; internal init; } = null!;

    [Inject]
    public PollSnapshotStore SnapshotStore { get; internal init; } = null!;

    [Inject]
    public IEventBus Bus { get; internal init; } = null!;

    [Inject]
    public IFormFactory Forms { get; internal init; } = null!;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized)
        {
            return;
        }

        if (this.IsInDesignMode())
        {
            return;
        }

        _initialized = true;

        _subs.Add(Bus.SubscribeOnUi<PollStarted>(this, _ =>
        {
            SetStatus(string.Empty, false);
            RebuildSnapshotCard();
        }));

        _subs.Add(Bus.SubscribeOnUi<PollProgressed>(this, _ => UpdateExistingCard()));
        _subs.Add(Bus.SubscribeOnUi<PollFinalized>(this, _ => UpdateExistingCard()));
        _subs.Add(Bus.SubscribeOnUi<PollTerminated>(this, _ => UpdateExistingCard()));
        _subs.Add(Bus.SubscribeOnUi<PollArchived>(this, _ => RebuildSnapshotCard()));
        _subs.Add(Bus.SubscribeOnUi<PollStartFailed>(this, OnPollStartFailed));
        _subs.DisposeOnClose(this);

        _liveRefreshTimer.Start();
        RebuildSnapshotCard();
    }

    private void OnAdHocClicked(object? sender, EventArgs e)
    {
        using var dialog = Forms.Create<PollProfileEditDialog>();

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (dialog.ShouldStartPoll && dialog.Result is not null)
        {
            _ = StartPollAsync(dialog.Result);
        }
    }

    private void OnFromProfileClicked(object? sender, EventArgs e)
    {
        var profiles = Manager.GetAll();

        if (profiles.Count == 0)
        {
            MessageBox.Show(this,
                "Нет сохранённых профилей. Используйте «+ Создать опрос», чтобы сохранить первый профиль.",
                "Нет профилей",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        using var dialog = Forms.Create<PollFromProfileDialog>();
        dialog.LoadProfiles(profiles);

        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedProfile is null)
        {
            return;
        }

        _ = StartPollAsync(dialog.SelectedProfile);
    }

    private async void OnCardStopRequested(object? sender, EventArgs e)
    {
        var snapshot = SnapshotStore.Current;

        if (snapshot is null || snapshot.Status != PollSnapshotStatus.Active)
        {
            return;
        }

        var result = MessageBox.Show(this,
            $"Завершить голосование «{snapshot.Title}» и показать результат зрителям?",
            "Подтверждение",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            var ok = await Controller.EndAsync(true, CancellationToken.None);

            if (!ok)
            {
                SetStatus("✗ Не удалось завершить голосование", true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"✗ {ex.Message}", true);
        }
    }

    private void OnLiveRefreshTimerTick(object? sender, EventArgs e)
    {
        var snapshot = SnapshotStore.Current;

        if (snapshot is null || snapshot.Status != PollSnapshotStatus.Active)
        {
            return;
        }

        foreach (var control in _cardsFlow.Controls)
        {
            if (control is ActivePollCard card && card.PollId == snapshot.PollId)
            {
                card.UpdateLive(snapshot);
            }
        }
    }

    private void OnStatusResetTimerTick(object? sender, EventArgs e)
    {
        _statusResetTimer.Stop();
        _statusLabel.Text = string.Empty;
        _statusLabel.ForeColor = SystemColors.ControlText;
    }

    private void OnCardsFlowClientSizeChanged(object? sender, EventArgs e)
    {
        ResizeCardsToFlow();
    }

    private async Task StartPollAsync(PollProfile profile)
    {
        SetStatus("Запуск голосования…", false);

        try
        {
            await Controller.StartAsync(profile, CancellationToken.None);
        }
        catch (Exception ex)
        {
            SetStatus($"✗ {ex.Message}", true);
        }
    }

    private void OnPollStartFailed(PollStartFailed @event)
    {
        SetStatus($"✗ {@event.SafeMessage}", true);
    }

    private void RebuildSnapshotCard()
    {
        var snapshot = SnapshotStore.Current;

        _cardsFlow.SuspendLayout();

        try
        {
            foreach (var control in _cardsFlow.Controls.Cast<Control>().ToList())
            {
                if (control is ActivePollCard oldCard)
                {
                    DetachCard(oldCard);
                    _cardsFlow.Controls.Remove(oldCard);
                    oldCard.Dispose();
                }
            }

            if (snapshot != null)
            {
                var card = new ActivePollCard { Width = ComputeCardWidth() };
                card.Bind(snapshot);
                AttachCard(card);
                _cardsFlow.Controls.Add(card);
            }

            var hasSnapshot = snapshot != null;
            _emptyLabel.Visible = !hasSnapshot;
            _cardsFlow.Visible = hasSnapshot;
        }
        finally
        {
            _cardsFlow.ResumeLayout();
        }
    }

    private void UpdateExistingCard()
    {
        var snapshot = SnapshotStore.Current;

        if (snapshot is null)
        {
            RebuildSnapshotCard();
            return;
        }

        ActivePollCard? matched = null;

        foreach (var control in _cardsFlow.Controls)
        {
            if (control is ActivePollCard card && card.PollId == snapshot.PollId)
            {
                matched = card;
                break;
            }
        }

        if (matched is null)
        {
            RebuildSnapshotCard();
            return;
        }

        matched.Bind(snapshot);
    }

    private int ComputeCardWidth()
    {
        return Math.Max(280, _cardsFlow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
    }

    private void ResizeCardsToFlow()
    {
        var width = ComputeCardWidth();

        foreach (var control in _cardsFlow.Controls)
        {
            if (control is ActivePollCard card)
            {
                card.Width = width;
            }
        }
    }

    private void AttachCard(ActivePollCard card)
    {
        card.StopRequested += OnCardStopRequested;
    }

    private void DetachCard(ActivePollCard card)
    {
        card.StopRequested -= OnCardStopRequested;
    }

    private void SetStatus(string text, bool isError)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;

        if (string.IsNullOrEmpty(text))
        {
            _statusResetTimer.Stop();
        }
        else
        {
            _statusResetTimer.Stop();
            _statusResetTimer.Start();
        }
    }
}
