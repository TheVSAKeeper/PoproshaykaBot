using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class StreamPreviewTileView : UserControl, IView<StreamPreviewTileViewModel>
{
    private const double ContentSizedWidth = 320;

    private double _slotWidth;

    public StreamPreviewTileView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => FitDecodeWidth();
        DataContextChanged += (_, _) => FitDecodeWidth();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var bounded = double.IsFinite(constraint.Width) && constraint.Width > 0;

        _slotWidth = bounded ? constraint.Width : 0;
        FrameImage.MaxWidth = bounded ? double.PositiveInfinity : ContentSizedWidth;

        return base.MeasureOverride(constraint);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        FitDecodeWidth();
    }

    private void FitDecodeWidth()
    {
        if (_slotWidth > 0 && DataContext is StreamPreviewTileViewModel tile)
        {
            tile.FitTo(_slotWidth * VisualTreeHelper.GetDpi(this).DpiScaleX);
        }
    }
}
