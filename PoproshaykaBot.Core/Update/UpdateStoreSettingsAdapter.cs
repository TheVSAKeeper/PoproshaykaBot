using KeepShell.Updates;
using PoproshaykaBot.Core.Settings.Stores;
using CoreUpdateApplyMode = PoproshaykaBot.Core.Settings.Update.UpdateApplyMode;
using CoreUpdateSettings = PoproshaykaBot.Core.Settings.Update.UpdateSettings;
using KeepUpdateApplyMode = KeepShell.Updates.UpdateApplyMode;

namespace PoproshaykaBot.Core.Update;

public sealed class UpdateStoreSettingsAdapter(UpdateStore store) : IUpdateSettings
{
    private readonly UpdateStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly Lock _syncLock = new();
    private readonly List<Action<CoreUpdateSettings>> _pending = [];

    public bool AutomaticCheckEnabled
    {
        get => Read().AutoCheckEnabled;
        set => Queue(settings => settings.AutoCheckEnabled = value);
    }

    public TimeSpan CheckInterval
    {
        get => TimeSpan.FromHours(Math.Max(1, Read().CheckIntervalHours));
        set
        {
            var hours = Math.Max(1, (int)Math.Round(value.TotalHours, MidpointRounding.AwayFromZero));
            Queue(settings => settings.CheckIntervalHours = hours);
        }
    }

    public KeepUpdateApplyMode ApplyMode
    {
        get => Read().ApplyMode == CoreUpdateApplyMode.SilentOnExit
            ? KeepUpdateApplyMode.Automatic
            : KeepUpdateApplyMode.Notify;
        set => Queue(settings => settings.ApplyMode = value == KeepUpdateApplyMode.Automatic
            ? CoreUpdateApplyMode.SilentOnExit
            : CoreUpdateApplyMode.NotifyAndConfirm);
    }

    public bool AllowFrameworkDependentUpdate
    {
        get => Read().AllowFrameworkDependentUpdate;
        set => Queue(settings => settings.AllowFrameworkDependentUpdate = value);
    }

    public string? SkippedVersion
    {
        get => Read().SkippedVersion;
        set => Queue(settings => settings.SkippedVersion = NormalizeOptional(value));
    }

    public string? RepositoryOverride
    {
        get => Read().RepositoryOverride;
        set => Queue(settings => settings.RepositoryOverride = NormalizeOptional(value));
    }

    public DateTimeOffset? LastCheckUtc
    {
        get => Read().LastCheckUtc;
        set => Queue(settings => settings.LastCheckUtc = value);
    }

    public void Save()
    {
        lock (_syncLock)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            var settings = _store.Load();

            foreach (var mutator in _pending)
            {
                mutator(settings);
            }

            _store.Save(settings);
            _pending.Clear();
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private CoreUpdateSettings Read()
    {
        lock (_syncLock)
        {
            var settings = _store.Load();

            foreach (var mutator in _pending)
            {
                mutator(settings);
            }

            return settings;
        }
    }

    private void Queue(Action<CoreUpdateSettings> mutator)
    {
        lock (_syncLock)
        {
            _pending.Add(mutator);
        }
    }
}
