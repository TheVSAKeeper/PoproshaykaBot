namespace PoproshaykaBot.Wpf.Bootstrap;

public static class SettingsKeys
{
    public const string ShowPageHeader = "ui.shell.show_page_header";
    public const string FontScale = "ui.shell.font_scale";
    public const string EnableToastNotifications = "ui.notifications.toast";
    public const string NavCollapsed = "ui.shell.nav_collapsed";
    public const string LastPage = "ui.shell.last_page";

    public const string WindowLeft = "ui.window.left";
    public const string WindowTop = "ui.window.top";
    public const string WindowWidth = "ui.window.width";
    public const string WindowHeight = "ui.window.height";
    public const string WindowMaximized = "ui.window.maximized";

    public static string Theme => ThemeManager.SettingsKeyName;
}
