using PoproshaykaBot.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Views;

public partial class CommandsPageView : IView<CommandsPageViewModel>
{
    private CommandsPageViewModel? _viewModel;

    public CommandsPageView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.SetUsagePollingActive(false);
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as CommandsPageViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            if (IsLoaded && IsVisible)
            {
                _viewModel.SetUsagePollingActive(true);
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.SetUsagePollingActive(IsLoaded && IsVisible);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.SetUsagePollingActive(false);
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _viewModel?.SetUsagePollingActive(IsLoaded && IsVisible);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(CommandsPageViewModel.Notice), StringComparison.Ordinal)
            || string.IsNullOrEmpty(_viewModel?.Notice))
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(AnnounceNotice));
    }

    private void AnnounceNotice()
    {
        var peer = UIElementAutomationPeer.FromElement(NoticeText)
            ?? UIElementAutomationPeer.CreatePeerForElement(NoticeText);

        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
}
