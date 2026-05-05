using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class SseEndpoint(SseService sseService) : IEndpointMapper
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        var lifetime = endpoints.ServiceProvider.GetRequiredService<IHostApplicationLifetime>();

        endpoints.MapGet("/events", async ctx =>
        {
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var bufferingFeature = ctx.Features.Get<IHttpResponseBodyFeature>();
            bufferingFeature?.DisableBuffering();

            await ctx.Response.Body.FlushAsync();

            sseService.AddClient(ctx.Response);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted,
                lifetime.ApplicationStopping);

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                sseService.RemoveClient(ctx.Response);
            }
        });
    }
}
