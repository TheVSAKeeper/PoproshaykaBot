using KeepShell.Bootstrap;
using MahApps.Metro.IconPacks;
using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Views.Settings;

public partial class DashboardLayoutSectionView : UserControl, IView<DashboardLayoutSectionViewModel>
{
    private const string DragFormat = "DashboardTileTypeId";
    private const double MaxPreviewHeight = 420;
    private const double MiniatureFontSize = 11;
    private const double MiniatureIconSize = 14;
    private const double MiniaturePadding = 8;
    private const double MiniatureLineHeight = 6;
    private const double DropFillOpacity = 0.35;

    private readonly Dictionary<FrameworkElement, int[]> _panePaths = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DashboardTileViewModel, FrameworkElement> _tileHosts = [];
    private readonly Dictionary<string, int[]> _tilePaths = new(StringComparer.Ordinal);

    private DashboardLayoutSectionViewModel? _viewModel;
    private Border? _dropHint;
    private Border? _dropFill;
    private double _scale = 1;
    private double _builtWidth;

    public DashboardLayoutSectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
        PreviewArea.SizeChanged += OnPreviewAreaSizeChanged;
        PreviewArea.LayoutUpdated += OnPreviewAreaLayoutUpdated;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LayoutChanged -= OnLayoutChanged;
        }

        _viewModel = e.NewValue as DashboardLayoutSectionViewModel;

        if (_viewModel is not null)
        {
            _viewModel.LayoutChanged += OnLayoutChanged;
        }

        RebuildPreview();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.LayoutChanged -= OnLayoutChanged;
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        RebuildPreview();
    }

    private void OnPreviewAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RebuildPreview();
    }

    private void OnPreviewAreaLayoutUpdated(object? sender, EventArgs e)
    {
        if (Math.Abs(PreviewArea.ActualWidth - _builtWidth) > 0.5)
        {
            RebuildPreview();

            return;
        }

        if (_viewModel is null || PreviewBox.Child is not FrameworkElement { ActualWidth: > 0 } canvas)
        {
            return;
        }

        var actual = PreviewBox.ActualWidth / canvas.ActualWidth;
        var drifted = Math.Abs(actual - _scale) > 0.05;

        _scale = actual;
        _viewModel.Scale = actual;

        if (drifted)
        {
            RebuildMiniatures();
        }
    }

    private void RebuildMiniatures()
    {
        var area = _viewModel!.ContentArea;

        HideDropHint();
        _panePaths.Clear();
        _tilePaths.Clear();
        _tileHosts.Clear();

        var canvas = new Grid
        {
            Width = area.Width,
            Height = area.Height,
        };

        canvas.Children.Add(WrapOverflow(BuildContent(_viewModel), area));

        PreviewBox.Child = canvas;
    }

    private void OnPaletteMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: string typeId } button)
        {
            DragDrop.DoDragDrop(button, new DataObject(DragFormat, typeId), DragDropEffects.Copy);
        }
    }

    private void OnMiniatureMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string typeId } border)
        {
            DragDrop.DoDragDrop(border, new DataObject(DragFormat, typeId), DragDropEffects.Move);
        }
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        if (_viewModel is null || e.Data.GetData(DragFormat) is not string typeId)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;

            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        ShowDropHint(typeId, e.GetPosition(PreviewArea));
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        HideDropHint();
    }

    private void OnPreviewDrop(object sender, DragEventArgs e)
    {
        HideDropHint();

        if (_viewModel is null || e.Data.GetData(DragFormat) is not string typeId)
        {
            return;
        }

        var position = e.GetPosition(PreviewArea);

        if (TargetAt(position) is not { } target)
        {
            return;
        }

        var side = DashboardPaneSurface.Side(position, target.Bounds);

        if (_tilePaths.TryGetValue(typeId, out var sourcePath))
        {
            if (!sourcePath.AsSpan().SequenceEqual(target.Path))
            {
                _viewModel.Move(sourcePath, target.Path, side);
            }
        }
        else
        {
            _viewModel.Add(typeId, target.Path, side);
        }

        e.Handled = true;
    }

    private DropTarget? TargetAt(Point position)
    {
        var paths = new List<int[]>();
        var bounds = new List<Rect>();

        foreach (var (element, path) in _panePaths)
        {
            if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            {
                continue;
            }

            paths.Add(path);
            bounds.Add(DashboardPaneSurface.Bounds(element, PreviewArea));
        }

        var nearest = DashboardPaneSurface.NearestPane(bounds, position, DashboardPaneSurface.DropReach);

        return nearest < 0 ? null : new(paths[nearest], bounds[nearest]);
    }

    private void ShowDropHint(string typeId, Point position)
    {
        if (_viewModel is null || TargetAt(position) is not { } target)
        {
            HideDropHint();

            return;
        }

        var side = DashboardPaneSurface.Side(position, target.Bounds);
        var placed = _tilePaths.TryGetValue(typeId, out var sourcePath);

        if (placed && sourcePath!.AsSpan().SequenceEqual(target.Path))
        {
            HideDropHint();

            return;
        }

        var pane = placed
            ? _viewModel.PreviewMove(sourcePath!, target.Path, side)
            : _viewModel.PreviewAdd(typeId, target.Path, side);

        if (pane is null
            || _viewModel.PreviewTile(typeId) is not { } tile
            || DashboardPaneSurface.LeafOf(pane, tile) is not { } leaf
            || DashboardPaneSurface.MeasurePane(pane, leaf, _viewModel.ContentArea, TileContentSize) is not { } rect)
        {
            HideDropHint();

            return;
        }

        var origin = PreviewBox.TransformToAncestor(PreviewArea).Transform(default);

        if (_dropHint is null)
        {
            _dropFill = new()
            {
                Opacity = DropFillOpacity,
            };

            _dropFill.SetResourceReference(Border.BackgroundProperty, ThemeKeys.AccentSoft);
            _dropFill.SetResourceReference(Border.CornerRadiusProperty, ThemeKeys.RadiusM);

            _dropHint = new()
            {
                BorderThickness = new(2),
                IsHitTestVisible = false,
                Child = _dropFill,
            };

            _dropHint.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.AccentPrimary);
            _dropHint.SetResourceReference(Border.CornerRadiusProperty, ThemeKeys.RadiusM);
        }

        if (!PreviewOverlay.Children.Contains(_dropHint))
        {
            PreviewOverlay.Children.Add(_dropHint);
        }

        _dropFill!.Visibility = side == PaneSide.None ? Visibility.Collapsed : Visibility.Visible;

        _dropHint.Width = rect.Width * _scale;
        _dropHint.Height = rect.Height * _scale;

        Canvas.SetLeft(_dropHint, origin.X + (rect.X * _scale));
        Canvas.SetTop(_dropHint, origin.Y + (rect.Y * _scale));
    }

    private void HideDropHint()
    {
        PreviewOverlay.Children.Clear();
    }

    private Size TileContentSize(DashboardTileViewModel tile)
    {
        return _tileHosts.TryGetValue(tile, out var host) ? host.DesiredSize : default;
    }

    private void RebuildPreview()
    {
        HideDropHint();

        PreviewBox.Child = null;
        _panePaths.Clear();
        _tilePaths.Clear();
        _tileHosts.Clear();

        if (_viewModel is null)
        {
            return;
        }

        var area = _viewModel.ContentArea;

        if (area.Width <= 0 || area.Height <= 0 || PreviewArea.ActualWidth <= 0)
        {
            return;
        }

        var ceiling = PreviewArea.ActualHeight > 0 ? Math.Min(PreviewArea.ActualHeight, MaxPreviewHeight) : MaxPreviewHeight;

        _builtWidth = PreviewArea.ActualWidth;
        _scale = Math.Min(_builtWidth / area.Width, ceiling / area.Height);
        _viewModel.Scale = _scale;

        RebuildMiniatures();
    }

    private FrameworkElement WrapOverflow(FrameworkElement content, Size area)
    {
        var required = _viewModel is { Stacked: false, Pane: { } pane } ? DashboardPaneSurface.RequiredHeight(pane) : 0;
        var overflows = required > area.Height;
        var grid = new Grid { Height = overflows ? required : double.NaN };

        grid.Children.Add(content);

        return new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = _viewModel?.Stacked == true || overflows
                ? ScrollBarVisibility.Auto
                : ScrollBarVisibility.Disabled,
        };
    }

    private FrameworkElement BuildContent(DashboardLayoutSectionViewModel viewModel)
    {
        var surface = Surface(viewModel);

        if (viewModel.Pane is { } pane)
        {
            if (!viewModel.Stacked)
            {
                return surface.BuildRoot(pane);
            }

            var stack = new Grid();

            surface.Stack(stack, pane);

            return stack;
        }

        var bands = new Grid();

        DashboardPaneSurface.ApplyTracks(
            bands.ColumnDefinitions,
            viewModel.Bands.Count,
            static () => new ColumnDefinition(),
            (definition, index) =>
            {
                var track = viewModel.Bands[index].Width;

                definition.Width = track.Length;
                definition.MaxWidth = track.Max;
                definition.MinWidth = track.Length.IsStar ? DashboardPaneSurface.ScaledStarBandMinWidth : 0;
            });

        for (var index = 0; index < viewModel.Bands.Count; index++)
        {
            var content = surface.BuildBandContent(viewModel.Bands[index]);

            Grid.SetColumn(content, index);
            bands.Children.Add(content);
        }

        return bands;
    }

    private DashboardPaneSurface Surface(DashboardLayoutSectionViewModel viewModel)
    {
        return new()
        {
            Tile = CreateMiniature,
            Hole = CreateHole,
            Registered = Register,
            Splitters = (grid, split, alongColumns) => DashboardPaneSurface.AddSplitters(
                grid,
                split,
                alongColumns,
                completed: (_, canceled) => CommitShares(grid, split.Path, alongColumns, canceled)),
            Stacked = viewModel.Stacked,
        };
    }

    private void Register(PaneLayout pane, FrameworkElement element)
    {
        _panePaths[element] = pane.Path;

        if (pane is TilePaneLayout leaf)
        {
            _tilePaths[leaf.Tile.TypeId] = leaf.Path;
            _tileHosts[leaf.Tile] = element;
        }
    }

    private void CommitShares(Grid grid, int[] path, bool alongColumns, bool canceled)
    {
        if (canceled || _viewModel?.Resize(path, DashboardPaneSurface.Shares(grid, alongColumns)) != true)
        {
            RebuildPreview();
        }
    }

    private FrameworkElement CreateHole()
    {
        var inverse = 1 / _scale;

        var border = new Border
        {
            BorderThickness = new(inverse),
            CornerRadius = new(4 * inverse),
            Margin = new(2 * inverse),
            Background = Brushes.Transparent,
        };

        border.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.BorderSubtle);

        return border;
    }

    private FrameworkElement CreateMiniature(DashboardTileViewModel tile)
    {
        var inverse = 1 / _scale;

        var icon = new PackIconLucide
        {
            Kind = tile.Icon,
            Width = MiniatureIconSize * inverse,
            Height = MiniatureIconSize * inverse,
            VerticalAlignment = VerticalAlignment.Center,
        };

        icon.SetResourceReference(ForegroundProperty, ThemeKeys.FgMuted);

        var header = new StackPanel { Orientation = Orientation.Horizontal };

        header.Children.Add(icon);

        if (!tile.IsCollapsedToStrip)
        {
            var title = new TextBlock
            {
                Text = tile.Title,
                ToolTip = tile.Title,
                FontSize = MiniatureFontSize * inverse,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new(6 * inverse, 0, 0, 0),
            };

            title.SetResourceReference(ForegroundProperty, ThemeKeys.FgPrimary);
            header.Children.Add(title);
        }

        var content = new Grid();

        content.RowDefinitions.Add(new() { Height = GridLength.Auto });
        content.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        content.Children.Add(header);

        if (!tile.IsCollapsed)
        {
            var body = new StackPanel { Margin = new(0, MiniaturePadding * inverse, 0, 0) };

            body.Children.Add(SchematicLine(inverse, 0.75));
            body.Children.Add(SchematicLine(inverse, 0.45));

            Grid.SetRow(body, 1);
            content.Children.Add(body);
        }

        var border = new Border
        {
            BorderThickness = new(inverse),
            CornerRadius = new(4 * inverse),
            Margin = new(2 * inverse),
            Padding = new(MiniaturePadding * inverse),
            Cursor = Cursors.SizeAll,
            Tag = tile.TypeId,
            Child = content,
            ContextMenu = BuildContextMenu(tile),
        };

        border.SetResourceReference(Border.BackgroundProperty, ThemeKeys.BgSurface);
        border.SetResourceReference(Border.BorderBrushProperty, ThemeKeys.BorderSubtle);

        border.PreviewMouseLeftButtonDown += OnMiniatureMouseDown;

        return border;
    }

    private static Grid SchematicLine(double inverse, double share)
    {
        var line = new Grid { Margin = new(0, 0, 0, 4 * inverse) };

        line.ColumnDefinitions.Add(new() { Width = new(share, GridUnitType.Star) });
        line.ColumnDefinitions.Add(new() { Width = new(1 - share, GridUnitType.Star) });

        var bar = new Border
        {
            Height = MiniatureLineHeight * inverse,
            CornerRadius = new(MiniatureLineHeight * inverse / 2),
        };

        bar.SetResourceReference(Border.BackgroundProperty, ThemeKeys.BgBase);

        line.Children.Add(bar);

        return line;
    }

    private ContextMenu BuildContextMenu(DashboardTileViewModel tile)
    {
        var menu = new ContextMenu();
        var record = _viewModel?.Record(tile.TypeId);
        var meta = _viewModel?.Meta(tile.TypeId);

        menu.Items.Add(BuildMaxSizeMenu(
            "Макс. ширина",
            _viewModel?.MaxWidthPresets ?? [],
            record?.MaxWidth,
            meta?.DefaultMaxWidth,
            value => _viewModel?.SetMaxWidth(tile.TypeId, value)));

        menu.Items.Add(BuildMaxSizeMenu(
            "Макс. высота",
            _viewModel?.MaxHeightPresets ?? [],
            record?.MaxHeight,
            meta?.DefaultMaxHeight,
            value => _viewModel?.SetMaxHeight(tile.TypeId, value)));

        menu.Items.Add(new Separator());

        var removeItem = new MenuItem { Header = "Убрать плитку" };

        removeItem.Click += (_, _) =>
        {
            if (_tilePaths.TryGetValue(tile.TypeId, out var path))
            {
                _viewModel?.Remove(path);
            }
        };

        menu.Items.Add(removeItem);

        return menu;
    }

    private static MenuItem BuildMaxSizeMenu(string label, IReadOnlyList<int> presets, int? overrideValue, int? typeDefault, Action<int?> setValue)
    {
        var isExplicitAuto = overrideValue is <= 0;
        var effective = isExplicitAuto ? null : overrideValue ?? typeDefault;

        string headerText;

        if (isExplicitAuto)
        {
            headerText = $"{label}: авто";
        }
        else if (effective.HasValue)
        {
            headerText = overrideValue.HasValue
                ? $"{label}: {effective.Value}px"
                : $"{label}: {effective.Value}px (по умолч.)";
        }
        else
        {
            headerText = $"{label}: авто (по умолч.)";
        }

        var root = new MenuItem { Header = headerText };

        var defaultLabel = typeDefault.HasValue
            ? $"По умолчанию ({typeDefault.Value}px)"
            : "По умолчанию (авто)";

        var defaultItem = new MenuItem { Header = defaultLabel, IsCheckable = true, IsChecked = !overrideValue.HasValue };
        defaultItem.Click += (_, _) => setValue(null);
        root.Items.Add(defaultItem);

        if (typeDefault.HasValue)
        {
            var autoItem = new MenuItem { Header = "Авто", IsCheckable = true, IsChecked = isExplicitAuto };
            autoItem.Click += (_, _) => setValue(0);
            root.Items.Add(autoItem);
        }

        root.Items.Add(new Separator());

        foreach (var preset in presets)
        {
            var value = preset;
            var item = new MenuItem { Header = $"{value}px", IsCheckable = true, IsChecked = overrideValue == value };
            item.Click += (_, _) => setValue(value);
            root.Items.Add(item);
        }

        // TODO: произвольное значение в px ввести нельзя – у IDialogService каркаса нет запроса числа,
        //       поэтому остаются пресеты, «авто» и умолчание. Завести поле, когда числовой промпт появится в KeepShell.
        return root;
    }

    private sealed record DropTarget(int[] Path, Rect Bounds);
}
