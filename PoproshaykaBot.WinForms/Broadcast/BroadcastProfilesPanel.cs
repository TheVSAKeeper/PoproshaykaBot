using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using PoproshaykaBot.WinForms.Infrastructure.Events.Streaming;
using PoproshaykaBot.WinForms.Settings;
using PoproshaykaBot.WinForms.Streaming;
using PoproshaykaBot.WinForms.Twitch.Helix;
using System.Text.Json;
using Timer = System.Windows.Forms.Timer;

namespace PoproshaykaBot.WinForms.Broadcast;

public partial class BroadcastProfilesPanel : UserControl
{
    private static readonly JsonSerializerOptions ImportJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly List<IDisposable> _subs = [];
    private readonly Timer _statusResetTimer;
    private BroadcastProfilesManager? _manager;
    private IGameCategoryResolver? _resolver;
    private SettingsManager? _settingsManager;
    private IStreamStatus? _streamStatus;

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
        _importButton.Click += OnImportClicked;
        _cardsFlow.ClientSizeChanged += (_, _) => ResizeCardsToFlow();

        Disposed += (_, _) =>
        {
            foreach (var sub in _subs)
            {
                sub.Dispose();
            }

            _subs.Clear();
            _statusResetTimer.Dispose();
        };
    }

    public void Setup(
        BroadcastProfilesManager manager,
        IGameCategoryResolver resolver,
        IEventBus bus,
        SettingsManager? settingsManager = null,
        IStreamStatus? streamStatus = null)
    {
        _manager = manager;
        _resolver = resolver;
        _settingsManager = settingsManager;
        _streamStatus = streamStatus;

        _subs.Add(bus.Subscribe<BroadcastProfilesChanged>(_ => BeginInvoke(ReloadCards)));
        _subs.Add(bus.Subscribe<BroadcastProfileApplied>(_ => BeginInvoke(() =>
        {
            ClearInFlightStates();
            ReloadCards();
            SetStatus(string.Empty, false);
        })));

        _subs.Add(bus.Subscribe<BroadcastProfileApplyFailed>(e => BeginInvoke(() =>
        {
            ClearInFlightStates();
            SetStatus($"✗ {e.ErrorMessage}", true);
        })));

        _subs.Add(bus.Subscribe<StreamWentOnline>(_ => BeginInvoke(ReloadCards)));
        _subs.Add(bus.Subscribe<StreamWentOffline>(_ => BeginInvoke(ReloadCards)));

        ReloadCards();
    }

    private void OnAddClicked(object? sender, EventArgs e)
    {
        if (_manager == null || _resolver == null)
        {
            return;
        }

        var profile = new BroadcastProfile { Name = $"Новый профиль {DateTime.Now:HH:mm:ss}" };

        try
        {
            _manager.Upsert(profile);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, true);
            return;
        }

        if (BroadcastProfileEditDialog.Edit(this, profile, _resolver) == DialogResult.OK)
        {
            PersistEdited(profile);
        }
    }

    private void OnImportClicked(object? sender, EventArgs e)
    {
        if (_manager == null)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Импорт профилей из JSON",
            Filter = "JSON файлы (*.json)|*.json|Все файлы (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        List<ExternalTwitchProfile>? incoming;

        try
        {
            var json = File.ReadAllText(dialog.FileName);
            incoming = JsonSerializer.Deserialize<List<ExternalTwitchProfile>>(json, ImportJsonOptions);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Не удалось прочитать файл: {ex.Message}",
                "Ошибка импорта", MessageBoxButtons.OK, MessageBoxIcon.Error);

            return;
        }

        if (incoming == null || incoming.Count == 0)
        {
            MessageBox.Show(this, "В файле нет профилей.",
                "Импорт", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        var imported = 0;
        var skipped = 0;
        var existingNames = new HashSet<string>(_manager.GetAll().Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var external in incoming)
        {
            if (string.IsNullOrWhiteSpace(external.Name))
            {
                skipped++;
                continue;
            }

            var name = external.Name;
            var suffix = 2;

            while (existingNames.Contains(name))
            {
                name = $"{external.Name} ({suffix++})";
            }

            var profile = new BroadcastProfile
            {
                Name = name,
                Title = external.Title ?? string.Empty,
                GameId = external.CategoryId ?? string.Empty,
            };

            try
            {
                _manager.Upsert(profile);
                existingNames.Add(name);
                imported++;
            }
            catch (InvalidOperationException)
            {
                skipped++;
            }
        }

        MessageBox.Show(this,
            $"Импортировано: {imported}. Пропущено: {skipped}.",
            "Импорт профилей", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

    private void OnEditRequested(BroadcastProfileCard card)
    {
        if (_manager == null || _resolver == null)
        {
            return;
        }

        var original = card.Profile;

        if (original == null)
        {
            return;
        }

        var copy = Clone(original);

        if (BroadcastProfileEditDialog.Edit(this, copy, _resolver) == DialogResult.OK)
        {
            PersistEdited(copy);
        }
    }

    private void PersistEdited(BroadcastProfile edited)
    {
        try
        {
            _manager!.Upsert(edited);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, true);
        }
    }

    private void OnDuplicateRequested(BroadcastProfileCard card)
    {
        if (_manager == null || card.Profile == null)
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
            _manager.Upsert(copy);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, true);
        }
    }

    private void OnDeleteRequested(BroadcastProfileCard card)
    {
        if (_manager == null || card.Profile == null)
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

        _manager.Remove(card.Profile.Id);
    }

    private async void OnApplyRequested(BroadcastProfileCard card)
    {
        if (_manager == null || card.Profile == null)
        {
            return;
        }

        card.SetApplyInFlight(true);
        SetStatus("Применяем…", false);

        try
        {
            await _manager.ApplyAsync(card.Profile.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            card.SetApplyInFlight(false);
            SetStatus($"✗ {HelixErrorMessages.SafeMessage(ex)}", true);
        }
    }

    private void ReloadCards()
    {
        if (_manager == null)
        {
            return;
        }

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

            var activeId = _settingsManager?.Current.Twitch.BroadcastProfiles.LastAppliedProfileId;
            var currentStream = _streamStatus?.CurrentStream;

            foreach (var profile in _manager.GetAll())
            {
                var isActive = activeId.HasValue && activeId.Value == profile.Id;
                var hasDrift = isActive && ProfileDivergesFromStream(profile, currentStream);

                var card = new BroadcastProfileCard { Width = ComputeCardWidth() };
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
        return Math.Max(200, _cardsFlow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 8);
    }

    private void ResizeCardsToFlow()
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

    private sealed record ExternalTwitchProfile(string? Name, string? Title, string? CategoryId);
}
