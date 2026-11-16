using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class MultiplatformChatTileView : UserControl, IView<MultiplatformChatTileViewModel>
{
    private const double BottomSnapThresholdDip = 32.0;

    private MultiplatformChatTileViewModel? _viewModel;
    private ScrollViewer? _scrollViewer;
    private bool _programmaticScroll;

    public MultiplatformChatTileView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer ??= FindScrollViewer(FeedListBox);

        if (_viewModel is not null)
        {
            _viewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
            _viewModel.ScrollToBottomRequested += OnScrollToBottomRequested;

            if (_viewModel.IsPinnedToBottom)
            {
                ScrollToBottom();
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
        }
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
        }

        _viewModel = DataContext as MultiplatformChatTileViewModel;

        if (_viewModel is not null)
        {
            _viewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
            _viewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
        }
    }

    private void OnScrollToBottomRequested(object? sender, EventArgs e)
    {
        ScrollToBottom();
    }

    private void ScrollToBottom()
    {
        _scrollViewer ??= FindScrollViewer(FeedListBox);

        if (_scrollViewer is null)
        {
            return;
        }

        _programmaticScroll = true;
        try
        {
            _scrollViewer.ScrollToEnd();
        }
        finally
        {
            _programmaticScroll = false;
        }
    }

    private void OnFeedScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _scrollViewer ??= FindScrollViewer(FeedListBox);

        if (_scrollViewer is null)
        {
            return;
        }

        if (e.ExtentHeightChange > 0 && _viewModel.IsPinnedToBottom)
        {
            ScrollToBottom();
            return;
        }

        if (_programmaticScroll)
        {
            return;
        }

        var distanceFromBottom = _scrollViewer.ScrollableHeight - _scrollViewer.VerticalOffset;
        _viewModel.UpdateScrollPosition(distanceFromBottom <= BottomSnapThresholdDip);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
