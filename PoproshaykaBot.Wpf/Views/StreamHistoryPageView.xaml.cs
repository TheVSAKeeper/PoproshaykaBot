using PoproshaykaBot.Wpf.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PoproshaykaBot.Wpf.Views;

public partial class StreamHistoryPageView : UserControl, IView<StreamHistoryPageViewModel>
{
    public const double SideBySideWidth = 1240;
    public const double StackedWidth = 1200;
    public const double DetailMinWidth = 360;
    public const double TableMinWidth = 720;
    public const double DetailSplitWidth = 710;
    public const double DetailStackWidth = 670;
    public const double ChattersMinWidth = 240;
    public const double TrendStripHeight = 40;
    public const double TrendStripCompactHeight = 36;

    private bool _sideBySide;
    private bool _layoutApplied;
    private bool _detailStacked;
    private bool _detailApplied;

    public StreamHistoryPageView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
        DetailSplit.SizeChanged += OnDetailSplitSizeChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private static void Place(UIElement element, int row, int column, int rowSpan = 1, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetRowSpan(element, rowSpan);
        Grid.SetColumnSpan(element, columnSpan);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
        FontScaleManager.Changed += OnFontScaleChanged;
        _layoutApplied = false;
        UpdateLayoutMode();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
    }

    private void OnFontScaleChanged(object? sender, double scale)
    {
        _layoutApplied = false;
        _detailApplied = false;
        UpdateLayoutMode();
        UpdateDetailMode();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateLayoutMode();
    }

