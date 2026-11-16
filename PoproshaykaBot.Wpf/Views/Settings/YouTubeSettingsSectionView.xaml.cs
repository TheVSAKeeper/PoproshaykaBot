using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Settings;

public partial class YouTubeSettingsSectionView : UserControl, IView<YouTubeSettingsSectionViewModel>
{
    private YouTubeSettingsSectionViewModel? _viewModel;

    public YouTubeSettingsSectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => UpdateRefreshState();
        Unloaded += (_, _) => _viewModel?.SetQuotaAutoRefresh(false);
        IsVisibleChanged += (_, _) => UpdateRefreshState();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is YouTubeSettingsSectionViewModel previous)
        {
            previous.SetQuotaAutoRefresh(false);
        }

        _viewModel = e.NewValue as YouTubeSettingsSectionViewModel;
        UpdateRefreshState();
    }

    private void UpdateRefreshState()
    {
        _viewModel?.SetQuotaAutoRefresh(IsLoaded && IsVisible);
    }
}
