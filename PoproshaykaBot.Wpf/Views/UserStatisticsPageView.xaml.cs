using KeepShell.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Views;

public partial class UserStatisticsPageView : IView<UserStatisticsPageViewModel>
{
    public static readonly DependencyProperty ShowsRankColumnProperty = DependencyProperty.Register(
        nameof(ShowsRankColumn),
        typeof(bool),
        typeof(UserStatisticsPageView),
        new PropertyMetadata(true));

    public const double RankColumnListWidth = 430;

    private double _listWidth;

    public UserStatisticsPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        RatingList.SizeChanged += OnRatingListSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public bool ShowsRankColumn
    {
        get => (bool)GetValue(ShowsRankColumnProperty);
        set => SetValue(ShowsRankColumnProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
        FontScaleManager.Changed += OnFontScaleChanged;
        ApplyRankColumn();
        ScrollToSelected();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
    }

    private void OnFontScaleChanged(object? sender, double scale)
    {
        Dispatcher.BeginInvoke(ApplyRankColumn);
    }

    private void OnRatingListSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _listWidth = e.NewSize.Width;
        ApplyRankColumn();
    }

    private void ApplyRankColumn()
    {
        if (_listWidth <= 0)
        {
            return;
        }

        ShowsRankColumn = _listWidth >= RankColumnListWidth * FontScaleManager.Current;
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
