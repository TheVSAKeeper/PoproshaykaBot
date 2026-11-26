using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
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
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
        };
    }

    internal void StartAt(OnboardingStep step) => _viewModel.StartAt(step);

    internal void AttachStartupSession(StartupOnboardingSession session) => _viewModel.AttachStartupSession(session);

    internal void ConfigureWindowPlacement(Window? owner)
    {
        Owner = owner;
        ShowInTaskbar = owner is null;
        WindowStartupLocation = owner is null
            ? WindowStartupLocation.CenterScreen
            : WindowStartupLocation.CenterOwner;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScheduleFocusIntoCurrentStep();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(OnboardingWizardViewModel.CurrentPage))
        {
            ScheduleFocusIntoCurrentStep();
        }
    }

    private void ScheduleFocusIntoCurrentStep()
    {
        if (_closeConfirmed || !_viewModel.IsPageInteractive)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (_closeConfirmed || !_viewModel.IsPageInteractive || !IsLoaded)
            {
                return;
            }

            Keyboard.Focus(PageHeaderTitle);
        });
    }

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
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.Dispose();
            return;
        }

        e.Cancel = true;

        if (_closing || _viewModel.IsOperationBusy)
        {
            return;
        }

        _closing = true;
        if (await _viewModel.RequestCloseAsync(confirmIfModified: true))
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
