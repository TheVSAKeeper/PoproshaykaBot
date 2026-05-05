using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PoproshaykaBot.Core.Users;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class AvatarEndpoint(UserProfileImageProvider userProfileImageProvider) : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/user-avatar", async (HttpContext ctx) =>
        {
            var userId = ctx.Request.Query["id"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(userId))
            {
                return Results.BadRequest();
            }

            var url = await userProfileImageProvider.GetAsync(userId, ctx.RequestAborted);

            if (string.IsNullOrEmpty(url))
            {
                return Results.NotFound();
            }

            ctx.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.Json(new { url }, ServerJsonOptions.Default);
        });
    }
}
