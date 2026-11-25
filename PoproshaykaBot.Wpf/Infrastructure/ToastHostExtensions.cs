using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Infrastructure;

internal static class ToastHostExtensions
{
    public static ToastViewModel? ShowIfEnabled(
        this ToastHostViewModel? toasts,
        ShellPreferences? preferences,
        string message,
        StatusSeverity severity = StatusSeverity.Info,
        TimeSpan? duration = null)
    {
        if (toasts is null || preferences is { EnableToastNotifications: false })
        {
            return null;
        }

        return toasts.Show(message, severity, duration);
    }
}
