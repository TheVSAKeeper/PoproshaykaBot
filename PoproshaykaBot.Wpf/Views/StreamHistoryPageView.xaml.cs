using PoproshaykaBot.Wpf.ViewModels;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views;

public partial class StreamHistoryPageView : UserControl, IView<StreamHistoryPageViewModel>
{
    public StreamHistoryPageView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is StreamHistoryPageViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnVmPropertyChanged;
        }

        if (e.NewValue is StreamHistoryPageViewModel newVm)
        {
            newVm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StreamHistoryPageViewModel.SelectedRow))
        {
            return;
        }

        if (sender is StreamHistoryPageViewModel vm && vm.SelectedRow != null)
        {
            SessionsGrid.ScrollIntoView(vm.SelectedRow);
        }
    }
}
