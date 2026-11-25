namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed class AppearanceSettingsSectionViewModel
{
    public AppearanceSettingsSectionViewModel(
        ThemeSelectionViewModel theme,
        ShellPreferences shell,
        ThemePacksViewModel? themePacks = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(shell);

        Theme = theme;
        Shell = shell;
        ThemePacks = themePacks;
    }

    public ThemeSelectionViewModel Theme { get; }

    public ShellPreferences Shell { get; }

    public ThemePacksViewModel? ThemePacks { get; }
}
