using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed partial class PlatformLifecycleItemViewModel(StreamingPlatform platform, string displayName)
    : ObservableObject
{
    [ObservableProperty]
    private bool _autoConnectOnStreamOnline;

    [ObservableProperty]
    private bool _autoDisconnectOnStreamOffline;

    public StreamingPlatform Platform { get; } = platform;

    public string DisplayName { get; } = displayName;

    public void LoadSettings(BotLifecycleAutomationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AutoConnectOnStreamOnline = settings.AutoConnectOnStreamOnline;
        AutoDisconnectOnStreamOffline = settings.AutoDisconnectOnStreamOffline;
    }

    public BotLifecycleAutomationSettings ToSettings()
    {
        return new()
        {
            AutoConnectOnStreamOnline = AutoConnectOnStreamOnline,
            AutoDisconnectOnStreamOffline = AutoDisconnectOnStreamOffline,
        };
    }
}
