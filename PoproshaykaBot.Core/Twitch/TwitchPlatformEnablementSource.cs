using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Twitch;

public sealed class TwitchPlatformEnablementSource : IPlatformEnablementSource, IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly Lock _syncLock = new();
    private bool _lastEnabled;

    public TwitchPlatformEnablementSource(SettingsManager settingsManager)
    {
        ArgumentNullException.ThrowIfNull(settingsManager);
        _settingsManager = settingsManager;
        _lastEnabled = settingsManager.Current.Twitch.Enabled;
        _settingsManager.SettingsSaved += OnSettingsSaved;
    }

    public StreamingPlatform Platform => StreamingPlatform.Twitch;

    public bool IsEnabledInSettings => _settingsManager.Current.Twitch.Enabled;

    public event Action? Changed;

    public void Dispose()
    {
        _settingsManager.SettingsSaved -= OnSettingsSaved;
    }

    private void OnSettingsSaved()
    {
        lock (_syncLock)
        {
            var currentEnabled = _settingsManager.Current.Twitch.Enabled;
            if (currentEnabled == _lastEnabled)
            {
                return;
            }

            _lastEnabled = currentEnabled;
        }

        Changed?.Invoke();
    }
}
