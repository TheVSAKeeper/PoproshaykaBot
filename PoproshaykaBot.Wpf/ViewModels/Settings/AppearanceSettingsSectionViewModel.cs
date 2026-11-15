namespace PoproshaykaBot.Wpf.ViewModels.Settings;

public sealed class AppearanceSettingsSectionViewModel
{
    public AppearanceSettingsSectionViewModel(ThemeViewModel theme, ShellPreferences shell)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(shell);

        Theme = theme;
        Shell = shell;
    }

    public ThemeViewModel Theme { get; }

    public ShellPreferences Shell { get; }
}
