using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed class BotAuthorizationPageViewModel : AuthorizationPageViewModelBase
{
    public BotAuthorizationPageViewModel(
        ITwitchOAuthService oauthService,
        AccountsStore accountsStore,
        ILogger<BotAuthorizationPageViewModel> logger,
        IShellLauncher shellLauncher,
        IClipboardService clipboard,
        IEmbeddedTwitchAuthDialog embeddedAuth,
        IUiDispatcher uiDispatcher)
        : base(PlatformAuthRole.Bot, oauthService, accountsStore, logger, shellLauncher, clipboard, embeddedAuth, uiDispatcher)
    {
    }
}
