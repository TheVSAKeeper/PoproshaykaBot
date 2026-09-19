using PoproshaykaBot.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Views;

public partial class UserStatisticsPageView : IView<UserStatisticsPageViewModel>
{
    public UserStatisticsPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScrollToSelected();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is UserStatisticsPageViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is UserStatisticsPageViewModel newViewModel)
        {
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UserStatisticsPageViewModel.SelectedRow))
        {
            return;
        }

        ScrollToSelected();
    }

    private void ScrollToSelected()
    {
        if (DataContext is not UserStatisticsPageViewModel { SelectedRow: { } row })
        {
            return;
        }

        Dispatcher.InvokeAsync(() => RatingList.ScrollIntoView(row), DispatcherPriority.Loaded);
    }
}
