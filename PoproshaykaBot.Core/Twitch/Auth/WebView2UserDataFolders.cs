using PoproshaykaBot.Core.Infrastructure;

namespace PoproshaykaBot.Core.Twitch.Auth;

public static class WebView2UserDataFolders
{
    private const string RootFolderName = "WebView2";

    public static string Resolve(TwitchOAuthRole role)
    {
        return AppPaths.Combine(RootFolderName, RoleSegment(role));
    }

    private static string RoleSegment(TwitchOAuthRole role)
    {
        return role switch
        {
            TwitchOAuthRole.Broadcaster => "broadcaster",
            _ => "bot",
        };
    }
}
