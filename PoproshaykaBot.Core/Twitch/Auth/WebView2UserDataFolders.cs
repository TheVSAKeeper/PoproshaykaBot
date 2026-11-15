using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms.Auth;

namespace PoproshaykaBot.Core.Twitch.Auth;

public static class WebView2UserDataFolders
{
    private const string RootFolderName = "WebView2";
    private const string OverlayPreviewSegment = "overlay-preview";

    public static string Resolve(PlatformAuthRole role)
    {
        return AppPaths.Combine(RootFolderName, RoleSegment(role));
    }

    public static string ResolveOverlayPreview()
    {
        return AppPaths.Combine(RootFolderName, OverlayPreviewSegment);
    }

    private static string RoleSegment(PlatformAuthRole role)
    {
        return role switch
        {
            PlatformAuthRole.Broadcaster => "broadcaster",
            _ => "bot",
        };
    }
}
