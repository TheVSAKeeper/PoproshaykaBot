using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.Views;
using System.Windows;

namespace PoproshaykaBot.Wpf.Bootstrap;

public sealed class GalleryHost : IGalleryHost
{
    public const string ArgumentName = "--gallery";

    private readonly ShellViewModel _shell;
    private readonly Window _window;

    private GalleryHost(IServiceProvider services, IReadOnlyList<string> pages)
    {
        _shell = services.GetRequiredService<ShellViewModel>();
        _window = services.GetRequiredService<MainWindow>();

        Cases = [.. pages.Select(page => new GalleryCase(GalleryCase.PageKind, page))];
    }

    public string AppName => AppInfo.Name;

    public string AppVersion => AppInfo.Version;

    public Window Window => _window;

    public IReadOnlyList<GalleryCase> Cases { get; }

    public static GalleryArguments Parse(IEnumerable<string> args, string defaultDirectory)
    {
        var defaults = new GalleryDefaults
        {
            Directory = defaultDirectory,
            Themes = [AppThemes.LightKey, AppThemes.DarkKey],
            Width = AppDefaults.GalleryWidthDefault,
            Height = AppDefaults.GalleryHeightDefault,
            ThemeDelayMs = AppDefaults.GalleryThemeDelayMs,
            FrameDelayMs = AppDefaults.GalleryPageDelayMs,
        };

        return GalleryArguments.Parse(args, defaults, ResolvePages);
    }

    public static GalleryHost Create(IServiceProvider services, GalleryArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return new(services, arguments.Pages);
    }

    public async Task<GalleryShot> CaptureAsync(GalleryCase item, GalleryContext context)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        Navigate(item.Name);

        await context.SettleAsync().ConfigureAwait(true);

        return context.Save(ViewCapture.Slug(item.Name));
    }

    private static IReadOnlyList<string> ResolvePages(IReadOnlyList<string> requested, ICollection<string> unknown)
    {
        if (requested.Count == 0)
        {
            return SectionKeys.All;
        }

        var pages = new List<string>();

        foreach (var page in requested)
        {
            var match = SectionKeys.All.FirstOrDefault(known => string.Equals(known, page, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                unknown.Add(page);
                continue;
            }

            if (!pages.Contains(match, StringComparer.Ordinal))
            {
                pages.Add(match);
            }
        }

        return pages.Count > 0 ? pages : SectionKeys.All;
    }

    private void Navigate(string page)
    {
        if (string.Equals(page, SectionKeys.Settings, StringComparison.OrdinalIgnoreCase))
        {
            _shell.OpenSettingsCommand.Execute(null);

            return;
        }

        if (_shell.FindSectionByKey(page) is not { } section)
        {
            throw new InvalidOperationException($"Страница «{page}» не открылась – её нет в навигации.");
        }

        _shell.Selected = section;
    }
}
