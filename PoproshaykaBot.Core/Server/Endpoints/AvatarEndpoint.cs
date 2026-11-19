using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.Helix;
using PoproshaykaBot.Core.Users.Avatars;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class AvatarEndpoint(
    IUserAvatarCache avatarCache,
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
                var lookup = await avatarCache.ResolveUrlAsync(userId, ctx.RequestAborted);
                if (lookup.Status == AvatarUrlLookupStatus.TransientFailure)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }

                url = lookup.Url;
            }
            else
            {
                var helix = serviceProvider.GetRequiredKeyedService<ITwitchHelixClient>(TwitchEndpoints.HelixBotClient);
                try
                {
                    var user = await helix.GetUserByLoginAsync(login!, ctx.RequestAborted);
                    url = user?.ProfileImageUrl;
                }
                catch (Exception) when (!ctx.RequestAborted.IsCancellationRequested)
                {
                    return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                }
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
