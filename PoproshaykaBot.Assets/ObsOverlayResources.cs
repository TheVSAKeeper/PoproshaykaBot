using System.Reflection;
using System.Text;

namespace PoproshaykaBot.Assets;

public static class ObsOverlayResources
{
    private const string ResourcePrefix = "PoproshaykaBot.Assets.ObsOverlay.";
    private static readonly Assembly Assembly = typeof(ObsOverlayResources).Assembly;

    private static readonly Lazy<string> ObsOverlayHtmlLazy = new(() => LoadText("ObsOverlay.html"));
    private static readonly Lazy<string> ObsOverlayCssLazy = new(() => LoadText("obs.css"));
    private static readonly Lazy<byte[]> ObsOverlayJsLazy = new(() => LoadBytes("obs.js"));
    private static readonly Lazy<byte[]> FaviconIcoLazy = new(() => LoadBytes("icon.ico"));

    public static string ObsOverlayHtml => ObsOverlayHtmlLazy.Value;
    public static string ObsOverlayCss => ObsOverlayCssLazy.Value;
    public static byte[] ObsOverlayJs => ObsOverlayJsLazy.Value;
    public static byte[] FaviconIco => FaviconIcoLazy.Value;

    private static string LoadText(string fileName)
    {
        using var stream = OpenResourceStream(fileName);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }

    private static byte[] LoadBytes(string fileName)
    {
        using var stream = OpenResourceStream(fileName);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static Stream OpenResourceStream(string fileName)
    {
        var fullName = ResourcePrefix + fileName;
        var stream = Assembly.GetManifestResourceStream(fullName);

        if (stream == null)
        {
            throw new InvalidOperationException($"Не найден ресурс '{fullName}' в сборке {Assembly.FullName}");
        }

        return stream;
    }
}
