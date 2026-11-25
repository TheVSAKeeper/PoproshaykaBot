using System.Linq;
using System.Windows;

namespace PoproshaykaBot.Wpf.Infrastructure;

public static class DialogOwner
{
    public static Window? Resolve()
    {
        var app = Application.Current;
        if (app is null || !app.Dispatcher.CheckAccess())
        {
            return null;
        }

        var windows = app.Windows.OfType<Window>().ToList();

        return windows.FirstOrDefault(window => window.IsActive)
               ?? windows.LastOrDefault(window => window.IsVisible)
               ?? app.MainWindow;
    }
}
