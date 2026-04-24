using PoproshaykaBot.WinForms.Broadcast.Profiles;
using PoproshaykaBot.WinForms.Infrastructure.Di;
using PoproshaykaBot.WinForms.Infrastructure.Events;
using PoproshaykaBot.WinForms.Infrastructure.Events.Broadcasting;
using Timer = System.Windows.Forms.Timer;

namespace PoproshaykaBot.WinForms.Broadcast;

public partial class BroadcastProfileQuickPanel : UserControl
{
    private readonly List<IDisposable> _subs = [];
    private readonly Timer _statusResetTimer;
    private bool _initialized;

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

    [Inject]
    public BroadcastProfilesManager Manager { get; init; } = null!;

    [Inject]
    public IEventBus Bus { get; init; } = null!;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        if (_initialized)
        {
            return;
        }

        _initialized = true;

        _subs.Add(Bus.SubscribeOnUi<BroadcastProfilesChanged>(this, _ => ReloadProfiles()));
        _subs.Add(Bus.SubscribeOnUi<BroadcastProfileApplied>(this, @event => SetStatus($"✓ {@event.Profile.Name}")));
        _subs.Add(Bus.SubscribeOnUi<BroadcastProfileApplyFailed>(this, @event => SetStatus($"✗ {@event.ErrorMessage}")));
        _subs.DisposeOnClose(this);

        Disposed += (_, _) => _statusResetTimer.Dispose();

        ReloadProfiles();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        if (_profilesComboBox.SelectedItem is not BroadcastProfile p)
        {
            return;
        }

        _applyButton.Enabled = false;

        try
        {
            await Manager.ApplyAsync(p.Id, CancellationToken.None);
        }
        finally
        {
            _applyButton.Enabled = true;
        }
    }

    private void ReloadProfiles()
    {
        var selected = _profilesComboBox.SelectedItem as BroadcastProfile;
        _profilesComboBox.Items.Clear();

        foreach (var p in Manager.GetAll())
        {
            _profilesComboBox.Items.Add(p);
        }

        _profilesComboBox.DisplayMember = nameof(BroadcastProfile.Name);

        if (selected != null)
        {
            var match = Manager.GetAll().FirstOrDefault(p => p.Id == selected.Id);
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
