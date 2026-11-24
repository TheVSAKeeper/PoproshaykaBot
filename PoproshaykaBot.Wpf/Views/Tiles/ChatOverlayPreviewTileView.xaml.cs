using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class ChatOverlayPreviewTileView : UserControl, IView<ChatOverlayPreviewTileViewModel>
{
    private readonly CancellationTokenSource _lifetime = new();
    private WebViewThemeBackground _themeBackground;

    private CancellationTokenSource? _browserCts;
    private int _browserGeneration;
    private bool _initializing;
    private bool _disposed;
    private ChatOverlayPreviewTileViewModel? _viewModel;

    public ChatOverlayPreviewTileView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
        Dispatcher.ShutdownStarted += OnShutdownStarted;
        _themeBackground = WebViewThemeBackground.Attach(WebView);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_viewModel is null && DataContext is ChatOverlayPreviewTileViewModel viewModel)
        {
            _viewModel = viewModel;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateBrowserForVisibility();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed || !IsLoaded)
        {
            return;
        }

        UpdateBrowserForVisibility();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!IsLoaded)
            {
                Release();
            }
        });
    }

    private void OnShutdownStarted(object? sender, EventArgs e)
    {
        Release();
    }

    private void Release()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _browserGeneration++;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        IsVisibleChanged -= OnIsVisibleChanged;
        Dispatcher.ShutdownStarted -= OnShutdownStarted;
        _themeBackground.Detach();
        CancelBrowserInitialization();
        _lifetime.Cancel();

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        WebView.Dispose();
        _lifetime.Dispose();
    }

    private void UpdateBrowserForVisibility()
    {
        if (_disposed || _viewModel is not { } viewModel)
        {
            return;
        }

        if (IsVisible)
        {
            if (!_initializing && WebView.CoreWebView2 is null && !viewModel.HasError)
            {
                _ = InitializeWebViewAsync(viewModel);
            }

            return;
        }

        if (!viewModel.IsLayoutEditing
            && !viewModel.IsModalDialogOpen
            && (_initializing || WebView.CoreWebView2 is not null))
        {
            UnloadBrowser();
        }
    }

    private void UnloadBrowser()
    {
        _browserGeneration++;
        _initializing = false;
        CancelBrowserInitialization();
        ReplaceBrowser();
    }

    private void ReplaceBrowser()
    {
        if (_disposed || WebView.Parent is not Grid host)
        {
            return;
        }

        var replacement = new WebView2 { Style = WebView.Style, Name = WebView.Name };
        var index = host.Children.IndexOf(WebView);
        host.Children.RemoveAt(index);
        UnregisterName(WebView.Name);
        RegisterName(replacement.Name, replacement);
        _themeBackground.Detach();
        var previous = WebView;
        WebView = replacement;
        _themeBackground = WebViewThemeBackground.Attach(replacement);
        previous.Dispose();
        host.Children.Insert(index, replacement);
    }

    private void CancelBrowserInitialization()
    {
        if (_browserCts is null)
        {
            return;
        }

        try
        {
            _browserCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _browserCts.Dispose();
        _browserCts = null;
    }

    private async Task InitializeWebViewAsync(ChatOverlayPreviewTileViewModel viewModel)
    {
        if (App.IsGallery || _disposed || !IsVisible)
        {
            return;
        }

        CancelBrowserInitialization();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _browserCts = cts;
        var generation = _browserGeneration;
        _initializing = true;

        try
        {
            var environment = await WebView2EnvironmentFactory.CreateAsync(
                viewModel.UserDataFolder,
                viewModel.Logger,
                cts.Token);

            if (_disposed || generation != _browserGeneration || !IsVisible)
            {
                return;
            }

            await WebView.EnsureCoreWebView2Async(environment);

            if (_disposed || generation != _browserGeneration)
            {
                return;
            }

            if (!IsVisible)
            {
                if (!viewModel.IsLayoutEditing && !viewModel.IsModalDialogOpen)
                {
                    UnloadBrowser();
                }

                return;
            }

            WebView.CoreWebView2.Navigate(viewModel.CurrentUrl);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            if (generation == _browserGeneration && !_disposed)
            {
                viewModel.Logger.LogError(exception, "WebView2 Runtime не установлен");
                viewModel.ShowRuntimeMissingError();
            }
        }
        catch (Exception exception)
        {
            if (generation == _browserGeneration && !_disposed)
            {
                viewModel.Logger.LogError(exception, "Ошибка инициализации WebView2 превью оверлея");
                viewModel.ShowGenericError();
            }
        }
        finally
        {
            if (generation == _browserGeneration)
            {
                _initializing = false;
            }

            if (ReferenceEquals(_browserCts, cts))
            {
                _browserCts = null;
            }

            cts.Dispose();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatOverlayPreviewTileViewModel.CurrentUrl))
        {
            return;
        }

        if (_disposed || WebView.CoreWebView2 is null || _viewModel is null)
        {
            return;
        }

        WebView.CoreWebView2.Navigate(_viewModel.CurrentUrl);
    }
}
