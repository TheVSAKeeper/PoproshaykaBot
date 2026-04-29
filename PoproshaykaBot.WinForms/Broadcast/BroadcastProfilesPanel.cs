using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Streaming;
using PoproshaykaBot.WinForms.Twitch.Helix;
using Timer = System.Windows.Forms.Timer;

namespace PoproshaykaBot.WinForms.Broadcast;

public partial class BroadcastProfilesPanel : UserControl
{
    private const int MinCardWidth = 220;
    private const int CardMargin = 6;
    private readonly List<IDisposable> _subs = [];
    private readonly Timer _statusResetTimer;
    private bool _initialized;
    private Guid? _activeProfileId;

    public BroadcastProfilesPanel()
    {
        InitializeComponent();

        _statusResetTimer = new()
        {
            Interval = 5000,
        };

        _statusResetTimer.Tick += (_, _) =>
        {
            _statusResetTimer.Stop();
            _statusLabel.Text = string.Empty;
            _statusLabel.ForeColor = SystemColors.ControlText;
        };

        _addButton.Click += OnAddClicked;
        _editCurrentButton.Click += OnEditCurrentClicked;
        _cardsFlow.ClientSizeChanged += (_, _) => RecomputeCardWidths();
    }

    [Inject]
    public BroadcastProfilesManager Manager { get; internal init; } = null!;

    [Inject]
    public IFormFactory Forms { get; internal init; } = null!;

    [Inject]
    public IEventBus Bus { get; internal init; } = null!;

    [Inject]
    public SettingsManager Settings { get; internal init; } = null!;

    [Inject]
    public IStreamStatus Stream { get; internal init; } = null!;

    [Inject]
    public IChannelInformationApplier Applier { get; internal init; } = null!;

    [Inject]
    public ITwitchChannelsApi ChannelsApi { get; internal init; } = null!;

    [Inject]
    public IBroadcasterIdProvider BroadcasterId { get; internal init; } = null!;

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

        _activeProfileId = Settings.Current.Twitch.BroadcastProfiles.LastAppliedProfileId;

        _subs.Add(Bus.SubscribeOnUi<BroadcastProfilesChanged>(this, _ => ReloadCards()));
        _subs.Add(Bus.SubscribeOnUi<BroadcastProfileApplied>(this, OnProfileApplied));
        _subs.Add(Bus.SubscribeOnUi<BroadcastProfileApplyFailed>(this, OnProfileApplyFailed));
        _subs.Add(Bus.SubscribeOnUi<StreamWentOnline>(this, _ => ReloadCards()));
        _subs.Add(Bus.SubscribeOnUi<StreamWentOffline>(this, _ => ReloadCards()));
        _subs.Add(Bus.SubscribeOnUi<ChannelUpdated>(this, _ => ReloadCards()));
        _subs.DisposeOnClose(this);

        Disposed += (_, _) => _statusResetTimer.Dispose();

        ReloadCards();
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        var profile = new BroadcastProfile { Name = $"Новый профиль {DateTime.Now:HH:mm:ss}" };

        if (!EditProfile(profile))
        {
            return;
        }

