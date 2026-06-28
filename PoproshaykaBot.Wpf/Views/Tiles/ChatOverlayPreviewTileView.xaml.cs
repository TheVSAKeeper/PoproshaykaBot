using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class ChatOverlayPreviewTileView : UserControl, IView<ChatOverlayPreviewTileViewModel>
{
    private bool _initialized;
    private ChatOverlayPreviewTileViewModel? _viewModel;

    public ChatOverlayPreviewTileView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        if (DataContext is not ChatOverlayPreviewTileViewModel viewModel)
        {
            return;
        }

        _initialized = true;
        _viewModel = viewModel;

        await InitializeWebViewAsync(viewModel);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        WebView.Dispose();
    }

    private async Task InitializeWebViewAsync(ChatOverlayPreviewTileViewModel viewModel)
    {
        try
        {
            var environment = await WebView2EnvironmentFactory.CreateAsync(viewModel.UserDataFolder, viewModel.Logger);
            await WebView.EnsureCoreWebView2Async(environment);
            WebView.CoreWebView2.Navigate(viewModel.CurrentUrl);
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            viewModel.Logger.LogError(exception, "WebView2 Runtime не установлен");
            viewModel.ShowRuntimeMissingError();
        }
        catch (Exception exception)
        {
            viewModel.Logger.LogError(exception, "Ошибка инициализации WebView2 превью оверлея");
            viewModel.ShowGenericError();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatOverlayPreviewTileViewModel.CurrentUrl))
        {
            return;
        }

        if (WebView.CoreWebView2 is null || _viewModel is null)
        {
            return;
        }

        WebView.CoreWebView2.Navigate(_viewModel.CurrentUrl);
    }
}
