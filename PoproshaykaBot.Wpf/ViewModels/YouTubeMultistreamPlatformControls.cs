using System.ComponentModel;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed class YouTubeMultistreamPlatformControls : IMultistreamPlatformControls
{
    public YouTubeMultistreamPlatformControls(SettingsPageViewModel settings)
    {
        Settings = settings.SectionPresentations
            .Concat(settings.PlatformPresentations)
            .SelectMany(presentation => presentation.Blocks)
            .Select(block => block.Content)
            .OfType<YouTubeSettingsSectionViewModel>()
            .FirstOrDefault();

        if (Settings is not null)
        {
            Settings.PropertyChanged += OnSettingsPropertyChanged;
        }
    }

    public event EventHandler? StateChanged;

    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public YouTubeSettingsSectionViewModel? Settings { get; }

    public bool RequiresCardSetup => Settings?.Enabled == false;

    public bool CanEnableFromSettings => Settings is not null;

    public string SetupActionLabel => "Включить в параметрах";

    private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName)
            || e.PropertyName == nameof(YouTubeSettingsSectionViewModel.Enabled))
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
