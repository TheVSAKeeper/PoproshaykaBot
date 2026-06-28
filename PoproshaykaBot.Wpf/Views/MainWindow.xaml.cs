using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace PoproshaykaBot.Wpf.Views;

public partial class MainWindow : Window
{
    private static readonly WindowPlacementKeys WindowKeys = new(
        SettingsKeys.WindowLeft, SettingsKeys.WindowTop, SettingsKeys.WindowWidth,
        SettingsKeys.WindowHeight, SettingsKeys.WindowMaximized);

    private readonly ShellViewModel _viewModel;
    private readonly ISettingsStore _settings;
    private bool _closeConfirmed;

    public MainWindow(ShellViewModel viewModel, ISettingsStore settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;

        WindowChromeTheming.Attach(this);

        Closing += OnClosing;

        WindowPlacement.Restore(this, _settings, WindowKeys);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            return;
        }

        e.Cancel = true;

        if (await _viewModel.RequestCloseAsync())
        {
            _closeConfirmed = true;
            WindowPlacement.Save(this, _settings, WindowKeys);
            Dispatcher.BeginInvoke(() => Close());
        }
    }
}
