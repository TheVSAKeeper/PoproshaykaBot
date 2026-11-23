using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Wpf.Bootstrap;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace PoproshaykaBot.Wpf.Views.Tiles;

public partial class ChatDisplayTileView : UserControl, IView<ChatDisplayTileViewModel>
{
    private readonly CancellationTokenSource _lifetime = new();
    private WebViewThemeBackground _themeBackground;
    private WebViewThemeBackground _platformThemeBackground;
    private BrowserState _twitchBrowser = new();
    private BrowserState _platformBrowser = new();

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
        ThemeManager.Changed += OnThemeChanged;
        _themeBackground = WebViewThemeBackground.Attach(WebView);
        _platformThemeBackground = WebViewThemeBackground.Attach(PlatformWebView);
    }

    private ChatDisplayPaneViewModel? PaneFor(WebView2 browser) =>
        ReferenceEquals(browser, WebView) ? _viewModel?.TwitchChat : _viewModel?.PlatformChat;

    private BrowserState StateFor(WebView2 browser) => ReferenceEquals(browser, WebView) ? _twitchBrowser : _platformBrowser;

    private bool IsCurrentBrowser(WebView2 browser) => !_disposed
        && (ReferenceEquals(browser, WebView) || ReferenceEquals(browser, PlatformWebView));

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
        _viewModel.ResetPlatformSessionRequested += OnResetPlatformSessionRequested;
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

    private void OnThemeChanged(object? sender, ThemeDefinition theme)
    {
        if (_viewModel?.PlatformChat.Platform == StreamingPlatform.YouTube)
        {
            _ = OpenChatAsync(PlatformWebView);
        }
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
        ThemeManager.Changed -= OnThemeChanged;
        _themeBackground.Detach();
        _platformThemeBackground.Detach();
        _lifetime.Cancel();

        if (_viewModel is not null)
        {
            _viewModel.OpenRequested -= OnOpenRequested;
            _viewModel.ResetSessionRequested -= OnResetSessionRequested;
            _viewModel.ResetPlatformSessionRequested -= OnResetPlatformSessionRequested;
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
            core.NewWindowRequested -= OnNewWindowRequested;
        }

        browser.Dispose();
    }

    private async Task OpenChatAsync(WebView2 browser, bool reload = false)
    {
        if (!IsCurrentBrowser(browser) || App.IsGallery || _viewModel is not { } viewModel || PaneFor(browser) is not { } pane)
        {
            return;
        }

        var state = StateFor(browser);
        if (!pane.IsShown)
        {
            state.OpenRequest++;
            return;
        }

        var profile = pane.SelectedAccount?.BrowserProfile;
        var dataFolder = profile?.UserDataFolder ?? (ReferenceEquals(browser, WebView) ? viewModel.UserDataFolder
            : AppPaths.Combine("WebView2", "platform-chat"));
        var session = (DataFolder: dataFolder, ProfileName: profile?.ProfileName);
        if (state.Session is { } active && active != session)
        {
            browser = ReplaceBrowser(browser);
            state = StateFor(browser);
        }

        state.Session = session;
        var request = ++state.OpenRequest;

        var uri = pane.ChannelUri;
        var isYouTube = pane.Platform == StreamingPlatform.YouTube;
        var isDarkTheme = ThemeManager.Current?.IsDark == true;
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

            if (isYouTube)
            {
                uri = BuildYouTubeChatUri(uri, isDarkTheme);
            }

            initialization = state.Initialization ??= InitializeBrowserAsync(browser, session, viewModel);
            await initialization;

            if (!IsCurrentBrowser(browser) || request != state.OpenRequest || browser.CoreWebView2 is not { } core)
            {
                return;
            }

            if (reload || !state.Navigation.CanReuse(uri))
            {
                if (isYouTube)
                {
                    core.Profile.PreferredColorScheme = isDarkTheme
                        ? CoreWebView2PreferredColorScheme.Dark
                        : CoreWebView2PreferredColorScheme.Light;
                }

                state.Navigation.Begin(uri);
                core.Navigate(uri.AbsoluteUri);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            if (IsCurrentBrowser(browser) && !_webViewRuntimeMissing)
            {
                _webViewRuntimeMissing = true;
                viewModel.Logger.LogError(exception, "WebView2 Runtime не установлен");
            }

            if (IsCurrentBrowser(browser) && request == state.OpenRequest && pane.IsShown && pane.ChannelUri is not null)
            {
                pane.ShowRuntimeMissingFallback();
                viewModel.UpdateWebViewActions();
            }
        }
        catch (Exception exception)
        {
            if (!IsCurrentBrowser(browser))
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

    private WebView2 ReplaceBrowser(WebView2 browser)
    {
        var isTwitch = ReferenceEquals(browser, WebView);
        var host = (Grid)browser.Parent;
        var replacement = new WebView2 { Style = browser.Style, Name = browser.Name };
        if (BindingOperations.GetBindingBase(browser, WebView2.ZoomFactorProperty) is { } zoomBinding)
        {
            BindingOperations.SetBinding(replacement, WebView2.ZoomFactorProperty, zoomBinding);
        }

        var index = host.Children.IndexOf(browser);
        host.Children.RemoveAt(index);
        UnregisterName(browser.Name);
        RegisterName(replacement.Name, replacement);
        if (isTwitch)
        {
            _themeBackground.Detach();
            _themeBackground = WebViewThemeBackground.Attach(replacement);
            WebView = replacement;
            _twitchBrowser = new();
            _clutterScriptId = null;
            _clutterScriptGeneration++;
        }
        else
        {
            _platformThemeBackground.Detach();
            _platformThemeBackground = WebViewThemeBackground.Attach(replacement);
            PlatformWebView = replacement;
            _platformBrowser = new();
        }

        ReleaseBrowser(browser);
        host.Children.Insert(index, replacement);
        return replacement;
    }

    private static Uri BuildYouTubeChatUri(Uri uri, bool isDarkTheme)
    {
        var builder = new UriBuilder(uri);
        var query = builder.Query.TrimStart('?');
        var themeParameter = isDarkTheme ? "dark_theme=1" : "dark_theme=0";
        builder.Query = query.Length == 0 ? themeParameter : $"{query}&{themeParameter}";
        return builder.Uri;
    }

    private async Task InitializeBrowserAsync(WebView2 browser, (string DataFolder, string? ProfileName) session, ChatDisplayTileViewModel viewModel)
    {
        var environment = await WebView2EnvironmentFactory.CreateAsync(session.DataFolder, viewModel.Logger, _lifetime.Token);
        if (!IsCurrentBrowser(browser))
        {
            return;
        }

        var options = session.ProfileName is null ? null : environment.CreateCoreWebView2ControllerOptions();
        if (options is not null)
        {
            options.ProfileName = session.ProfileName;
        }

        await browser.EnsureCoreWebView2Async(environment, options);
        if (!IsCurrentBrowser(browser))
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
        else
        {
            core.NewWindowRequested += OnNewWindowRequested;
        }
    }

    private static void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (sender is not CoreWebView2 core)
        {
            return;
        }

        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            core.Navigate(uri.AbsoluteUri);
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

            if (_disposed || !ReferenceEquals(core, WebView.CoreWebView2))
            {
                return false;
            }

            if (generation != _clutterScriptGeneration)
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
        var browser = WebView;
        if (_viewModel is not { TwitchChat.IsShown: true } viewModel || browser.CoreWebView2?.Profile is not { } profile)
        {
            return;
        }

        var confirmed = StyledMessageBox.Show(
            $"Это завершит вход в чате Twitch и очистит куки профиля «{viewModel.TwitchChat.SelectedAccount?.Label}». Продолжить?",
            "Сброс сессии Twitch",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed || !IsCurrentBrowser(browser))
        {
            return;
        }

        _ = UiAsync.RunGuardedAsync(
            () => ClearSessionAsync(browser, profile),
            exception => viewModel.Logger.LogError(exception, "Не удалось очистить данные сессии WebView2"));
    }

    private void OnResetPlatformSessionRequested(string accountId, string accountLabel)
    {
        var browser = PlatformWebView;
        if (_viewModel is not { PlatformChat.IsShown: true } viewModel)
        {
            return;
        }

        var confirmed = StyledMessageBox.Show(
            $"Это завершит браузерный вход YouTube и очистит куки профиля «{accountLabel}». Отправка через API останется доступной. Продолжить?",
            "Сброс веб-сессии YouTube",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed || !IsCurrentBrowser(browser))
        {
            return;
        }

        var profile = browser.CoreWebView2?.Profile;
        var browserProfile = viewModel.PlatformChat.SelectedAccount?.BrowserProfile;
        _ = UiAsync.RunGuardedAsync(
            () => ClearPlatformSessionAsync(browser, profile, browserProfile, viewModel, accountId),
            exception =>
            {
                viewModel.Logger.LogError(exception, "Не удалось очистить веб-сессию YouTube WebView2");
                viewModel.PlatformChat.MessageStatus = "Не удалось очистить куки веб-сессии YouTube. Попробуйте позже.";
            });
    }

    private async Task ClearPlatformSessionAsync(
        WebView2 browser,
        CoreWebView2Profile? profile,
        PlatformChatBrowserProfile? browserProfile,
        ChatDisplayTileViewModel viewModel,
        string accountId)
    {
        if (profile is null && browserProfile is not null && IsCurrentBrowser(browser))
        {
            var state = StateFor(browser);
            var targetSession = (DataFolder: browserProfile.UserDataFolder, ProfileName: (string?)browserProfile.ProfileName);
            if (state.Session is { } active && active != targetSession)
            {
                browser = ReplaceBrowser(browser);
                state = StateFor(browser);
            }

            state.Session = targetSession;
            var init = state.Initialization ??= InitializeBrowserAsync(browser, targetSession, viewModel);
            await init;

            if (!IsCurrentBrowser(browser))
            {
                return;
            }

            profile = browser.CoreWebView2?.Profile;
        }

        if (profile is null)
        {
            viewModel.Logger.LogWarning("Профиль WebView2 для аккаунта YouTube {AccountId} недоступен для очистки", accountId);
            viewModel.PlatformChat.MessageStatus = "Не удалось очистить веб-сессию YouTube: браузер недоступен.";
            return;
        }

        await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);
        profile.Delete();

        if (_disposed)
        {
            return;
        }

        viewModel.CompletePlatformSessionReset(accountId);
    }

    private async Task ClearSessionAsync(WebView2 browser, CoreWebView2Profile profile)
    {
        if (!IsCurrentBrowser(browser))
        {
            return;
        }

        await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);

        if (!IsCurrentBrowser(browser))
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
            if (IsCurrentBrowser(browser) && pane.IsShown)
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
        public (string DataFolder, string? ProfileName)? Session { get; set; }
        public int OpenRequest { get; set; }
        public Task? Initialization { get; set; }
        public ChatBrowserNavigation Navigation { get; } = new();
    }
}
