using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PoproshaykaBot.Core.Infrastructure;
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
    private readonly WebViewThemeBackground _platformThemeBackground;
    private readonly BrowserState _twitchBrowser = new();
    private readonly BrowserState _platformBrowser = new();

    private bool _initialized;
    private bool _disposed;
    private bool _webViewRuntimeMissing;
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
        _platformThemeBackground = WebViewThemeBackground.Attach(PlatformWebView);
    }

    private ChatDisplayPaneViewModel? PaneFor(WebView2 browser) =>
        ReferenceEquals(browser, WebView) ? _viewModel?.TwitchChat : _viewModel?.PlatformChat;

    private BrowserState StateFor(WebView2 browser) => ReferenceEquals(browser, WebView) ? _twitchBrowser : _platformBrowser;

    private WebView2? BrowserForCore(object? sender)
    {
        if (sender is not CoreWebView2 core)
        {
            return null;
        }

        return ReferenceEquals(core, WebView.CoreWebView2) ? WebView
            : ReferenceEquals(core, PlatformWebView.CoreWebView2) ? PlatformWebView : null;
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
        _viewModel.OpenRequested += OnOpenRequested;
        _viewModel.ResetSessionRequested += OnResetSessionRequested;
        _viewModel.ClutterScriptChanged += OnClutterScriptChanged;

        await Task.WhenAll(OpenChatAsync(WebView), OpenChatAsync(PlatformWebView));
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
        _platformThemeBackground.Detach();
        _lifetime.Cancel();

        if (_viewModel is not null)
        {
            _viewModel.OpenRequested -= OnOpenRequested;
            _viewModel.ResetSessionRequested -= OnResetSessionRequested;
            _viewModel.ClutterScriptChanged -= OnClutterScriptChanged;
        }

        ReleaseBrowser(WebView);
        ReleaseBrowser(PlatformWebView);
        _lifetime.Dispose();
    }

    private void ReleaseBrowser(WebView2 browser)
    {
        if (browser.CoreWebView2 is { } core)
        {
            core.ProcessFailed -= OnProcessFailed;
            core.NavigationStarting -= OnNavigationStarting;
            core.NavigationCompleted -= OnNavigationCompleted;
        }

        browser.Dispose();
    }

    private async Task OpenChatAsync(WebView2 browser, bool reload = false)
    {
        if (_disposed || App.IsGallery || _viewModel is not { } viewModel || PaneFor(browser) is not { } pane)
        {
            return;
        }

        var state = StateFor(browser);
        var request = ++state.OpenRequest;
        if (!pane.IsShown)
        {
            return;
        }

        var uri = pane.ChannelUri;
        Task? initialization = null;

        try
        {
            if (uri is null)
            {
                if (state.Navigation.Target is not null)
                {
                    state.Navigation.Begin(null);
                    browser.CoreWebView2?.Navigate("about:blank");
                }

                return;
            }

            if (_webViewRuntimeMissing)
            {
                pane.ShowRuntimeMissingFallback();
                viewModel.UpdateWebViewActions();
                return;
            }

            var dataFolder = ReferenceEquals(browser, WebView) ? viewModel.UserDataFolder
                : AppPaths.Combine("WebView2", "platform-chat");
            initialization = state.Initialization ??= InitializeBrowserAsync(browser, dataFolder, viewModel);
            await initialization;

            if (_disposed || request != state.OpenRequest || browser.CoreWebView2 is not { } core)
            {
                return;
            }

            if (reload || !state.Navigation.CanReuse(uri))
            {
                state.Navigation.Begin(uri);
                core.Navigate(uri.AbsoluteUri);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            if (!_disposed && !_webViewRuntimeMissing)
            {
                _webViewRuntimeMissing = true;
                viewModel.Logger.LogError(exception, "WebView2 Runtime не установлен");
            }

            if (!_disposed && pane.IsShown && pane.ChannelUri is not null)
            {
                pane.ShowRuntimeMissingFallback();
                viewModel.UpdateWebViewActions();
            }
        }
        catch (Exception exception)
        {
            if (_disposed)
            {
                return;
            }

            if (initialization is not { IsFaulted: true } || ReferenceEquals(state.Initialization, initialization))
            {
                if (initialization is { IsFaulted: true })
                {
                    state.Initialization = null;
                }

                state.Navigation.RequireRetry();
                viewModel.Logger.LogError(exception, "Не удалось открыть WebView2 для чата трансляции");
            }

            if (request == state.OpenRequest && uri is not null)
            {
                pane.ShowGenericFallback();
            }
        }
    }

    private async Task InitializeBrowserAsync(WebView2 browser, string dataFolder, ChatDisplayTileViewModel viewModel)
    {
        var environment = await WebView2EnvironmentFactory.CreateAsync(dataFolder, viewModel.Logger, _lifetime.Token);
        if (_disposed)
        {
            return;
        }

        await browser.EnsureCoreWebView2Async(environment);
        if (_disposed)
        {
            return;
        }

        var core = browser.CoreWebView2;
        core.ProcessFailed += OnProcessFailed;
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;

        if (ReferenceEquals(browser, WebView))
        {
            await RegisterClutterScriptAsync(viewModel);
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

        await RefreshTwitchAfterSettingsChangeAsync();
    }

    private void OnOpenRequested(ChatDisplayPaneViewModel pane, bool reload)
    {
        var browser = ReferenceEquals(pane, _viewModel?.TwitchChat) ? WebView : PlatformWebView;
        _ = OpenChatAsync(browser, reload);
    }

    private void OnResetSessionRequested()
    {
        if (_viewModel is not { TwitchChat.IsShown: true } viewModel || WebView.CoreWebView2?.Profile is not { } profile)
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

        await RefreshTwitchAfterSettingsChangeAsync();
    }

    private Task RefreshTwitchAfterSettingsChangeAsync()
    {
        _twitchBrowser.Navigation.RequireRetry();
        return OpenChatAsync(WebView, reload: true);
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (BrowserForCore(sender) is { } browser)
        {
            StateFor(browser).Navigation.Started(e.Uri, e.NavigationId, e.IsRedirected);
        }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (BrowserForCore(sender) is not { } browser)
        {
            return;
        }

        _viewModel?.Logger.LogError("WebView2 процесс упал: {Reason}", e.ProcessFailedKind);

        var navigation = StateFor(browser).Navigation;
        navigation.RequireRetry();
        if (PaneFor(browser) is not { IsShown: true, ChannelUri: not null } pane)
        {
            return;
        }

        if (!navigation.TryRecover())
        {
            pane.ShowGenericFallback();
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (!_disposed && pane.IsShown)
            {
                _ = OpenChatAsync(browser, reload: true);
            }
        });
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var success = e.IsSuccess && e.HttpStatusCode < 400;
        if (BrowserForCore(sender) is not { } browser
            || !StateFor(browser).Navigation.TryComplete(e.NavigationId, success)
            || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled
            || PaneFor(browser) is not { IsShown: true, ChannelUri: not null } pane)
        {
            return;
        }

        if (success)
        {
            pane.HasFallback = false;
            return;
        }

        _viewModel?.Logger.LogWarning("Чат {Platform} не загрузился: status={HttpStatusCode}, error={WebErrorStatus}",
            pane.Platform, e.HttpStatusCode, e.WebErrorStatus);
        pane.ShowGenericFallback();
    }

    private sealed class BrowserState
    {
        public int OpenRequest { get; set; }
        public Task? Initialization { get; set; }
        public ChatBrowserNavigation Navigation { get; } = new();
    }
}
