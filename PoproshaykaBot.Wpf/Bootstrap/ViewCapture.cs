using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PoproshaykaBot.Wpf.Bootstrap;

public static class ViewCapture
{
    private const double BaseDpi = 96;

    public static FrameworkElement? Find(DependencyObject root, string name)
    {
        if (root is FrameworkElement element && string.Equals(element.Name, name, StringComparison.OrdinalIgnoreCase))
        {
            return element;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            if (Find(VisualTreeHelper.GetChild(root, index), name) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public static (int Width, int Height) Save(FrameworkElement element, string path, double scale = 1)
    {
        scale = Math.Clamp(scale, AppDefaults.ViewCaptureScaleMin, AppDefaults.ViewCaptureScaleMax);
        var size = new Size(element.ActualWidth, element.ActualHeight);
        var width = (int)Math.Ceiling(size.Width * scale);
        var height = (int)Math.Ceiling(size.Height * scale);

        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(element), null, new(size));
        }

        var dpi = BaseDpi * scale;
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? throw new ArgumentException("Путь кадра без каталога", nameof(path)));

        using var stream = File.Create(path);
        encoder.Save(stream);

        return (width, height);
    }

    internal static string Slug(string label)
    {
        var builder = new StringBuilder(label.Length);

        foreach (var symbol in label.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(symbol))
            {
                builder.Append(symbol);
                continue;
            }

            if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-') is { Length: > 0 } slug ? slug : "view";
    }
}
