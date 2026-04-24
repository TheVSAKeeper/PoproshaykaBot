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

        _subs.Add(Bus.Subscribe<BroadcastProfilesChanged>(OnBroadcastProfilesChanged));
        _subs.Add(Bus.Subscribe<BroadcastProfileApplied>(OnBroadcastProfileApplied));
        _subs.Add(Bus.Subscribe<BroadcastProfileApplyFailed>(OnBroadcastProfileApplyFailed));

        Disposed += OnControlDisposed;

        ReloadProfiles();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusResetTimer.Dispose();
            components?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnControlDisposed(object? sender, EventArgs e)
    {
        foreach (var sub in _subs)
        {
            sub.Dispose();
        }

        _subs.Clear();
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

    private void OnBroadcastProfilesChanged(BroadcastProfilesChanged @event)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnBroadcastProfilesChanged(@event));
                return;
            }

            ReloadProfiles();
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (IsDisposed)
        {
        }
    }

    private void OnBroadcastProfileApplied(BroadcastProfileApplied @event)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnBroadcastProfileApplied(@event));
                return;
            }

            SetStatus($"✓ {@event.Profile.Name}");
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (IsDisposed)
        {
        }
    }

    private void OnBroadcastProfileApplyFailed(BroadcastProfileApplyFailed @event)
    {
        if (IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke(() => OnBroadcastProfileApplyFailed(@event));
                return;
            }

            SetStatus($"✗ {@event.ErrorMessage}");
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException) when (IsDisposed)
        {
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
