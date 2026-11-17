using KeepShell.Bootstrap;
using PoproshaykaBot.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Views;

public partial class UserStatisticsPageView : IView<UserStatisticsPageViewModel>
{
    public static readonly DependencyProperty ShowsRankColumnProperty = DependencyProperty.Register(
        nameof(ShowsRankColumn),
        typeof(bool),
        typeof(UserStatisticsPageView),
        new PropertyMetadata(true));

    public static readonly DependencyProperty IsCompactFilterBarProperty = DependencyProperty.Register(
        nameof(IsCompactFilterBar),
        typeof(bool),
        typeof(UserStatisticsPageView),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsInspectorScrollCombinedProperty = DependencyProperty.Register(
        nameof(IsInspectorScrollCombined),
        typeof(bool),
        typeof(UserStatisticsPageView),
        new PropertyMetadata(false));

    public const double RankColumnListWidth = 464;
    public const double SummaryCompactHeight = 460;
    public const double InspectorCombinedScrollHeight = 500;
    public const double ToolbarWrapWidth = 370;

    private double _listWidth;
    private string? _announcedAdjustmentError;
    private UserStatisticsPageViewModel? _viewModel;

    public UserStatisticsPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
        RatingList.SizeChanged += OnRatingListSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public bool ShowsRankColumn
    {
        get => (bool)GetValue(ShowsRankColumnProperty);
        set => SetValue(ShowsRankColumnProperty, value);
    }

    public bool IsCompactFilterBar
    {
        get => (bool)GetValue(IsCompactFilterBarProperty);
        set => SetValue(IsCompactFilterBarProperty, value);
    }

    public bool IsInspectorScrollCombined
    {
        get => (bool)GetValue(IsInspectorScrollCombinedProperty);
        set => SetValue(IsInspectorScrollCombinedProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
        FontScaleManager.Changed += OnFontScaleChanged;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyRankColumn();
        ApplyResponsiveLayout();
        ScrollToSelected();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }
    }

    private void OnFontScaleChanged(object? sender, double scale)
    {
        _ = Dispatcher.BeginInvoke(() =>
        {
            ApplyRankColumn();
            ApplyResponsiveLayout();
        });
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout();
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

    private void ApplyResponsiveLayout()
    {
        var scale = FontScaleManager.Current;
        var filterWidth = FilterBar.ActualWidth > 0 ? FilterBar.ActualWidth : ActualWidth;

        if (filterWidth > 0)
        {
            IsCompactFilterBar = filterWidth < ToolbarWrapWidth * scale;
        }

        if (ActualHeight > 0)
        {
            IsInspectorScrollCombined = ActualHeight < InspectorCombinedScrollHeight;

            if (DataContext is UserStatisticsPageViewModel viewModel)
            {
                viewModel.IsSummaryHeightConstrained = ActualHeight < SummaryCompactHeight * scale;
            }
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as UserStatisticsPageViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyResponsiveLayout();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UserStatisticsPageViewModel.AdjustmentError))
        {
            AnnounceAdjustmentErrorIfChanged((sender as UserStatisticsPageViewModel)?.AdjustmentError);
            return;
        }

        if (e.PropertyName != nameof(UserStatisticsPageViewModel.SelectedRow))
        {
            return;
        }

        ScrollToSelected();
    }

    private void AnnounceAdjustmentErrorIfChanged(string? error)
    {
        if (string.Equals(error, _announcedAdjustmentError, StringComparison.Ordinal))
        {
            return;
        }

        _announcedAdjustmentError = error;

        if (error is null)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var peer = UIElementAutomationPeer.FromElement(AdjustmentErrorText)
                ?? UIElementAutomationPeer.CreatePeerForElement(AdjustmentErrorText);

            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }));
    }

    private void OnRowAvatarDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is UserAvatarViewModel avatar && DataContext is UserStatisticsPageViewModel page)
        {
            page.RequestAvatar(avatar);
        }
    }

    private void ScrollToSelected()
    {
        if (DataContext is not UserStatisticsPageViewModel { SelectedRow: { } row })
        {
            return;
        }

        _ = Dispatcher.InvokeAsync(() => RatingList.ScrollIntoView(row), DispatcherPriority.Loaded);
    }
}
