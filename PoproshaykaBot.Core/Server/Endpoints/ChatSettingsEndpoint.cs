using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PoproshaykaBot.Core.Server.Obs;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class ChatSettingsEndpoint(ObsChatStore obsChatStore) : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/chat-settings", () =>
        {
            var settings = obsChatStore.Load();
            var cssSettings = ObsChatCssSettings.FromObsChatSettings(settings);
            return Results.Json(cssSettings, ServerJsonOptions.Default);
        });
    }
}
