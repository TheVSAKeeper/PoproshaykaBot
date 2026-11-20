using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Views;

public partial class MultistreamPageView : UserControl, IView<MultistreamPageViewModel>
{
    private MultistreamPageViewModel? _page;
    private bool _revealingSection;

    public MultistreamPageView()
    {
        InitializeComponent();
        Loaded += (_, _) => AttachPage(DataContext as MultistreamPageViewModel);
        Unloaded += (_, _) => AttachPage(null);
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

            PageScroll.UpdateLayout();
            var target = section switch
            {
                MultistreamSection.Platforms => FindPlatformTarget(page.SelectedCard),
                MultistreamSection.Automation => FindControl(AutomationSection, control => control is ToggleButton),
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
                var top = target.TransformToAncestor(PageScroll).Transform(default).Y + PageScroll.VerticalOffset;
                PageScroll.ScrollToVerticalOffset(Math.Max(0, top - 12));
            }
            finally
            {
                _revealingSection = false;
            }
        });
    }

    private Control? FindPlatformTarget(MultistreamPlatformCard? card)
    {
        if (card is null || PlatformCards.ItemContainerGenerator.ContainerFromItem(card) is not ContentPresenter presenter)
        {
            return null;
        }

        var template = presenter.ContentTemplate;
        return !card.Status.IsBlockedByDebugSession
                && template.FindName("PlatformControls", presenter) is ContentControl platformControls
                && FindControl(platformControls, control => control.IsTabStop) is { } setup
            ? setup
            : template.FindName("OpenPlatform", presenter) as Button;
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
