using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.Views;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Bootstrap;

internal sealed record GalleryFrame(string Page, string Theme, string File, int Width, int Height);

internal sealed record GalleryIndex(
    string App,
    string Version,
    int Width,
    int Height,
    double Scale,
    double FontScale,
    IReadOnlyList<string> Unknown,
    IReadOnlyList<GalleryFrame> Frames);

public static class GalleryRun
{
    public const string ArgumentName = "--gallery";
    public const string FolderName = "gallery";
    public const string IndexFileName = "index.json";

    private const double OffScreen = -32000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static async Task<int> RenderAsync(GalleryOptions options, IServiceProvider services)
    {
        var window = services.GetRequiredService<MainWindow>();
        var shell = services.GetRequiredService<ShellViewModel>();

        Directory.CreateDirectory(options.Directory);
        FontScaleManager.Apply(options.FontScale);

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = OffScreen;
        window.Top = OffScreen;
        window.Width = options.Width;
        window.Height = options.Height;
        window.ShowInTaskbar = false;
        window.Show();

        var frames = new List<GalleryFrame>();
        var skipped = 0;

        foreach (var theme in options.Themes)
        {
            ThemeManager.Apply(AppThemes.ToKey(theme));
            await Task.Delay(AppDefaults.GalleryThemeDelayMs).ConfigureAwait(true);

            foreach (var page in options.Pages)
            {
                var frame = await CaptureAsync(window, shell, options, page, AppThemes.ToKey(theme)).ConfigureAwait(true);

                if (frame is null)
                {
                    skipped++;
                    continue;
                }

                frames.Add(frame);
            }
        }

        WriteIndex(options, frames);
        window.Hide();

        return skipped == 0 && frames.Count > 0 ? 0 : 1;
    }

    private static async Task<GalleryFrame?> CaptureAsync(
        MainWindow window,
        ShellViewModel shell,
        GalleryOptions options,
        string page,
        string themeKey)
    {
        if (!Navigate(shell, page))
        {
            return null;
        }

        await DrainAsync().ConfigureAwait(true);
        await Task.Delay(AppDefaults.GalleryPageDelayMs).ConfigureAwait(true);
        await DrainAsync().ConfigureAwait(true);

        FrameworkElement target = window;

        if (!string.IsNullOrEmpty(options.Element))
        {
            if (ViewCapture.Find(window, options.Element) is not { } element)
            {
                return null;
            }

            target = element;
        }

        var file = $"{ViewCapture.Slug(page)}-{themeKey}.png";
        var (width, height) = ViewCapture.Save(target, Path.Combine(options.Directory, file), options.Scale);

        return width > 0 && height > 0 ? new(page, themeKey, file, width, height) : null;
    }

    private static bool Navigate(ShellViewModel shell, string page)
    {
        // Настройки открываются своей командой: пункта в Sections у них нет, а ActivateSettings
        // должен отработать так же, как при клике человека.
        if (string.Equals(page, SectionKeys.Settings, StringComparison.OrdinalIgnoreCase))
        {
            shell.OpenSettingsCommand.Execute(null);

            return true;
        }

        if (shell.FindSectionByKey(page) is not { } section)
        {
            return false;
        }

        shell.Selected = section;

        return true;
    }

    private static Task DrainAsync()
    {
        return Application.Current.Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle).Task;
    }

    private static void WriteIndex(GalleryOptions options, IReadOnlyList<GalleryFrame> frames)
    {
        var index = new GalleryIndex(
            AppInfo.Name,
            AppInfo.Version,
            options.Width,
            options.Height,
            options.Scale,
            options.FontScale,
            options.Unknown,
            frames);

        File.WriteAllText(Path.Combine(options.Directory, IndexFileName), JsonSerializer.Serialize(index, JsonOptions));
    }
}
