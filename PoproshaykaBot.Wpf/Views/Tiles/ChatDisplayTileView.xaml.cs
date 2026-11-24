using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
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
    private BrowserState _primaryBrowser = new();
    private BrowserState _secondaryBrowser = new();

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
        ReferenceEquals(browser, WebView) ? _viewModel?.PrimaryChat : _viewModel?.SecondaryChat;

    private BrowserState StateFor(WebView2 browser) => ReferenceEquals(browser, WebView) ? _primaryBrowser : _secondaryBrowser;

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

    private WebView2 BrowserForPane(ChatDisplayPaneViewModel pane) =>
        ReferenceEquals(pane, _viewModel?.PrimaryChat) ? WebView : PlatformWebView;

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
        _viewModel.SetDarkTheme(ThemeManager.Current?.IsDark == true);
        _viewModel.OpenRequested += OnOpenRequested;
        _viewModel.ResetSessionRequested += OnResetSessionRequested;
        _viewModel.ResetPaneSessionRequested += OnResetPaneSessionRequested;
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
        _viewModel?.SetDarkTheme(theme.IsDark);
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
            _viewModel.ResetPaneSessionRequested -= OnResetPaneSessionRequested;
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
        var dataFolder = profile?.UserDataFolder ?? viewModel.GetDefaultUserDataFolder(pane);
        var session = (DataFolder: dataFolder, ProfileName: profile?.ProfileName);
        if (state.Session is { } active && active != session)
        {
            browser = ReplaceBrowser(browser);
            state = StateFor(browser);
        }

        state.Session = session;
        var request = ++state.OpenRequest;

        var uri = pane.ChannelUri;
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

            initialization = state.Initialization ??= InitializeBrowserAsync(browser, pane, session, viewModel);
            await initialization;

            if (!IsCurrentBrowser(browser) || request != state.OpenRequest || browser.CoreWebView2 is not { } core)
            {
                return;
            }

            if (reload || !state.Navigation.CanReuse(uri))
            {
                if (pane.AppliesPreferredColorScheme)
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
        var isPrimary = ReferenceEquals(browser, WebView);
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
        if (isPrimary)
        {
            _themeBackground.Detach();
            _themeBackground = WebViewThemeBackground.Attach(replacement);
            WebView = replacement;
            _primaryBrowser = new();
            _clutterScriptId = null;
            _clutterScriptGeneration++;
        }
        else
        {
            _platformThemeBackground.Detach();
            _platformThemeBackground = WebViewThemeBackground.Attach(replacement);
            PlatformWebView = replacement;
            _secondaryBrowser = new();
        }

        ReleaseBrowser(browser);
        host.Children.Insert(index, replacement);
        return replacement;
    }

    private async Task InitializeBrowserAsync(
        WebView2 browser,
        ChatDisplayPaneViewModel pane,
        (string DataFolder, string? ProfileName) session,
        ChatDisplayTileViewModel viewModel)
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

        if (pane.SupportsClutterBlocking)
        {
            await RegisterClutterScriptAsync(browser, viewModel);
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

    private async Task<bool> RegisterClutterScriptAsync(WebView2 browser, ChatDisplayTileViewModel viewModel)
    {
        if (_disposed || browser.CoreWebView2 is not { } core)
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

            if (_disposed || !ReferenceEquals(core, browser.CoreWebView2))
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
        if (_disposed)
        {
            return;
        }

        foreach (var browser in new[] { WebView, PlatformWebView })
        {
            if (PaneFor(browser) is not { IsShown: true, SupportsClutterBlocking: true } || browser.CoreWebView2 is null)
            {
                continue;
            }

            if (!await RegisterClutterScriptAsync(browser, viewModel) || _disposed)
            {
                return;
            }

            await RefreshPaneAfterSettingsChangeAsync(browser);
        }
    }

    private void OnOpenRequested(ChatDisplayPaneViewModel pane, bool reload)
    {
        _ = OpenChatAsync(BrowserForPane(pane), reload);
    }

    private void OnResetSessionRequested()
    {
        if (_viewModel is not { } viewModel)
        {
            return;
        }

        var pane = viewModel.PrimaryChat.IsShown ? viewModel.PrimaryChat : viewModel.SecondaryChat;
        if (!pane.IsShown)
        {
            return;
        }

        if (pane.SelectedAccount is { Id.Length: > 0, BrowserProfile: not null } selectedAccount)
        {
            OnResetPaneSessionRequested(pane, selectedAccount.Id, selectedAccount.Label);
            return;
        }

        var browser = BrowserForPane(pane);
        if (browser.CoreWebView2?.Profile is not { } profile)
        {
            return;
        }

        var confirmed = StyledMessageBox.Show(
            $"Это завершит вход в чате {pane.DisplayName} и очистит куки профиля «{pane.SelectedAccount?.Label}». Продолжить?",
            $"Сброс сессии {pane.DisplayName}",
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

    private void OnResetPaneSessionRequested(ChatDisplayPaneViewModel pane, string accountId, string accountLabel)
    {
        var browser = BrowserForPane(pane);
        if (_viewModel is not { } viewModel || !pane.IsShown)
        {
            return;
        }

        var targetPlatform = pane.Platform;
        var platformDisplayName = pane.DisplayName;
        var confirmed = StyledMessageBox.Show(
            $"Это завершит веб-сессию {platformDisplayName} и очистит куки профиля «{accountLabel}». Продолжить?",
            $"Сброс веб-сессии {platformDisplayName}",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

        if (!confirmed || !IsCurrentBrowser(browser))
        {
            return;
        }

        var profile = browser.CoreWebView2?.Profile;
        var browserProfile = pane.SelectedAccount?.BrowserProfile;
        _ = UiAsync.RunGuardedAsync(
            () => ClearPaneSessionAsync(browser, profile, browserProfile, viewModel, pane, targetPlatform, platformDisplayName, accountId),
            exception =>
            {
                viewModel.Logger.LogError(exception, "Не удалось очистить веб-сессию {Platform} WebView2", platformDisplayName);
                pane.StatusNotice = $"Не удалось очистить куки веб-сессии {platformDisplayName}. Попробуйте позже.";
            });
    }

    private async Task ClearPaneSessionAsync(
        WebView2 browser,
        CoreWebView2Profile? profile,
        PlatformChatBrowserProfile? browserProfile,
        ChatDisplayTileViewModel viewModel,
        ChatDisplayPaneViewModel pane,
        StreamingPlatform targetPlatform,
        string platformDisplayName,
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
            var init = state.Initialization ??= InitializeBrowserAsync(browser, pane, targetSession, viewModel);
            await init;

            if (!IsCurrentBrowser(browser))
            {
                return;
            }

            profile = browser.CoreWebView2?.Profile;
        }

        if (profile is null)
        {
            viewModel.Logger.LogWarning("Профиль WebView2 для аккаунта {Platform} {AccountId} недоступен для очистки", platformDisplayName, accountId);
            pane.StatusNotice = $"Не удалось очистить веб-сессию {platformDisplayName}: браузер недоступен.";
            return;
        }

        await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);
        profile.Delete();

        if (_disposed)
        {
            return;
        }

        viewModel.CompletePaneSessionReset(pane, targetPlatform, accountId);
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

        await RefreshPaneAfterSettingsChangeAsync(browser);
    }

    private Task RefreshPaneAfterSettingsChangeAsync(WebView2 browser)
    {
        StateFor(browser).Navigation.RequireRetry();
        return OpenChatAsync(browser, reload: true);
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
