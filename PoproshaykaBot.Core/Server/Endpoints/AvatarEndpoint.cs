using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class AvatarEndpoint(
    UserProfileImageProvider userProfileImageProvider,
    IServiceProvider serviceProvider) : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/user-avatar", async (HttpContext ctx) =>
        {
            var userId = ctx.Request.Query["id"].FirstOrDefault();
            var login = ctx.Request.Query["login"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(login))
            {
                return Results.BadRequest();
            }

            string? url;
            if (!string.IsNullOrWhiteSpace(userId))
            {
                url = await userProfileImageProvider.GetAsync(userId, ctx.RequestAborted);
            }
            else
            {
                var helix = serviceProvider.GetRequiredKeyedService<ITwitchHelixClient>(TwitchEndpoints.HelixBotClient);
                var user = await helix.GetUserByLoginAsync(login!, ctx.RequestAborted);
                url = user?.ProfileImageUrl;
            }

            if (string.IsNullOrEmpty(url))
            {
                return Results.NotFound();
            }

            ctx.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.Json(new { url }, ServerJsonOptions.Default);
        });
    }
}
