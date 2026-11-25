using System.ComponentModel;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Threading;
using KeepShell.Views;
using PoproshaykaBot.Wpf.ViewModels;

namespace PoproshaykaBot.Wpf.Views;

public partial class AutoMessagesPageView : UserControl, IView<AutoMessagesPageViewModel>
{
    public AutoMessagesPageView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Observe(DataContext as AutoMessagesPageViewModel, true);
            ShowSelectedRule(RulesList);
        };
        Unloaded += (_, _) => Observe(DataContext as AutoMessagesPageViewModel, false);
        DataContextChanged += (_, args) =>
        {
            Observe(args.OldValue as AutoMessagesPageViewModel, false);
            Observe(args.NewValue as AutoMessagesPageViewModel, IsLoaded);
        };
    }

    private void Observe(AutoMessagesPageViewModel? page, bool attach)
    {
        if (page is null)
        {
            return;
        }

        page.PropertyChanged -= OnPropertyChanged;
        if (attach)
        {
            page.PropertyChanged += OnPropertyChanged;
        }
    }

    private void OnRuleSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is ListBox list && ReferenceEquals(args.Source, list))
        {
            ShowSelectedRule(list);
        }
    }

    private void ShowSelectedRule(ListBox list)
    {
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (list.IsLoaded && list.SelectedItem is { } selected)
            {
                list.ScrollIntoView(selected);
            }
        }));
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(AutoMessagesPageViewModel.Notice) || sender is not AutoMessagesPageViewModel { Notice: not null })
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var peer = UIElementAutomationPeer.FromElement(NoticeText)
                ?? UIElementAutomationPeer.CreatePeerForElement(NoticeText);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }));
    }
}
