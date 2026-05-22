using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class StaticAssetsEndpoint : IEndpointMapper
{
    private static readonly string OverlayHtml = ResourceLoader.LoadResourceText("PoproshaykaBot.Core.Assets.ObsOverlay.html");
    private static readonly string AnimationsDemoHtml = ResourceLoader.LoadResourceText("PoproshaykaBot.Core.Assets.animations-demo.html");
    private static readonly byte[] ObsCssBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.Core.Assets.obs.css");
    private static readonly byte[] ObsJsBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.Core.Assets.obs.js");
    private static readonly byte[] AnimationsDemoCssBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.Core.Assets.animations-demo.css");
    private static readonly byte[] AnimationsDemoJsBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.Core.Assets.animations-demo.js");
    private static readonly byte[] AnimationsDemoConfigJsBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.Core.Assets.animations-demo-config.js");
    private static readonly byte[] FaviconBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.Core.Assets.icon.ico");

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/chat", () => Results.Content(OverlayHtml, "text/html; charset=utf-8"));
        endpoints.MapGet("/animations-demo", () => Results.Content(AnimationsDemoHtml, "text/html; charset=utf-8"));
        endpoints.MapGet("/assets/obs.css", () => Results.Bytes(ObsCssBytes, "text/css; charset=utf-8"));
        endpoints.MapGet("/assets/obs.js", () => Results.Bytes(ObsJsBytes, "application/javascript; charset=utf-8"));
        endpoints.MapGet("/assets/animations-demo.css", () => Results.Bytes(AnimationsDemoCssBytes, "text/css; charset=utf-8"));
        endpoints.MapGet("/assets/animations-demo.js", () => Results.Bytes(AnimationsDemoJsBytes, "application/javascript; charset=utf-8"));
        endpoints.MapGet("/assets/animations-demo-config.js", () => Results.Bytes(AnimationsDemoConfigJsBytes, "application/javascript; charset=utf-8"));
        endpoints.MapGet("/favicon.ico", () => Results.Bytes(FaviconBytes, "image/x-icon"));
    }
}
