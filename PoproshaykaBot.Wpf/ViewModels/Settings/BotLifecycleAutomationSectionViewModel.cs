using CommunityToolkit.Mvvm.ComponentModel;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using System.Collections.ObjectModel;

namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed class BotLifecycleAutomationSectionViewModel : ObservableObject, IDisposable
{
    public BotLifecycleAutomationSectionViewModel(IChatPlatformRegistry? platformRegistry = null)
    {
        if (platformRegistry is not null)
        {
            foreach (var platform in platformRegistry.All)
            {
                Platforms.Add(new(platform.Id, platform.DisplayName));
            }
        }
    }

    public ObservableCollection<PlatformLifecycleItemViewModel> Platforms { get; } = [];

    public void LoadSettings(PlatformLifecycleSettingsFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        foreach (var item in Platforms)
        {
            var settings = file.Platforms.TryGetValue(item.Platform, out var found) && found is not null
                ? found
                : new BotLifecycleAutomationSettings();
            item.LoadSettings(settings);
        }
    }

    public void SaveSettings(PlatformLifecycleSettingsFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        foreach (var item in Platforms)
        {
            file.Platforms[item.Platform] = item.ToSettings();
        }
    }

    public void Dispose()
    {
    }
}
