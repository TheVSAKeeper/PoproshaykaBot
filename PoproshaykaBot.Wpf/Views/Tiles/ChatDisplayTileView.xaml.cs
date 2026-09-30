using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class ChatDisplayTileView : UserControl, IView<ChatDisplayTileViewModel>
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WebViewThemeBackground _themeBackground;

    private bool _initialized;
    private bool _disposed;
    private bool _reloadAttempted;
    private ChatDisplayTileViewModel? _viewModel;
    private string? _clutterScriptId;
    private int _clutterScriptGeneration;

    public ChatDisplayTileView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Dispatcher.ShutdownStarted += OnShutdownStarted;
        _themeBackground = WebViewThemeBackground.Attach(WebView);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || _disposed)
        {
            return;
        }

        if (DataContext is not ChatDisplayTileViewModel viewModel)
        {
            return;
        }

        _initialized = true;
        _viewModel = viewModel;
        _viewModel.ReloadRequested += OnReloadRequested;
        _viewModel.ResetSessionRequested += OnResetSessionRequested;
        _viewModel.ClutterScriptChanged += OnClutterScriptChanged;
        _viewModel.ChannelUriChanged += OnChannelUriChanged;

        await InitializeWebViewAsync(viewModel);
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
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        Dispatcher.ShutdownStarted -= OnShutdownStarted;
        _themeBackground.Detach();
        _lifetime.Cancel();

        if (_viewModel is not null)
        {
            _viewModel.ReloadRequested -= OnReloadRequested;
            _viewModel.ResetSessionRequested -= OnResetSessionRequested;
            _viewModel.ClutterScriptChanged -= OnClutterScriptChanged;
            _viewModel.ChannelUriChanged -= OnChannelUriChanged;
        }

        if (WebView.CoreWebView2 is not null)
        {
            WebView.CoreWebView2.ProcessFailed -= OnProcessFailed;
        }

        WebView.NavigationCompleted -= OnNavigationCompleted;
        WebView.Dispose();
        _lifetime.Dispose();
    }

    private async Task InitializeWebViewAsync(ChatDisplayTileViewModel viewModel)
    {
        if (App.IsGallery)
        {
            return;
        }

        try
        {
            var environment = await WebView2EnvironmentFactory.CreateAsync(
                viewModel.UserDataFolder,
                viewModel.Logger,
                _lifetime.Token);

            if (_disposed)
            {
                return;
            }

            await WebView.EnsureCoreWebView2Async(environment);

            if (_disposed)
            {
                return;
            }

            WebView.CoreWebView2.ProcessFailed += OnProcessFailed;
            WebView.NavigationCompleted += OnNavigationCompleted;

            await RegisterClutterScriptAsync(viewModel);

            if (viewModel.ChannelUri is { } channelUri)
            {
                WebView.Source = channelUri;
            }
            else
            {
                viewModel.ShowNoChannelFallback();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            viewModel.Logger.LogError(exception, "WebView2 Runtime не установлен");
            viewModel.ShowRuntimeMissingFallback();
        }
        catch (Exception exception)
        {
            viewModel.Logger.LogError(exception, "Не удалось инициализировать WebView2 для Twitch-чата");
            viewModel.ShowGenericFallback();
        }
    }

    private async Task<bool> RegisterClutterScriptAsync(ChatDisplayTileViewModel viewModel)
    {
        if (_disposed || WebView.CoreWebView2 is not { } core)
        {
            return false;
        }

        var generation = ++_clutterScriptGeneration;

        try
        {
            if (_clutterScriptId is not null)
            {
                core.RemoveScriptToExecuteOnDocumentCreated(_clutterScriptId);
                _clutterScriptId = null;
            }

            var scriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(viewModel.BuildHideClutterScript());

            if (generation != _clutterScriptGeneration || _disposed)
            {
                core.RemoveScriptToExecuteOnDocumentCreated(scriptId);
                return false;
            }

            _clutterScriptId = scriptId;
            return true;
        }
        catch (Exception exception)
        {
            viewModel.Logger.LogWarning(exception, "Не удалось применить блокираторы баннеров чата");
            return false;
        }
    }

    private void OnClutterScriptChanged()
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        _ = UiAsync.RunGuardedAsync(
            () => ReapplyClutterScriptAsync(viewModel),
            exception => viewModel.Logger.LogWarning(exception, "Не удалось перезагрузить чат после смены блокираторов баннеров"));
    }

    private async Task ReapplyClutterScriptAsync(ChatDisplayTileViewModel viewModel)
    {
        if (_disposed || WebView.CoreWebView2 is null)
        {
            return;
        }

        if (!await RegisterClutterScriptAsync(viewModel) || _disposed)
        {
            return;
        }

        WebView.Reload();
    }

    private void OnChannelUriChanged()
    {
        if (_disposed || _viewModel?.ChannelUri is not { } channelUri || WebView.CoreWebView2 is null)
        {
            return;
        }

        WebView.Source = channelUri;
    }

    private void OnReloadRequested()
    {
        if (WebView.CoreWebView2 is not null)
        {
            WebView.Reload();
        }
    }

    private void OnResetSessionRequested()
    {
        if (_viewModel is not { } viewModel || WebView.CoreWebView2?.Profile is not { } profile)
        {
            return;
        }

        var confirmed = StyledMessageBox.Show(
            $"Это разлогинит {viewModel.AccountLabel} из Twitch-чата и очистит куки профиля. Продолжить?",
            "Сброс сессии Twitch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed)
        {
            return;
        }

        _ = UiAsync.RunGuardedAsync(
            () => ClearSessionAsync(profile),
            exception => viewModel.Logger.LogError(exception, "Не удалось очистить данные сессии WebView2"));
    }

    private async Task ClearSessionAsync(CoreWebView2Profile profile)
    {
        await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);

        if (_disposed)
        {
            return;
        }

        WebView.Reload();
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        _viewModel?.Logger.LogError("WebView2 процесс упал: {Reason}", e.ProcessFailedKind);

        if (_reloadAttempted)
        {
            return;
        }

        _reloadAttempted = true;

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (WebView.CoreWebView2 is not null)
            {
                WebView.Reload();
            }
        });
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (e.IsSuccess)
        {
            _reloadAttempted = false;
            return;
        }

        _viewModel?.Logger.LogWarning("Twitch-чат не загрузился: status={HttpStatusCode}, error={WebErrorStatus}", e.HttpStatusCode, e.WebErrorStatus);
    }
}
