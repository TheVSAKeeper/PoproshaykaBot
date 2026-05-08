using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PoproshaykaBot.Core.Twitch.Auth;
using System.Text.Encodings.Web;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class OAuthCallbackEndpoint(ITwitchOAuthService twitchOAuthService) : IEndpointMapper
{
    private static readonly string SuccessHtml = ResourceLoader.LoadResourceText("PoproshaykaBot.Core.Assets.oauth-success.html");
    private static readonly string ErrorHtmlTemplate = ResourceLoader.LoadResourceText("PoproshaykaBot.Core.Assets.oauth-error.html");

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", (HttpContext ctx) =>
        {
            var code = ctx.Request.Query["code"].FirstOrDefault();
            var state = ctx.Request.Query["state"].FirstOrDefault();
            var error = ctx.Request.Query["error"].FirstOrDefault();

            if (!string.IsNullOrEmpty(code))
            {
                twitchOAuthService.SetAuthResult(code, state);
                return Results.Content(SuccessHtml, "text/html; charset=utf-8");
            }

            if (string.IsNullOrEmpty(error))
            {
                return Results.BadRequest();
            }

            twitchOAuthService.SetAuthError(new InvalidOperationException($"OAuth ошибка: {error}"));
            var html = ErrorHtmlTemplate.Replace("{0}", HtmlEncoder.Default.Encode(error), StringComparison.Ordinal);
            return Results.Content(html, "text/html; charset=utf-8");
        });
    }
}
