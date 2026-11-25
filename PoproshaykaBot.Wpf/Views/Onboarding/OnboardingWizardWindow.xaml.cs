using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using System.ComponentModel;
using System.Windows;
using PoproshaykaBot.Core.Settings.Onboarding;

namespace PoproshaykaBot.Wpf.Views.Onboarding;

public partial class OnboardingWizardWindow : Window
{
    private readonly OnboardingWizardViewModel _viewModel;
    private bool _closeConfirmed;
    private bool _closing;

    public OnboardingWizardWindow(OnboardingWizardViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        WindowChromeTheming.Attach(this);

        _viewModel.CloseRequested += OnCloseRequested;
        Closing += OnClosing;
    }

    internal void StartAt(OnboardingStep step) => _viewModel.StartAt(step);

    private void OnCloseRequested()
    {
        if (_closeConfirmed)
        {
            return;
        }

        Close();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed)
        {
            _viewModel.CloseRequested -= OnCloseRequested;
            return;
        }

        e.Cancel = true;

        if (_closing || _viewModel.IsBusy)
        {
            return;
        }

        _closing = true;
        if (await _viewModel.RequestCloseAsync())
        {
            _closeConfirmed = true;
            _ = Dispatcher.BeginInvoke(() => Close());
        }
        else
        {
            _closing = false;
        }
    }
}
