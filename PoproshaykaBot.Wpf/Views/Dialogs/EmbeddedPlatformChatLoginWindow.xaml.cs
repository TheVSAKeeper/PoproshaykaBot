using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Views.Dialogs;

public partial class EmbeddedPlatformChatLoginWindow : Window
{
    private const int MonitorDefaultToNearest = 2;

    private readonly EmbeddedPlatformChatLoginDialogViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly WebViewThemeBackground _themeBackground;

    private bool _initialized;
    private bool _tornDown;

    public EmbeddedPlatformChatLoginWindow(EmbeddedPlatformChatLoginDialogViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = viewModel;

        WindowChromeTheming.Attach(this);

        _viewModel.CloseRequested += OnCloseRequested;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        Dispatcher.ShutdownStarted += OnShutdownStarted;
        _themeBackground = WebViewThemeBackground.Attach(WebView);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyOwnerMonitorBounds();
    }

    private void ApplyOwnerMonitorBounds()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        var anchor = Owner is null ? IntPtr.Zero : new WindowInteropHelper(Owner).Handle;
        var monitor = MonitorFromWindow(anchor == IntPtr.Zero ? source.Handle : anchor, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var transform = source.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var work = transform.Transform(new Vector(info.WorkRight - info.WorkLeft, info.WorkBottom - info.WorkTop));

        if (work.X > 0)
        {
            var maxWidth = Math.Floor(work.X);
            SetCurrentValue(MaxWidthProperty, maxWidth);
            SetCurrentValue(MinWidthProperty, Math.Min(MinWidth, maxWidth));
        }

        if (work.Y > 0)
        {
            var maxHeight = Math.Floor(work.Y);
            SetCurrentValue(MaxHeightProperty, maxHeight);
            SetCurrentValue(MinHeightProperty, Math.Min(MinHeight, maxHeight));
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

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is CoreWebView2 core)
        {
            _ = RefreshSignedInStateAsync(core);
        }
    }

    private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        if (sender is CoreWebView2 core)
        {
            _ = RefreshSignedInStateAsync(core);
        }
    }

    private int _cookieCheckVersion;

    private async Task RefreshSignedInStateAsync(CoreWebView2 core)
    {
        if (_tornDown || !ReferenceEquals(core, WebView.CoreWebView2))
        {
            return;
        }

        var checkVersion = ++_cookieCheckVersion;
        var descriptor = _viewModel.Descriptor;
        try
        {
            if (!IsAllowedPlatformPage(core.Source, descriptor.AllowedHosts))
            {
                _viewModel.SetSignedIn(false);
                return;
            }

            var cookies = await core.CookieManager.GetCookiesAsync(descriptor.CookieTargetUrl);
            if (_tornDown || !ReferenceEquals(core, WebView.CoreWebView2) || checkVersion != _cookieCheckVersion)
            {
                return;
            }

            var authCookies = descriptor.AuthCookieNames;
            var signedIn = cookies.Any(cookie =>
                authCookies.Contains(cookie.Name, StringComparer.Ordinal)
                && !string.IsNullOrWhiteSpace(cookie.Value));

            _viewModel.SetSignedIn(signedIn);
        }
        catch (Exception exception)
        {
            if (_viewModel.Logger.IsEnabled(LogLevel.Debug))
            {
                _viewModel.Logger.LogDebug(exception, "Не удалось проверить состояние входа веб-чата {Platform} в WebView2", descriptor.PlatformDisplayName);
            }
        }
    }

    private static bool IsAllowedPlatformPage(string? url, IReadOnlyList<string> allowedHosts)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        foreach (var host in allowedHosts)
        {
            if (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || _tornDown)
        {
            return;
        }

        _initialized = true;

        if (!await TryInitializeWebViewAsync() || _tornDown || WebView.CoreWebView2 is null)
        {
            return;
        }

        _viewModel.SetBrowserReady();
        WebView.CoreWebView2.Navigate(_viewModel.Descriptor.StartUrl);
    }

    private async Task<bool> TryInitializeWebViewAsync()
    {
        var descriptor = _viewModel.Descriptor;
        try
        {
            var environment = await WebView2EnvironmentFactory.CreateAsync(
                descriptor.UserDataFolder,
                _viewModel.Logger,
                _lifetime.Token);

            if (_tornDown)
            {
                return false;
            }

            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = _viewModel.BrowserProfileName;
            await WebView.EnsureCoreWebView2Async(environment, options);

            if (_tornDown)
            {
                return false;
            }

            WebView.CoreWebView2.Profile.PreferredColorScheme = ThemeManager.Current?.IsDark switch
            {
                true => CoreWebView2PreferredColorScheme.Dark,
                false => CoreWebView2PreferredColorScheme.Light,
                null => CoreWebView2PreferredColorScheme.Auto,
            };
            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.SourceChanged += OnSourceChanged;

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (WebView2RuntimeNotFoundException exception)
        {
            _viewModel.Logger.LogWarning(exception, "WebView2 Runtime не найден при открытии встроенного входа {Platform}", descriptor.PlatformDisplayName);
            _viewModel.ShowRuntimeMissing();

            return false;
        }
        catch (Exception exception)
        {
            _viewModel.Logger.LogError(exception, "Ошибка инициализации WebView2 для встроенного входа {Platform}", descriptor.PlatformDisplayName);
            _viewModel.ShowInitializationFailure();

            return false;
        }
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
        _viewModel.CloseRequested -= OnCloseRequested;

        _themeBackground.Detach();
        _lifetime.Cancel();

        var shouldDeleteProfile = _viewModel.Result.Outcome != PlatformWebChatLoginOutcome.Completed;
        var userDataFolder = _viewModel.Descriptor.UserDataFolder;
        var profileName = _viewModel.BrowserProfileName;

        if (WebView.CoreWebView2 is { } core)
        {
            core.NewWindowRequested -= OnNewWindowRequested;
            core.NavigationCompleted -= OnNavigationCompleted;
            core.SourceChanged -= OnSourceChanged;
            if (shouldDeleteProfile)
            {
                core.Profile.Delete();
            }
        }

        WebView.Dispose();
        if (shouldDeleteProfile)
        {
            WebView2EnvironmentFactory.TryDeleteProfileDirectory(userDataFolder, profileName, _viewModel.Logger);
        }

        _lifetime.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public int MonitorLeft;
        public int MonitorTop;
        public int MonitorRight;
        public int MonitorBottom;
        public int WorkLeft;
        public int WorkTop;
        public int WorkRight;
        public int WorkBottom;
        public int Flags;
    }
}