        PersistEdited(profile);
    }

    private async void OnEditCurrentClicked(object? sender, EventArgs e)
    {
        _editCurrentButton.Enabled = false;
        SetStatus("Загружаем текущие настройки…", false);

        BroadcastProfile? draft;

        try
        {
            var broadcasterId = await BroadcasterId.GetAsync(CancellationToken.None);

            if (string.IsNullOrEmpty(broadcasterId))
            {
                SetStatus("✗ Не удалось определить канал", true);
                return;
            }

            var info = await ChannelsApi.GetChannelInformationAsync(broadcasterId, CancellationToken.None);

            if (info == null)
            {
                SetStatus("✗ Не удалось загрузить настройки канала", true);
                return;
            }

            draft = new()
            {
                Id = Guid.Empty,
                Name = "(текущие настройки)",
                Title = info.Title ?? string.Empty,
                GameId = info.GameId ?? string.Empty,
                GameName = info.GameName ?? string.Empty,
                BroadcasterLanguage = string.IsNullOrEmpty(info.BroadcasterLanguage) ? "ru" : info.BroadcasterLanguage,
                Tags = info.Tags?.ToList() ?? [],
            };

            SetStatus(string.Empty, false);
        }
        catch (Exception ex)
        {
            SetStatus($"✗ {HelixErrorMessages.SafeMessage(ex)}", true);
            return;
        }
        finally
        {
            _editCurrentButton.Enabled = true;
        }

        using var dialog = Forms.Create<BroadcastProfileEditDialog>();
        dialog.LoadFrom(draft);
        dialog.ConfigureCurrentSettingsMode();

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        dialog.SaveTo(draft);

        var newProfileName = dialog.NewProfileName;

        if (string.IsNullOrEmpty(newProfileName))
        {
            SetStatus("⏳ Применяется текущая конфигурация…", false);
            await Applier.ApplyAsync(draft, CancellationToken.None);
            return;
        }

        var saved = new BroadcastProfile
        {
            Name = newProfileName,
            Title = draft.Title,
            GameId = draft.GameId,
            GameName = draft.GameName,
            BroadcasterLanguage = draft.BroadcasterLanguage,
            Tags = draft.Tags.ToList(),
        };

        try
        {
            Manager.Upsert(saved);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, true);
            return;
        }

        SetStatus($"⏳ Применяется «{saved.Name}»…", false);
        await Manager.ApplyAsync(saved.Id, CancellationToken.None);
    }

    private void OnCardApplyRequested(object? sender, EventArgs e)
    {
        if (sender is BroadcastProfileCard card)
        {
            OnApplyRequested(card);
        }
    }

    private void OnCardEditRequested(object? sender, EventArgs e)
    {
        if (sender is BroadcastProfileCard card)
        {
            OnEditRequested(card);
        }
    }

    private void OnCardDuplicateRequested(object? sender, EventArgs e)
    {
        if (sender is BroadcastProfileCard card)
        {
            OnDuplicateRequested(card);
        }
    }

    private void OnCardDeleteRequested(object? sender, EventArgs e)
    {
        if (sender is BroadcastProfileCard card)
        {
            OnDeleteRequested(card);
        }
    }

    private static bool ProfileDivergesFromStream(BroadcastProfile profile, StreamInfo? stream)
    {
        if (stream == null)
        {
            return false;
        }

        if (!string.Equals(profile.Title?.Trim(), stream.Title?.Trim(), StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(profile.GameId ?? string.Empty, stream.GameId ?? string.Empty, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static BroadcastProfile Clone(BroadcastProfile source)
    {
        return new()
        {
            Id = source.Id,
            Name = source.Name,
            Title = source.Title,
            GameId = source.GameId,
            GameName = source.GameName,
            BroadcasterLanguage = source.BroadcasterLanguage,
            Tags = source.Tags.ToList(),
        };
    }

    private bool EditProfile(BroadcastProfile profile)
    {
        using var dialog = Forms.Create<BroadcastProfileEditDialog>();
        dialog.LoadFrom(profile);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        dialog.SaveTo(profile);
        return true;
    }

    private void OnProfileApplied(BroadcastProfileApplied @event)
    {
        var isSavedProfile = @event.Profile.Id != Guid.Empty;
        _activeProfileId = isSavedProfile ? @event.Profile.Id : null;
        ClearInFlightStates();
        ReloadCards();
        SetStatus(isSavedProfile ? $"✓ Применён профиль «{@event.Profile.Name}»" : "✓ Применено", false);
    }

    private void OnProfileApplyFailed(BroadcastProfileApplyFailed @event)
    {
        ClearInFlightStates();
        SetStatus($"✗ {@event.ErrorMessage}", true);
    }

    private void OnEditRequested(BroadcastProfileCard card)
    {
        var original = card.Profile;

        if (original == null)
        {
            return;
        }

        var copy = Clone(original);

        if (EditProfile(copy))
        {
            PersistEdited(copy);
        }
    }

    private void PersistEdited(BroadcastProfile edited)
    {
        try
        {
            Manager.Upsert(edited);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, true);
        }
    }

    private void OnDuplicateRequested(BroadcastProfileCard card)
    {
        if (card.Profile == null)
        {
            return;
        }

        var source = card.Profile;
        var copy = new BroadcastProfile
        {
            Name = source.Name + " (копия)",
            Title = source.Title,
            GameId = source.GameId,
            GameName = source.GameName,
            BroadcasterLanguage = source.BroadcasterLanguage,
            Tags = source.Tags.ToList(),
        };

        try
        {
            Manager.Upsert(copy);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, true);
        }
    }

    private void OnDeleteRequested(BroadcastProfileCard card)
    {
        if (card.Profile == null)
        {
            return;
        }

        var result = MessageBox.Show(this,
            $"Удалить профиль «{card.Profile.Name}»?",
            "Удаление профиля",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        Manager.Remove(card.Profile.Id);
    }

    private async void OnApplyRequested(BroadcastProfileCard card)
    {
        if (card.Profile == null)
        {
            return;
        }

        card.SetApplyInFlight(true);
        SetStatus($"⏳ Применяется «{card.Profile.Name}»…", false);

        try
        {
            await Manager.ApplyAsync(card.Profile.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            card.SetApplyInFlight(false);
            SetStatus($"✗ {HelixErrorMessages.SafeMessage(ex)}", true);
        }
    }

    private void ReloadCards()
    {
        _cardsFlow.SuspendLayout();

        try
        {
            foreach (var child in _cardsFlow.Controls.Cast<Control>().ToList())
            {
                if (child is not BroadcastProfileCard oldCard)
                {
                    continue;
                }

                DetachCard(oldCard);
                oldCard.Dispose();
            }

            _cardsFlow.Controls.Clear();

            var activeId = _activeProfileId ?? Settings.Current.Twitch.BroadcastProfiles.LastAppliedProfileId;
            var currentStream = Stream.CurrentStream;

            var profiles = Manager.GetAll();
            if (activeId.HasValue && profiles.All(p => p.Id != activeId.Value))
            {
                _activeProfileId = null;
                activeId = null;
            }

            var cardWidth = ComputeCardWidth();

            foreach (var profile in profiles)
            {
                var isActive = activeId.HasValue && activeId.Value == profile.Id;
                var hasDrift = isActive && ProfileDivergesFromStream(profile, currentStream);

                var card = new BroadcastProfileCard { Width = cardWidth };
                card.UpdateFrom(profile, isActive, hasDrift);
                AttachCard(card);
                _cardsFlow.Controls.Add(card);
            }

            var hasProfiles = _cardsFlow.Controls.Count > 0;
            _emptyLabel.Visible = !hasProfiles;
            _cardsFlow.Visible = hasProfiles;
        }
        finally
        {
            _cardsFlow.ResumeLayout();
        }
    }

    private int ComputeCardWidth()
    {
        var available = _cardsFlow.ClientSize.Width - _cardsFlow.Padding.Horizontal;
        if (available <= MinCardWidth)
        {
            return Math.Max(MinCardWidth, available);
        }

        var columns = Math.Max(1, (available + CardMargin) / (MinCardWidth + CardMargin));
        return Math.Max(MinCardWidth, (available - columns * CardMargin) / columns);
    }

    private void RecomputeCardWidths()
    {
        var width = ComputeCardWidth();

        foreach (var control in _cardsFlow.Controls)
        {
            if (control is BroadcastProfileCard card)
            {
                card.Width = width;
            }
        }
    }

    private void AttachCard(BroadcastProfileCard card)
    {
        card.ApplyRequested += OnCardApplyRequested;
        card.EditRequested += OnCardEditRequested;
        card.DuplicateRequested += OnCardDuplicateRequested;
        card.DeleteRequested += OnCardDeleteRequested;
    }

    private void DetachCard(BroadcastProfileCard card)
    {
        card.ApplyRequested -= OnCardApplyRequested;
        card.EditRequested -= OnCardEditRequested;
        card.DuplicateRequested -= OnCardDuplicateRequested;
        card.DeleteRequested -= OnCardDeleteRequested;
    }

    private void ClearInFlightStates()
    {
        foreach (var control in _cardsFlow.Controls)
        {
            if (control is BroadcastProfileCard card)
            {
                card.SetApplyInFlight(false);
            }
        }
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
