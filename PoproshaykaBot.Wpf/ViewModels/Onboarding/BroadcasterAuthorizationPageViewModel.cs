using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Wpf.Infrastructure;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed class BroadcasterAuthorizationPageViewModel : AuthorizationPageViewModelBase
{
    public BroadcasterAuthorizationPageViewModel(
        ITwitchOAuthService oauthService,
        AccountsStore accountsStore,
        ILogger<BroadcasterAuthorizationPageViewModel> logger,
        IShellLauncher shellLauncher,
        IClipboardService clipboard,
        IEmbeddedTwitchAuthDialog embeddedAuth,
        IUiDispatcher uiDispatcher)
        : base(PlatformAuthRole.Broadcaster, oauthService, accountsStore, logger, shellLauncher, clipboard, embeddedAuth, uiDispatcher)
    {
    }
}
