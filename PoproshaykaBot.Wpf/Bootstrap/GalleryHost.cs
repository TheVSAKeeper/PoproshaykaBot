using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Statistics;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.Views;
using Serilog;
using System.Windows;

namespace PoproshaykaBot.Wpf.Bootstrap;

public sealed class GalleryHost : IGalleryHost
{
    public const string ArgumentName = "--gallery";

    private readonly IServiceProvider _services;
    private readonly ShellViewModel _shell;
    private readonly Window _window;

    private GalleryHost(IServiceProvider services, IReadOnlyList<string> pages)
    {
        _services = services;
        _shell = services.GetRequiredService<ShellViewModel>();
        _window = services.GetRequiredService<MainWindow>();

        Cases = [.. pages.Select(page => new GalleryCase(GalleryDialogs.IsDialogKey(page) ? GalleryDialogs.Kind : GalleryCase.PageKind, page))];
    }

    public string AppName => AppInfo.Name;

    public string AppVersion => AppInfo.Version;

    public Window Window => _window;

    public IReadOnlyList<GalleryCase> Cases { get; }

    private static Serilog.ILogger HostLog => Log.ForContext<GalleryHost>();

    public static GalleryArguments Parse(IEnumerable<string> args, string defaultDirectory)
    {
        var defaults = new GalleryDefaults
        {
            Directory = defaultDirectory,
            Themes = [AppThemes.LightKey, AppThemes.DarkKey],
            Width = AppDefaults.GalleryWidthDefault,
            Height = AppDefaults.GalleryHeightDefault,
        };

        return GalleryArguments.Parse(args, defaults, ResolvePages);
    }

    public static GalleryHost Create(IServiceProvider services, GalleryArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return new(services, arguments.Pages);
    }

    public async Task ArrangeAsync()
    {
        try
        {
            var fileStore = _services.GetRequiredService<StatisticsFileStore>();
            var users = await fileStore.LoadUsersAsync().ConfigureAwait(true);
            var bot = await fileStore.LoadBotAsync().ConfigureAwait(true);

            _services.GetRequiredService<IUserStatisticsRepository>().ReplaceAll(users);

            if (bot is not null)
            {
                _services.GetRequiredService<IBotStatisticsRepository>().Replace(bot);
            }

            _services.GetRequiredService<UserStatisticsPageViewModel>().RefreshCommand.Execute(null);
        }
        catch (Exception exception)
        {
            HostLog.Warning(exception, "Статистика профиля не прочитана – страница пользователей останется пустой");
        }
    }

    public async Task<GalleryShot> CaptureAsync(GalleryCase item, GalleryContext context)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(context);

        if (GalleryDialogs.Find(item.Name) is { } dialog)
        {
            return await CaptureDialogAsync(dialog, context).ConfigureAwait(true);
        }

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
            var match = Canonical(page);

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

    private static string? Canonical(string requested)
    {
        if (GalleryDialogs.IsDialogKey(requested))
        {
            return GalleryDialogs.Find(requested)?.Key;
        }

        return SectionKeys.All.FirstOrDefault(known => string.Equals(known, requested, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<GalleryShot> CaptureDialogAsync(GalleryDialogCase item, GalleryContext context)
    {
        Navigate(item.Page);

        if (item.SettingsSection is { } section)
        {
            _services.GetRequiredService<SettingsPageViewModel>().Sections.Restore(section);
        }

        using var scope = _services.CreateScope();
        var dialog = item.Create(scope.ServiceProvider);

        try
        {
            _ = _shell.Modal.ShowAsync(dialog);

            await context.SettleAsync().ConfigureAwait(true);

            return context.Save(ViewCapture.Slug(item.Key));
        }
        finally
        {
            _shell.Modal.RequestCancel();

            await context.SettleAsync().ConfigureAwait(true);
        }
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
