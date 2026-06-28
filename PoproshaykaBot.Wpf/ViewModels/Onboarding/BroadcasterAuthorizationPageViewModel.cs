using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed class BroadcasterAuthorizationPageViewModel : AuthorizationPageViewModelBase
{
    public BroadcasterAuthorizationPageViewModel(
        ITwitchOAuthService oauthService,
        SettingsManager settingsManager,
        ILogger<BroadcasterAuthorizationPageViewModel> logger)
        : base(TwitchOAuthRole.Broadcaster, oauthService, settingsManager, logger)
    {
    }
}
