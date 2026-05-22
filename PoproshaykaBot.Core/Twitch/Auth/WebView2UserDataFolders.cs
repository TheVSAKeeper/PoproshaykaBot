using PoproshaykaBot.Core.Infrastructure;

namespace PoproshaykaBot.Core.Twitch.Auth;

public static class WebView2UserDataFolders
{
    private const string RootFolderName = "WebView2";
    private const string OverlayPreviewSegment = "overlay-preview";

    public static string Resolve(TwitchOAuthRole role)
    {
        return AppPaths.Combine(RootFolderName, RoleSegment(role));
    }

    public static string ResolveOverlayPreview()
    {
        return AppPaths.Combine(RootFolderName, OverlayPreviewSegment);
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
