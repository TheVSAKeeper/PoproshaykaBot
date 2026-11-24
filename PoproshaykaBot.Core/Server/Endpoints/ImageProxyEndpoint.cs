using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Server.Images;
using PoproshaykaBot.Core.Settings.Stores;

namespace PoproshaykaBot.Core.Server.Endpoints;

internal sealed class ImageProxyEndpoint : IEndpointMapper
{
    public const string HttpClientName = MessageImageFetcher.HttpClientName;
    public const int MaxContentBytes = MessageImageFetcher.MaxContentBytes;
    public const int MaxRedirects = MessageImageFetcher.MaxRedirects;

    public static readonly TimeSpan RequestTimeout = MessageImageFetcher.RequestTimeout;

    private readonly ObsChatStore _obsChatStore;
    private readonly MessageImageFetcher _fetcher;
    private readonly ILogger<ImageProxyEndpoint>? _logger;

    [ActivatorUtilitiesConstructor]
    public ImageProxyEndpoint(
        ObsChatStore obsChatStore,
        MessageImageFetcher fetcher,
        ILogger<ImageProxyEndpoint>? logger = null)
    {
        _obsChatStore = obsChatStore;
        _fetcher = fetcher;
        _logger = logger;
    }

    internal ImageProxyEndpoint(
        ObsChatStore obsChatStore,
        IHttpClientFactory httpClientFactory,
        ILogger<ImageProxyEndpoint>? logger = null)
        : this(obsChatStore, new MessageImageFetcher(httpClientFactory), logger)
    {
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(MessageImagePolicy.ProxyPath, async (HttpContext ctx) =>
        {
            var settings = _obsChatStore.Load();

            if (!settings.ShowMessageImages)
            {
                Refuse("картинки в сообщениях выключены");
                return Results.NotFound();
            }

            var requested = ctx.Request.Query[MessageImagePolicy.UrlQueryKey].FirstOrDefault();

            if (!MessageImageUrlValidator.IsAllowed(requested, settings.MessageImageAllowedHosts, out var uri))
            {
                Refuse("ссылка не прошла проверку (схема, хост или расширение)");
                return Results.NotFound();
            }

            var image = await _fetcher.FetchAsync(uri, settings, ctx.RequestAborted, _logger);

            if (image == null)
            {
                return Results.NotFound();
            }

            ctx.Response.Headers.CacheControl = "public, max-age=3600";
            ctx.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.Bytes(image.Content, image.MediaType);
        });
    }

    private void Refuse(string reason)
    {
        _logger?.LogDebug("GET {Path}: картинка не отдана – {Reason}", MessageImagePolicy.ProxyPath, reason);
    }
}
