using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Twitch.Auth;

namespace PoproshaykaBot.WinForms.Forms.Onboarding;

public sealed class OnboardingContext(AppSettings settings, TwitchAccountSettings botAccount, TwitchAccountSettings broadcasterAccount)
{
    public AppSettings Settings { get; } = settings;

    public TwitchAccountSettings BotAccount { get; set; } = botAccount;

    public TwitchAccountSettings BroadcasterAccount { get; set; } = broadcasterAccount;
}
