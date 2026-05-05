using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class StaticAssetsEndpoint : IEndpointMapper
{
    private static readonly string OverlayHtml = ResourceLoader.LoadResourceText("PoproshaykaBot.Core.Assets.ObsOverlay.html");
    private static readonly byte[] ObsCssBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.Core.Assets.obs.css");
    private static readonly byte[] ObsJsBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.Core.Assets.obs.js");
    private static readonly byte[] FaviconBytes = ResourceLoader.LoadResourceBytes("PoproshaykaBot.Core.Assets.icon.ico");

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/chat", () => Results.Content(OverlayHtml, "text/html; charset=utf-8"));
        endpoints.MapGet("/assets/obs.css", () => Results.Bytes(ObsCssBytes, "text/css; charset=utf-8"));
        endpoints.MapGet("/assets/obs.js", () => Results.Bytes(ObsJsBytes, "application/javascript; charset=utf-8"));
        endpoints.MapGet("/favicon.ico", () => Results.Bytes(FaviconBytes, "image/x-icon"));
    }
}
