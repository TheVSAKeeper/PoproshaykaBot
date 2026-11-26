namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed class AppearanceSettingsSectionViewModel
{
    public AppearanceSettingsSectionViewModel(
        ThemeSelectionViewModel theme,
        ShellPreferences shell,
        ThemePacksViewModel? themePacks = null,
        bool showChromeAndNotificationOptions = true)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(shell);

        Theme = theme;
        Shell = shell;
        ThemePacks = themePacks;
        ShowChromeAndNotificationOptions = showChromeAndNotificationOptions;
    }

    public ThemeSelectionViewModel Theme { get; }

    public ShellPreferences Shell { get; }

    public ThemePacksViewModel? ThemePacks { get; }

    public bool ShowChromeAndNotificationOptions { get; }
}
