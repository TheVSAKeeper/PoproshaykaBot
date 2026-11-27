using PoproshaykaBot.Wpf.ViewModels.Migration;
using System.ComponentModel;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Migration;

public partial class FirstRunStartView : UserControl, IView<FirstRunStartViewModel>
{
    private FirstRunStartViewModel? _subscribed;

    public FirstRunStartView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribed = null;
        }

        if (e.NewValue is FirstRunStartViewModel viewModel)
        {
            _subscribed = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FirstRunStartViewModel.CurrentStep))
        {
            ContentScroll.ScrollToTop();
        }
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_subscribed is not null)
        {
            _subscribed.PropertyChanged -= OnViewModelPropertyChanged;
            _subscribed = null;
        }
    }
}
