using System.Windows;

namespace PoproshaykaBot.Wpf.Infrastructure;

public interface IMainWindowController
{
    void RequestClose();
}

internal sealed class MainWindowController : IMainWindowController
{
    public void RequestClose() => Application.Current?.MainWindow?.Close();
}
