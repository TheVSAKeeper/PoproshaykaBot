using KeepShell.Testing;
using PoproshaykaBot.Wpf.Bootstrap;
using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Tests.Support;

internal static class TestResources
{
    public static Application Ensure()
    {
        return EnsureCore(AppResources.All);
    }

    public static void Ensure(IEnumerable<string> sources)
    {
        var uris = sources
            .Select(static source => new Uri(source, UriKind.RelativeOrAbsolute))
            .ToArray();

        _ = EnsureCore(uris);
    }

    private static Application EnsureCore(Uri[] uris)
    {
        PackScheme.Ensure();

        var existing = Application.Current;
        if (existing is null || existing.Dispatcher.CheckAccess())
        {
            return TestApplication.Ensure(uris);
        }

        var merged = existing.Resources.MergedDictionaries;
        foreach (var source in uris)
        {
            if (merged.All(dictionary => dictionary.Source != source))
            {
                merged.Add(new ResourceDictionary { Source = source });
            }
        }

        ScreenGuard.Install();
        return existing;
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
