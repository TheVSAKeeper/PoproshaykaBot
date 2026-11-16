using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;

namespace PoproshaykaBot.Wpf.ViewModels.Onboarding;

public sealed class OnboardingContext(
    AppSettings settings,
    TwitchAccountSettings botAccount,
    TwitchAccountSettings broadcasterAccount,
    YouTubeSettings? youTubeSettings = null,
    string youTubeClientSecret = "",
    YouTubeAccountSettings? youTubeBotAccount = null,
    YouTubeAccountSettings? youTubeBroadcasterAccount = null)
{
    public event EventHandler? CredentialsChanged;

    public AppSettings Settings { get; } = settings;

    public TwitchAccountSettings BotAccount { get; set; } = botAccount;

    public TwitchAccountSettings BroadcasterAccount { get; set; } = broadcasterAccount;

    public YouTubeSettings YouTubeSettings { get; } = youTubeSettings ?? new();

    public string YouTubeClientSecret { get; set; } = youTubeClientSecret;

    public YouTubeAccountSettings YouTubeBotAccount { get; set; } = youTubeBotAccount ?? new();

    public YouTubeAccountSettings YouTubeBroadcasterAccount { get; set; } = youTubeBroadcasterAccount ?? new();

    public bool AutoDetectChannel { get; set; } = true;

    public void RaiseCredentialsChanged()
    {
        CredentialsChanged?.Invoke(this, EventArgs.Empty);
    }
}
