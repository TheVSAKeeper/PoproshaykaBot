using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
            if (!sseService.IsRunning)
            {
                ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.Headers.Connection = "keep-alive";

            var bufferingFeature = ctx.Features.Get<IHttpResponseBodyFeature>();
            bufferingFeature?.DisableBuffering();

            await ctx.Response.Body.FlushAsync();
            await ctx.Response.Body.WriteAsync("retry: 3000\n\n"u8.ToArray());
            await ctx.Response.Body.FlushAsync();

            if (!sseService.AddClient(ctx.Response))
            {
                return;
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted,
                lifetime.ApplicationStopping);

            try
            {
                await Task.Delay(Timeout.Infinite, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // client disconnected or app stopping
            }
            finally
            {
                sseService.RemoveClient(ctx.Response);
            }
        });
    }
}
