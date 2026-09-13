using PoproshaykaBot.Core.Dashboard;
using PoproshaykaBot.Wpf.Infrastructure.Dashboard;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Views.Settings;

public partial class DashboardLayoutSectionView : UserControl, IView<DashboardLayoutSectionViewModel>
{
    private const string DragFormat = "DashboardTileTypeId";
    private const double TileCellMinWidth = 56;

    private DashboardLayoutSectionViewModel? _viewModel;

    public DashboardLayoutSectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
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

        RebuildEditor();
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
        RebuildEditor();
    }

    private void OnPaletteMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { Tag: string typeId } button)
        {
            DragDrop.DoDragDrop(button, new DataObject(DragFormat, typeId), DragDropEffects.Copy);
        }
    }

    private void OnTileMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string typeId } border)
        {
            DragDrop.DoDragDrop(border, new DataObject(DragFormat, typeId), DragDropEffects.Move);
        }
    }

    private void OnCellDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnCellDrop(object sender, DragEventArgs e)
    {
        if (_viewModel is null || !e.Data.GetDataPresent(DragFormat) || e.Data.GetData(DragFormat) is not string typeId)
        {
            return;
        }

        if (sender is not FrameworkElement element)
        {
            return;
        }

        switch (element.Tag)
        {
            case CellPosition cell:
                _viewModel.PlaceOrMove(typeId, cell.Row, cell.Column);
                break;

            case string targetTypeId when _viewModel.Placed(targetTypeId) is { } target:
                if (!string.Equals(targetTypeId, typeId, StringComparison.Ordinal))
                {
                    _viewModel.PlaceOrMove(typeId, target.Row, target.Column);
                }

                break;
        }

        e.Handled = true;
    }

    private void RebuildEditor()
    {
        EditorGrid.Children.Clear();
        EditorGrid.ColumnDefinitions.Clear();
        EditorGrid.RowDefinitions.Clear();

        if (_viewModel is null)
        {
            return;
        }

        var columnCount = _viewModel.ColumnCount;
        var rowCount = _viewModel.RowCount;

        for (var column = 0; column < columnCount; column++)
        {
            EditorGrid.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        }

        for (var row = 0; row < rowCount; row++)
        {
            EditorGrid.RowDefinitions.Add(new() { Height = new(1, GridUnitType.Star) });
        }

        var occupied = new bool[rowCount, columnCount];

        foreach (var placed in _viewModel.PlacedTiles)
        {
            for (var r = placed.Row; r < placed.Row + placed.RowSpan && r < rowCount; r++)
            {
                for (var c = placed.Column; c < placed.Column + placed.ColumnSpan && c < columnCount; c++)
                {
                    occupied[r, c] = true;
                }
            }

            EditorGrid.Children.Add(CreateTilePanel(placed));
        }

        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                if (!occupied[row, column])
                {
                    EditorGrid.Children.Add(CreatePlaceholder(row, column));
                }
            }
        }
    }

    private Border CreateTilePanel(PlacedTile placed)
    {
        var meta = _viewModel!.Meta(placed.TypeId);

        var title = meta?.Title ?? placed.TypeId;

        var text = new TextBlock
        {
            Text = title,
            ToolTip = title,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new(4),
        };

        text.SetResourceReference(TextBlock.ForegroundProperty, "Fg.Primary");

        var border = new Border
        {
            BorderThickness = new(1),
            CornerRadius = new(4),
            Margin = new(2),
            MinWidth = TileCellMinWidth,
            Cursor = Cursors.SizeAll,
            AllowDrop = true,
            Tag = placed.TypeId,
            Child = text,
            ContextMenu = BuildContextMenu(placed),
        };

        border.SetResourceReference(Border.BackgroundProperty, "Accent.Soft");
        border.SetResourceReference(Border.BorderBrushProperty, "Accent.Primary");

        border.PreviewMouseLeftButtonDown += OnTileMouseDown;
        border.DragOver += OnCellDragOver;
        border.Drop += OnCellDrop;

        Grid.SetRow(border, placed.Row);
        Grid.SetColumn(border, placed.Column);
        Grid.SetRowSpan(border, placed.RowSpan);
        Grid.SetColumnSpan(border, placed.ColumnSpan);

        return border;
    }

    private Border CreatePlaceholder(int row, int column)
    {
        var border = new Border
        {
            BorderThickness = new(1),
            CornerRadius = new(4),
            Margin = new(2),
            MinWidth = TileCellMinWidth,
            AllowDrop = true,
            Background = Brushes.Transparent,
            Tag = new CellPosition(row, column),
        };

        border.SetResourceReference(Border.BorderBrushProperty, "Border.Subtle");

        border.DragOver += OnCellDragOver;
        border.Drop += OnCellDrop;

        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);

        return border;
    }

    private ContextMenu BuildContextMenu(PlacedTile placed)
    {
        var menu = new ContextMenu();

        var maxColumnSpan = Math.Max(1, _viewModel!.ColumnCount - placed.Column);
        var maxRowSpan = Math.Max(1, _viewModel.RowCount - placed.Row);

        menu.Items.Add(BuildSpanMenu("Ширина", placed.ColumnSpan, maxColumnSpan, span => _viewModel.SetColumnSpan(placed.TypeId, span)));
        menu.Items.Add(BuildSpanMenu("Высота", placed.RowSpan, maxRowSpan, span => _viewModel.SetRowSpan(placed.TypeId, span)));
        menu.Items.Add(new Separator());

        var meta = _viewModel.Meta(placed.TypeId);

        menu.Items.Add(BuildMaxSizeMenu("Макс. ширина", _viewModel.MaxWidthPresets, placed.MaxWidth, meta?.DefaultMaxWidth, value => _viewModel.SetMaxWidth(placed.TypeId, value)));
        menu.Items.Add(BuildMaxSizeMenu("Макс. высота", _viewModel.MaxHeightPresets, placed.MaxHeight, meta?.DefaultMaxHeight, value => _viewModel.SetMaxHeight(placed.TypeId, value)));
        menu.Items.Add(new Separator());

        var removeItem = new MenuItem { Header = "Удалить плитку" };
        removeItem.Click += (_, _) => _viewModel.RemoveTile(placed.TypeId);
        menu.Items.Add(removeItem);

        return menu;
    }

    private static MenuItem BuildSpanMenu(string label, int currentSpan, int maxSpan, Action<int> setSpan)
    {
        var root = new MenuItem { Header = $"{label}: {currentSpan}" };

        for (var i = 1; i <= maxSpan; i++)
        {
            var span = i;
            var item = new MenuItem { Header = span.ToString(), IsCheckable = true, IsChecked = span == currentSpan };
            item.Click += (_, _) => setSpan(span);
            root.Items.Add(item);
        }

        return root;
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

    private sealed record CellPosition(int Row, int Column);
}
