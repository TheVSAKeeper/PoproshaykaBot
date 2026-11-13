using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Polling;
using PoproshaykaBot.Core.Polls;
using PoproshaykaBot.WinForms.Controls;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Tiles;

namespace PoproshaykaBot.WinForms.Forms.Polls;

public partial class PollsControlPanel : UserControl, IDashboardTileHeaderProvider
{
    private readonly List<IDisposable> _subs = [];
    private readonly ToolStripStatusIndicator _status = new();
    private bool _initialized;
    private ToolStripButton? _adHocButton;
    private ToolStripButton? _fromProfileButton;

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

    public IReadOnlyList<ToolStripItem> CreateHeaderItems()
    {
        _adHocButton = new()
        {
            AutoToolTip = false,
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Text = "+ Создать опрос",
        };

        _adHocButton.Click += OnAdHocClicked;

        _fromProfileButton = new()
        {
            AutoToolTip = false,
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Text = "Из профиля…",
        };

        _fromProfileButton.Click += OnFromProfileClicked;

        return [_adHocButton, _fromProfileButton, _status.Label];
    }

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
            _status.Clear();
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

        Disposed += (_, _) => _status.Dispose();
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
                _status.Show("✗ Не удалось завершить голосование", true);
            }
        }
        catch (Exception ex)
        {
            _status.Show($"✗ {ex.Message}", true);
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

    private void OnCardsFlowClientSizeChanged(object? sender, EventArgs e)
    {
        ResizeCardsToFlow();
    }

    private async Task StartPollAsync(PollProfile profile)
    {
        _status.Show("Запуск голосования…", false);

        try
        {
            await Controller.StartAsync(profile, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _status.Show($"✗ {ex.Message}", true);
        }
    }

    private void OnPollStartFailed(PollStartFailed @event)
    {
        _status.Show($"✗ {@event.SafeMessage}", true);
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
}
