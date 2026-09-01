using Microsoft.Web.WebView2.Core;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using System.ComponentModel;
using System.Windows;

namespace PoproshaykaBot.Wpf.Views.Dialogs;

public partial class EmbeddedTwitchAuthWindow : Window
{
    private readonly EmbeddedTwitchAuthDialogViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();

    private bool _initialized;
    private bool _tornDown;

    public EmbeddedTwitchAuthWindow(EmbeddedTwitchAuthDialogViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        WindowChromeTheming.Attach(this);

        _viewModel.NavigateRequested += OnNavigateRequested;
        _viewModel.CloseRequested += OnCloseRequested;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        Dispatcher.ShutdownStarted += OnShutdownStarted;
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (sender is not CoreWebView2 core)
        {
            return;
        }

        e.NewWindow = core;
        e.Handled = true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || _tornDown)
        {
            return;
        }

        _initialized = true;

        if (!await TryInitializeWebViewAsync())
        {
            return;
        }

        await _viewModel.RunFlowAsync();
    }

    private async Task<bool> TryInitializeWebViewAsync()
    {
        try
        {
            var environment = await WebView2EnvironmentFactory.CreateAsync(
                _viewModel.UserDataFolder,
                _viewModel.Logger,
                _lifetime.Token);

            if (_tornDown)
            {
                return false;
            }

            await WebView.EnsureCoreWebView2Async(environment);

            if (_tornDown)
            {
                return false;
            }

            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            _viewModel.Logger.EmbeddedAuthRuntimeMissing(exception);
            _viewModel.ShowRuntimeMissing();

            return false;
        }
        catch (Exception exception)
        {
            _viewModel.Logger.EmbeddedAuthWebViewFailed(exception);
            _viewModel.ShowInitializationFailure();

            return false;
        }
    }

    private void OnNavigateRequested(string authUrl)
    {
        if (_tornDown || WebView.CoreWebView2 is null)
        {
            return;
        }

        WebView.CoreWebView2.Navigate(authUrl);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (_tornDown)
        {
            return;
        }

        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _viewModel.Cancel();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        TearDown();
    }

    private void OnShutdownStarted(object? sender, EventArgs e)
    {
        TearDown();
    }

    private void TearDown()
    {
        if (_tornDown)
        {
            return;
        }

        _tornDown = true;

        Dispatcher.ShutdownStarted -= OnShutdownStarted;
        Loaded -= OnLoaded;
        Closing -= OnClosing;
        Closed -= OnClosed;

        _viewModel.NavigateRequested -= OnNavigateRequested;
        _viewModel.CloseRequested -= OnCloseRequested;

        _lifetime.Cancel();

        if (WebView.CoreWebView2 is { } core)
        {
            core.NewWindowRequested -= OnNewWindowRequested;
        }

        WebView.Dispose();
        _lifetime.Dispose();
    }
}
