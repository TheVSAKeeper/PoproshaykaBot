using PoproshaykaBot.Assets;
using System.Net;
using System.Text;

namespace PoproshaykaBot.Core.Infrastructure.Http.Handlers;

/// <summary>
/// Обработчик статических файлов (HTML, CSS, JS, favicon).
/// </summary>
public class StaticFilesHandler : IHttpRequestHandler
{
    private static readonly string ObsOverlayHtmlContent = ObsOverlayResources.ObsOverlayHtml;
    private static readonly string ObsOverlayCssContent = ObsOverlayResources.ObsOverlayCss;
    private static readonly byte[] ObsOverlayJsContent = ObsOverlayResources.ObsOverlayJs;
    private static readonly byte[] FaviconIcoContent = ObsOverlayResources.FaviconIco;

    private static readonly Dictionary<string, (string ContentType, Func<byte[]> Content)> StaticFiles = new()
    {
        ["/chat"] = ("text/html; charset=utf-8", () => Encoding.UTF8.GetBytes(ObsOverlayHtmlContent)),
        ["/assets/obs.css"] = ("text/css; charset=utf-8", () => Encoding.UTF8.GetBytes(ObsOverlayCssContent)),
        ["/assets/obs.js"] = ("application/javascript; charset=utf-8", () => ObsOverlayJsContent),
        ["/favicon.ico"] = ("image/x-icon", () => FaviconIcoContent),
    };

    public event Action<string>? LogMessage;

    public bool CanHandle(HttpListenerRequest request)
    {
        return request.Url != null && StaticFiles.ContainsKey(request.Url.AbsolutePath);
    }

    public async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            var path = request.Url?.AbsolutePath ?? "";

            if (StaticFiles.TryGetValue(path, out var fileInfo))
            {
                var content = fileInfo.Content();
                response.ContentType = fileInfo.ContentType;
                response.ContentLength64 = content.Length;
                await response.OutputStream.WriteAsync(content);
            }
            else
            {
                response.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            LogMessage?.Invoke($"Ошибка отдачи статического файла {request.Url?.AbsolutePath}: {ex.Message}");
            response.StatusCode = 500;
        }
        finally
        {
            response.Close();
        }
    }
}
