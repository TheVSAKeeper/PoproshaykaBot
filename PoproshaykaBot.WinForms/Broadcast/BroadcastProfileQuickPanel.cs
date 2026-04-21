using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using Timer = System.Windows.Forms.Timer;

namespace PoproshaykaBot.WinForms.Broadcast;

public partial class BroadcastProfileQuickPanel : UserControl
{
    private readonly List<IDisposable> _subs = [];
    private readonly Timer _statusResetTimer;
    private BroadcastProfilesManager? _manager;

    public BroadcastProfileQuickPanel()
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
        };

        _applyButton.Click += OnApplyClicked;
    }

    public void Setup(BroadcastProfilesManager manager, IEventBus eventBus)
    {
        _manager = manager;
        _subs.Add(eventBus.Subscribe<BroadcastProfilesChanged>(_ => BeginInvoke(ReloadProfiles)));
        _subs.Add(eventBus.Subscribe<BroadcastProfileApplied>(e => BeginInvoke(() => SetStatus($"✓ {e.Profile.Name}"))));
        _subs.Add(eventBus.Subscribe<BroadcastProfileApplyFailed>(e => BeginInvoke(() => SetStatus($"✗ {e.ErrorMessage}"))));

        ReloadProfiles();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var s in _subs)
            {
                s.Dispose();
            }

            _subs.Clear();
            _statusResetTimer.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        if (_manager == null || _profilesComboBox.SelectedItem is not BroadcastProfile p)
        {
            return;
        }

        _applyButton.Enabled = false;

        try
        {
            await _manager.ApplyAsync(p.Id, CancellationToken.None);
        }
        finally
        {
            _applyButton.Enabled = true;
        }
    }

    private void ReloadProfiles()
    {
        if (_manager == null)
        {
            return;
        }

        var selected = _profilesComboBox.SelectedItem as BroadcastProfile;
        _profilesComboBox.Items.Clear();

        foreach (var p in _manager.GetAll())
        {
            _profilesComboBox.Items.Add(p);
        }

        _profilesComboBox.DisplayMember = nameof(BroadcastProfile.Name);

        if (selected != null)
        {
            var match = _manager.GetAll().FirstOrDefault(p => p.Id == selected.Id);
            if (match != null)
            {
                _profilesComboBox.SelectedItem = match;
            }
        }

        _applyButton.Enabled = _profilesComboBox.Items.Count > 0;
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
        _statusResetTimer.Stop();
        _statusResetTimer.Start();
    }
}
