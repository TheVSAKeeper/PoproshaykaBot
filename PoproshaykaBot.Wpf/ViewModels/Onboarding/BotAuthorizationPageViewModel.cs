using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed class BotAuthorizationPageViewModel : AuthorizationPageViewModelBase
{
    public BotAuthorizationPageViewModel(
        ITwitchOAuthService oauthService,
        SettingsManager settingsManager,
        ILogger<BotAuthorizationPageViewModel> logger)
        : base(TwitchOAuthRole.Bot, oauthService, settingsManager, logger)
    {
    }
}
