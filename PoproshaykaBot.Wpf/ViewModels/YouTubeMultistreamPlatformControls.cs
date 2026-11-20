using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.ViewModels;

public sealed class YouTubeMultistreamPlatformControls(SettingsPageViewModel settings) : IMultistreamPlatformControls
{
    public StreamingPlatform Platform => StreamingPlatform.YouTube;

    public YouTubeSettingsSectionViewModel? Settings { get; } = settings.SectionPresentations
        .SelectMany(presentation => presentation.Blocks)
        .Select(block => block.Content)
        .OfType<YouTubeSettingsSectionViewModel>()
        .FirstOrDefault();

    public bool RequiresCardSetup => Settings?.Enabled == false;
}