    private void OnDetailSplitSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateDetailMode();
    }

    private void UpdateDetailMode()
    {
        var width = DetailSplit.ActualWidth;

        if (width <= 0)
        {
            return;
        }

        var scale = FontScaleManager.Current;

        var stacked = _detailStacked
            ? width < DetailSplitWidth * scale
            : width < DetailStackWidth * scale;

        if (_detailApplied && stacked == _detailStacked)
        {
            return;
        }

        _detailStacked = stacked;
        _detailApplied = true;

        var numbers = stacked ? Visibility.Collapsed : Visibility.Visible;

        SegmentMessagesColumn.Visibility = numbers;
        SegmentPeakColumn.Visibility = numbers;
        SegmentAverageColumn.Visibility = numbers;

        DetailSplit.ColumnDefinitions.Clear();
        DetailSplit.RowDefinitions.Clear();

        if (stacked)
        {
            DetailSplit.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star), MinHeight = 96 });
            DetailSplit.RowDefinitions.Add(new() { Height = new(12, GridUnitType.Pixel) });
            DetailSplit.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star), MinHeight = 120 });

            Place(SegmentsPane, 0, 0);
            Place(DetailSplitter, 1, 0);
            Place(ChattersPane, 2, 0);

            DetailSplitter.ResizeDirection = GridResizeDirection.Rows;
        }
        else
        {
            DetailSplit.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star), MinWidth = 220 * scale });
            DetailSplit.ColumnDefinitions.Add(new() { Width = new(12, GridUnitType.Pixel) });
            DetailSplit.ColumnDefinitions.Add(new()
            {
                Width = GridLength.Auto,
                MinWidth = ChattersMinWidth * scale,
                MaxWidth = 360 * scale,
            });

            Place(SegmentsPane, 0, 0);
            Place(DetailSplitter, 0, 1);
            Place(ChattersPane, 0, 2);

            DetailSplitter.ResizeDirection = GridResizeDirection.Columns;
        }
    }

    private void UpdateLayoutMode()
    {
        var width = ActualWidth;

        if (width <= 0)
        {
            return;
        }

        var scale = FontScaleManager.Current;

        var sideBySide = _sideBySide
            ? width >= StackedWidth * scale
            : width >= SideBySideWidth * scale;

        if (_layoutApplied && sideBySide == _sideBySide)
        {
            return;
        }

        _sideBySide = sideBySide;
        _layoutApplied = true;

        ApplyCompactMode(sideBySide is false, scale);

        if (sideBySide)
        {
            ApplySideBySide(scale);
        }
        else
        {
            ApplyStacked();
        }
    }

    private void ApplyCompactMode(bool compact, double scale)
    {
        TrendStrip.Height = (compact ? TrendStripCompactHeight : TrendStripHeight) * scale;
        TrendStrip.Margin = new(0, 0, 0, compact ? 6 : 0);

        if (DataContext is StreamHistoryPageViewModel viewModel)
        {
            viewModel.IsCompactLayout = compact;
        }
    }

    private void ApplyStacked()
    {
        PageGrid.ColumnDefinitions.Clear();
        PageGrid.RowDefinitions.Clear();

        PageGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        PageGrid.RowDefinitions.Add(new() { Height = new(3, GridUnitType.Star), MinHeight = 160 });
        PageGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        PageGrid.RowDefinitions.Add(new() { Height = new(2, GridUnitType.Star), MinHeight = 200 });

        Place(HeaderStack, 0, 0);
        Place(TableCard, 1, 0);
        Place(LayoutSplitter, 2, 0);
        Place(DetailEmpty, 3, 0);
        Place(DetailCard, 3, 0);
        Place(HistoryEmpty, 1, 0, rowSpan: 3);

        LayoutSplitter.ResizeDirection = GridResizeDirection.Rows;
        LayoutSplitter.Width = double.NaN;
        LayoutSplitter.Height = 8;
    }

    private void ApplySideBySide(double scale)
    {
        PageGrid.ColumnDefinitions.Clear();
        PageGrid.RowDefinitions.Clear();

        PageGrid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        PageGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });

        PageGrid.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star), MinWidth = TableMinWidth * scale });
        PageGrid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        PageGrid.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star), MinWidth = DetailMinWidth * scale });

        Place(HeaderStack, 0, 0, columnSpan: 3);
        Place(TableCard, 1, 0);
        Place(LayoutSplitter, 1, 1);
        Place(DetailEmpty, 1, 2);
        Place(DetailCard, 1, 2);
        Place(HistoryEmpty, 1, 0, columnSpan: 3);

        LayoutSplitter.ResizeDirection = GridResizeDirection.Columns;
        LayoutSplitter.ClearValue(HeightProperty);
        LayoutSplitter.ClearValue(WidthProperty);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is StreamHistoryPageViewModel oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is StreamHistoryPageViewModel newViewModel)
        {
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
            _layoutApplied = false;
            UpdateLayoutMode();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StreamHistoryPageViewModel.SelectedRow))
        {
            return;
        }

        if (sender is StreamHistoryPageViewModel { SelectedRow: { } row })
        {
            SessionsGrid.ScrollIntoView(row);
        }
    }

    private void OnTrendMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnSegmentRowActivated(object sender, MouseButtonEventArgs e)
    {
        FilterBySegment(sender);
    }

    private void OnSegmentRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        if (FilterBySegment(sender))
        {
            e.Handled = true;
        }
    }

    private bool FilterBySegment(object sender)
    {
        if (sender is not DataGridRow { Item: StreamSessionSegmentRowViewModel segment }
            || DataContext is not StreamHistoryPageViewModel viewModel
            || !segment.CanFilter)
        {
            return false;
        }

        viewModel.FilterByGameCommand.Execute(segment);

        return true;
    }

    private void OnCategoryRowActivated(object sender, MouseButtonEventArgs e)
    {
        FilterByCategory(sender);
    }

    private void OnCategoryRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        if (FilterByCategory(sender))
        {
            e.Handled = true;
        }
    }

    private bool FilterByCategory(object sender)
    {
        if (sender is not ListBoxItem { DataContext: StreamCategoryRowViewModel category }
            || DataContext is not StreamHistoryPageViewModel viewModel)
        {
            return false;
        }

        viewModel.FilterByGameCommand.Execute(category);

        return true;
    }

    private void OnChatterRowActivated(object sender, MouseButtonEventArgs e)
    {
        OpenChatter(sender);
    }

    private void OnChatterRowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space))
        {
            return;
        }

        if (OpenChatter(sender))
        {
            e.Handled = true;
        }
    }

    private bool OpenChatter(object sender)
    {
        if (sender is not ListBoxItem { DataContext: StreamSessionChatterRowViewModel chatter }
            || DataContext is not StreamHistoryPageViewModel viewModel
            || !chatter.CanOpen)
        {
            return false;
        }

        viewModel.OpenChatterCommand.Execute(chatter);

        return true;
    }
}
