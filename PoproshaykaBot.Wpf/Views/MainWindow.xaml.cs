using PoproshaykaBot.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace PoproshaykaBot.Wpf.Views;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        WindowChromeTheming.Attach(this, enableBackdrop: true);

        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_viewModel.RequestClose())
        {
            e.Cancel = true;
        }
    }
}
