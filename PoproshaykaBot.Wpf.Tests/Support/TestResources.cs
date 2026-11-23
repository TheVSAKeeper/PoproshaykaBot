using KeepShell.Testing;
using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Tests.Support;

internal static class TestResources
{
    public static void Ensure(IEnumerable<string> sources)
    {
        PackScheme.Ensure();

        var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var merged = application.Resources.MergedDictionaries;

        foreach (var source in sources)
        {
            if (merged.All(dictionary => dictionary.Source?.OriginalString != source))
            {
                merged.Add(new ResourceDictionary { Source = new(source) });
            }
        }
    }

    public static void CaptureAccountLayout(FrameworkElement host, string name, int width, int height)
    {
        var directory = Environment.GetEnvironmentVariable("POPROSHAYKA_TEST_ACCOUNT_LAYOUT");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(host);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, $"{name}.png"));
        png.Save(output);
    }
}
