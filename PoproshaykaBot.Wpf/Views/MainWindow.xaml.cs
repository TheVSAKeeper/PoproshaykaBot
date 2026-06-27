using PoproshaykaBot.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace PoproshaykaBot.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;
    private bool _closeConfirmed;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        WindowChromeTheming.Attach(this, true);

        Closing += OnClosing;
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
            Dispatcher.BeginInvoke(() => Close());
        }
    }
}
