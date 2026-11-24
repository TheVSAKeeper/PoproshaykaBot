using System.Windows;
using System.Windows.Controls;
using KeepShell.Bootstrap;
using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels.Settings;

namespace PoproshaykaBot.Wpf.Views.Settings;

public partial class ChannelCatalogSettingsSectionView : UserControl, IView<ChannelCatalogSettingsSectionViewModel>
{
    public static readonly DependencyProperty IsNarrowLayoutProperty = DependencyProperty.Register(
        nameof(IsNarrowLayout),
        typeof(bool),
        typeof(ChannelCatalogSettingsSectionView),
        new PropertyMetadata(false));

    public static readonly DependencyProperty IsTwoColumnCardsProperty = DependencyProperty.Register(
        nameof(IsTwoColumnCards),
        typeof(bool),
        typeof(ChannelCatalogSettingsSectionView),
        new PropertyMetadata(false));

    private const double NarrowEnterWidth = 700;
    private const double NarrowExitWidth = 730;
    private const double TwoColumnEnterWidth = 640;
    private const double TwoColumnExitWidth = 600;
    private const double EditorColumnRatio = 7.0 / 12.0;

    public ChannelCatalogSettingsSectionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
    }

    public bool IsNarrowLayout
    {
        get => (bool)GetValue(IsNarrowLayoutProperty);
        set => SetValue(IsNarrowLayoutProperty, value);
    }

    public bool IsTwoColumnCards
    {
        get => (bool)GetValue(IsTwoColumnCardsProperty);
        set => SetValue(IsTwoColumnCardsProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
        FontScaleManager.Changed += OnFontScaleChanged;
        UpdateResponsiveLayout();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
    }

    private void OnFontScaleChanged(object? sender, double _) => UpdateResponsiveLayout();

    internal void UpdateResponsiveLayout()
    {
        var scale = FontScaleManager.Current;
        var nextTwoColumn = ResolveTwoColumnCards(IsTwoColumnCards, ActualWidth, scale);
        if (nextTwoColumn != IsTwoColumnCards)
        {
            IsTwoColumnCards = nextTwoColumn;
        }

        var editorWidth = nextTwoColumn ? ActualWidth * EditorColumnRatio : ActualWidth;
        var nextNarrow = ResolveNarrowLayout(IsNarrowLayout, editorWidth, scale);
        if (nextNarrow != IsNarrowLayout)
        {
            IsNarrowLayout = nextNarrow;
        }
    }

    internal static bool ResolveNarrowLayout(bool currentNarrow, double width, double fontScale)
    {
        if (width <= 0)
        {
            return currentNarrow;
        }

        var scale = Math.Max(1.0, fontScale);
        if (!currentNarrow && width < NarrowEnterWidth * scale)
        {
            return true;
        }

        if (currentNarrow && width >= NarrowExitWidth * scale)
        {
            return false;
        }

        return currentNarrow;
    }

    internal static bool ResolveTwoColumnCards(bool currentTwoColumn, double width, double fontScale)
    {
        if (width <= 0)
        {
            return currentTwoColumn;
        }

        var scale = Math.Max(1.0, fontScale);
        if (!currentTwoColumn && width >= TwoColumnEnterWidth * scale)
        {
            return true;
        }

        if (currentTwoColumn && width < TwoColumnExitWidth * scale)
        {
            return false;
        }

        return currentTwoColumn;
    }
}
