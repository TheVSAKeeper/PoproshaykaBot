using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using KeepShell.Bootstrap;
using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Views;

public partial class MultistreamPageView : UserControl, IView<MultistreamPageViewModel>
{
    public static readonly DependencyProperty IsCompactLayoutProperty = DependencyProperty.Register(
        nameof(IsCompactLayout),
        typeof(bool),
        typeof(MultistreamPageView),
        new PropertyMetadata(false));

    private const double CompactEnterWidth = 880;
    private const double CompactExitWidth = 920;

    private MultistreamPageViewModel? _page;
    private bool _revealingSection;

    public MultistreamPageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        DataContextChanged += (_, e) =>
        {
            if (IsLoaded)
            {
                AttachPage(e.NewValue as MultistreamPageViewModel);
            }
        };
        IsVisibleChanged += (_, _) =>
        {
            if (_page is { } page)
            {
                page.Status.SetPageOnScreen(IsVisible);
                if (IsVisible)
                {
                    RevealSection(page);
                }
            }
        };
    }

    public bool IsCompactLayout
    {
        get => (bool)GetValue(IsCompactLayoutProperty);
        set => SetValue(IsCompactLayoutProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
        FontScaleManager.Changed += OnFontScaleChanged;
        UpdateResponsiveLayout();
        AttachPage(DataContext as MultistreamPageViewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        FontScaleManager.Changed -= OnFontScaleChanged;
        AttachPage(null);
    }

    private void OnFontScaleChanged(object? sender, double _) => UpdateResponsiveLayout();

    private void UpdateResponsiveLayout()
    {
        var next = ResolveCompactLayout(IsCompactLayout, ActualWidth, FontScaleManager.Current);
        if (next != IsCompactLayout)
        {
            IsCompactLayout = next;
        }
    }

    internal static bool ResolveCompactLayout(bool currentCompact, double width, double fontScale)
    {
        if (width <= 0)
        {
            return currentCompact;
        }

        var scale = Math.Max(1.0, fontScale);
        if (!currentCompact && width < CompactEnterWidth * scale)
        {
            return true;
        }

        if (currentCompact && width >= CompactExitWidth * scale)
        {
            return false;
        }

        return currentCompact;
    }

    private void AttachPage(MultistreamPageViewModel? page)
    {
        if (ReferenceEquals(_page, page))
        {
            return;
        }

        if (_page is not null)
        {
            _page.SectionRequested -= OnSectionRequested;
            _page.Status.SetPageOnScreen(false);
        }

        _page = page;
        if (_page is not null)
        {
            _page.SectionRequested += OnSectionRequested;
            _page.Status.SetPageOnScreen(IsVisible);
            RevealSection(_page);
        }
    }

    private void OnSectionRequested(object? sender, EventArgs e)
    {
        if (sender is MultistreamPageViewModel page)
        {
            RevealSection(page);
        }
    }

    private void RevealSection(MultistreamPageViewModel page)
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!ReferenceEquals(_page, page) || !IsVisible)
            {
                return;
            }

            var section = page.TakeSectionRequest();
            if (section == MultistreamSection.None)
            {
                return;
            }

            DetailScroll.UpdateLayout();
            var target = section switch
            {
                MultistreamSection.Parameters => FindControl(PlatformSettingsHost, control =>
                        AutomationProperties.GetAutomationId(control) == "MultistreamEnableYouTube")
                    ?? FindControl(PlatformSettingsHost, control => control.IsTabStop),
                MultistreamSection.Platforms => FindPlatformTarget(page.SelectedCard),
                MultistreamSection.Automation => FindControl(AutomationSection, control => control.IsTabStop),
                MultistreamSection.BotAuthorization => FindControl(ChannelEditor, control =>
                        AutomationProperties.GetAutomationId(control) is "ChannelBotAuthorization" or "DefaultBotAuthorization")
                    ?? FindChannelTarget(),
                _ => FindChannelTarget(),
            };
            if (target is null)
            {
                return;
            }

            _revealingSection = true;
            try
            {
                target.Focus();
                if (target.IsDescendantOf(DetailScroll))
                {
                    var top = target.TransformToAncestor(DetailScroll).Transform(default).Y + DetailScroll.VerticalOffset;
                    DetailScroll.ScrollToVerticalOffset(Math.Max(0, top - 12));
                }
            }
            finally
            {
                _revealingSection = false;
            }
        });
    }

    private Control? FindPlatformTarget(MultistreamPlatformCard? card)
    {
        if (card is null)
        {
            return null;
        }

        return PlatformCards.ItemContainerGenerator.ContainerFromItem(card) as Control;
    }

    private Control? FindChannelTarget() =>
        FindControl(ChannelEditor, control => AutomationProperties.GetAutomationId(control) == "ChannelCatalogSelection")
        ?? FindControl(ChannelEditor, control => control.IsTabStop);

    private static Control? FindControl(DependencyObject root, Predicate<Control> matches)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Control { Focusable: true, IsVisible: true, IsEnabled: true } control && matches(control))
            {
                return control;
            }

            if (FindControl(child, matches) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void SuppressNavigationAutoScroll(object sender, RequestBringIntoViewEventArgs e)
    {
        if (_revealingSection)
        {
            e.Handled = true;
        }
    }
}
