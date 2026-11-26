using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.Views.Controls;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Diagnostics;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Core.YouTube.Quota;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.Views.Onboarding;
using PoproshaykaBot.Wpf.Views.Settings;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public sealed class YouTubeSettingsSectionViewTests
{
    private static readonly string[] Dictionaries =
    [
        "pack://application:,,,/KeepShell;component/Resources/Themes/Light.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Themes/Tokens.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Converters.xaml",
        "pack://application:,,,/KeepShell;component/Resources/Styles/Controls.xaml",
        "pack://application:,,,/PoproshaykaBot.Wpf;component/Infrastructure/Converters/AppConverters.xaml",
    ];

    [OneTimeSetUp]
    public void EnsureApplication()
    {
        TestResources.Ensure(Dictionaries);
    }

    [Test]
    public void Блоки_транспорта_и_категорий_квоты_отрисовываются_без_обрезки_по_ширине()
    {
        var quotaStatus = new YouTubeQuotaStatus(
            "proj-1",
            new DateOnly(2026, 11, 24),
            new DateTimeOffset(2026, 11, 25, 8, 0, 0, TimeSpan.Zero),
            10_000,
            91,
            0,
            75,
            16,
            9_909,
            false,
            YouTubeQuotaReliability.Reliable,
            true,
            new Dictionary<YouTubeQuotaCategory, int>
            {
                [YouTubeQuotaCategory.ChatSend] = 60,
                [YouTubeQuotaCategory.StreamMonitoring] = 12,
                [YouTubeQuotaCategory.ChatRead] = 15,
                [YouTubeQuotaCategory.ChannelAndBroadcastInfo] = 4,
            });

        var chatStatus = new YouTubeChatStatus(
            ConnectionState.Connected,
            YouTubeChatTransport.InnerTube,
            ChatReadIssue.None,
            "@streamer",
            "chat-1",
            null,
            null,
            null,
            10,
            0,
            0);

        var monitorStatus = new YouTubeMonitoringStatus(
            true,
            StreamStatus.Online,
            YouTubeMonitorIssue.None,
            "UC1",
            "vid-1",
            "chat-1",
            1,
            DateTimeOffset.UtcNow,
            0,
            YouTubeMonitoringTransport.HybridInnerTubeAndDataApi);

        using var vm = new YouTubeSettingsSectionViewModel(
            new StubOAuthService(),
            new StubShellLauncher(),
            new StubDialogService(),
            new ManualUiDispatcher(),
            TimeProvider.System,
            NullLoggerFactory.Instance,
            new StubQuotaGovernor(quotaStatus),
            new StubChatDiagnostics(chatStatus),
            new StubMonitorDiagnostics(monitorStatus));

        vm.Load(new YouTubeSettings { Enabled = true, ProjectId = "proj-1" }, string.Empty);

        var view = new YouTubeSettingsSectionView { DataContext = vm };
        view.Measure(new Size(740, 1400));
        view.Arrange(new Rect(0, 0, 740, 1400));
        view.UpdateLayout();

        var texts = Descendants<TextBlock>(view)
            .Where(static tb => tb.Visibility == Visibility.Visible && !string.IsNullOrWhiteSpace(tb.Text))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(texts.Any(tb => tb.Text.Contains("Как сейчас работает YouTube", StringComparison.Ordinal)), Is.True);
            Assert.That(texts.Any(tb => tb.Text.Contains("InnerTube (без расхода квоты)", StringComparison.Ordinal)), Is.True);
            Assert.That(texts.Any(tb => tb.Text.Contains("Куда уходит квота за сегодня", StringComparison.Ordinal)), Is.True);
            Assert.That(texts.Any(tb => tb.Text == "Отправка в чат"), Is.True);
            Assert.That(texts.Any(tb => tb.Text == "60 ед."), Is.True);
            Assert.That(texts.All(tb =>
            {
                var bounds = tb.TransformToAncestor(view).TransformBounds(new Rect(0, 0, tb.ActualWidth, tb.ActualHeight));
                var fitsView = bounds.Left >= -0.5 && bounds.Right <= 740.0 + 0.5;
                var parent = VisualTreeHelper.GetParent(tb) as FrameworkElement;
                var fitsParent = parent is null || parent.ActualWidth <= 0.5 || tb.ActualWidth <= parent.ActualWidth + 0.5;
                return fitsView && fitsParent;
            }), Is.True);
        }
    }

    [Test]
    public void Все_редакторы_формы_YouTube_выровнены_по_одной_оси_и_имеют_имена_UI_Automation()
    {
        using var vm = CreateSectionViewModel();
        vm.ShowLegacyChannelField = true;
        vm.IsAdvancedExpanded = true;
        vm.Load(new YouTubeSettings { Enabled = true, ProjectId = "proj-1", ClientId = "cid", Channel = "@chan" }, "sec");

        var view = new YouTubeSettingsSectionView { DataContext = vm };
        view.Measure(new Size(740, 1400));
        view.Arrange(new Rect(0, 0, 740, 1400));
        view.UpdateLayout();

        var clientId = Descendants<RevealableTextBox>(view).Single(e => AutomationProperties.GetName(e) == "Client ID");
        var clientSecret = Descendants<RevealableTextBox>(view).Single(e => AutomationProperties.GetName(e) == "Client Secret");
        var channel = Descendants<TextBox>(view).Single(e => AutomationProperties.GetName(e) == "Канал YouTube");
        var projectId = Descendants<TextBox>(view).Single(e => AutomationProperties.GetName(e) == "Project ID");
        var quotaBudget = Descendants<TextBox>(view).Single(e => AutomationProperties.GetName(e) == "Бюджет квоты в сутки");

        var clientIdLeft = clientId.TransformToAncestor(view).TransformBounds(new Rect(0, 0, clientId.ActualWidth, clientId.ActualHeight)).Left;
        var clientSecretLeft = clientSecret.TransformToAncestor(view).TransformBounds(new Rect(0, 0, clientSecret.ActualWidth, clientSecret.ActualHeight)).Left;
        var channelLeft = channel.TransformToAncestor(view).TransformBounds(new Rect(0, 0, channel.ActualWidth, channel.ActualHeight)).Left;
        var projectIdLeft = projectId.TransformToAncestor(view).TransformBounds(new Rect(0, 0, projectId.ActualWidth, projectId.ActualHeight)).Left;
        var quotaBudgetLeft = quotaBudget.TransformToAncestor(view).TransformBounds(new Rect(0, 0, quotaBudget.ActualWidth, quotaBudget.ActualHeight)).Left;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clientSecretLeft, Is.EqualTo(clientIdLeft).Within(0.5));
            Assert.That(channelLeft, Is.EqualTo(clientIdLeft).Within(0.5));
            Assert.That(projectIdLeft, Is.EqualTo(clientIdLeft).Within(0.5));
            Assert.That(quotaBudgetLeft, Is.EqualTo(clientIdLeft).Within(0.5));

            Assert.That(channel.ActualWidth, Is.EqualTo(clientId.ActualWidth).Within(0.5));
            Assert.That(projectId.ActualWidth, Is.EqualTo(clientId.ActualWidth).Within(0.5));
            Assert.That(quotaBudget.ActualWidth, Is.LessThan(clientId.ActualWidth));

            Assert.That(AutomationProperties.GetName(clientId.FindName("Pwd") as DependencyObject ?? throw new InvalidOperationException()), Is.EqualTo("Client ID"));
            Assert.That(AutomationProperties.GetName(clientId.FindName("Plain") as DependencyObject ?? throw new InvalidOperationException()), Is.EqualTo("Client ID"));
            Assert.That(AutomationProperties.GetName(clientSecret.FindName("Pwd") as DependencyObject ?? throw new InvalidOperationException()), Is.EqualTo("Client Secret"));
            Assert.That(AutomationProperties.GetName(clientSecret.FindName("Plain") as DependencyObject ?? throw new InvalidOperationException()), Is.EqualTo("Client Secret"));
        }
    }

    [Test]
    public void В_мастере_YouTube_дополнительные_секции_свёрнуты_по_умолчанию_и_есть_подсказка_блокировки()
    {
        using var editor = CreateSectionViewModel();
        using var page = new YouTubeOnboardingPageViewModel(editor);
        var context = new OnboardingContext(new AppSettings(), new(), new(), new YouTubeSettings { Enabled = true });
        page.OnEnter(context);

        var onboardingView = new YouTubeOnboardingPageView { DataContext = page };
        onboardingView.Measure(new Size(688, 900));
        onboardingView.Arrange(new Rect(0, 0, 688, 900));
        onboardingView.UpdateLayout();

        var settingsView = new YouTubeSettingsSectionView { DataContext = editor };
        settingsView.Measure(new Size(688, 900));
        settingsView.Arrange(new Rect(0, 0, 688, 900));
        settingsView.UpdateLayout();

        var expanders = Descendants<Expander>(settingsView).ToArray();
        var broadcasterExpander = Descendants<Expander>(onboardingView).Single(e => Equals(e.Header, "Аккаунт стримера (необязательно)"));
        var hintBlock = Descendants<TextBlock>(onboardingView).Single(tb => tb.Text == page.ValidationHint);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(editor.IsAdvancedExpanded, Is.False);
            Assert.That(editor.IsGuideExpanded, Is.False);
            Assert.That(expanders.All(e => !e.IsExpanded), Is.True);
            Assert.That(broadcasterExpander.IsExpanded, Is.False);
            Assert.That(hintBlock.Visibility, Is.EqualTo(Visibility.Visible));
        }
    }

    [Test]
    public void PlatformOAuthAccountView_скрывает_ручные_токены_в_Expander_и_задаёт_имена_UI_Automation()
    {
        using var ytSection = CreateSectionViewModel();
        ytSection.Load(new YouTubeSettings { Enabled = true }, string.Empty);

        var botView = new PlatformOAuthAccountView { DataContext = ytSection.Bot };
        botView.Measure(new Size(680, 900));
        botView.Arrange(new Rect(0, 0, 680, 900));
        botView.UpdateLayout();

        var broadcasterView = new PlatformOAuthAccountView { DataContext = ytSection.Broadcaster };
        broadcasterView.Measure(new Size(680, 900));
        broadcasterView.Arrange(new Rect(0, 0, 680, 900));
        broadcasterView.UpdateLayout();

        var botExpander = Descendants<Expander>(botView).Single();
        var broadcasterExpander = Descendants<Expander>(broadcasterView).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(botExpander.IsExpanded, Is.False);
            Assert.That(botExpander.Header, Is.EqualTo("Дополнительные параметры (токены и права)"));
            Assert.That(Descendants<RevealableTextBox>(botView), Is.Empty);
        }

        botExpander.IsExpanded = true;
        botView.UpdateLayout();
        broadcasterExpander.IsExpanded = true;
        broadcasterView.UpdateLayout();

        var accessBox = Descendants<RevealableTextBox>(botView).Single(e => AutomationProperties.GetName(e) == "Access Token");
        var refreshBox = Descendants<RevealableTextBox>(botView).Single(e => AutomationProperties.GetName(e) == "Refresh Token");
        var botResetBtn = Descendants<Button>(botView).Single(b => Equals(b.Content, "Восстановить стандартные права бота"));
        var broadcasterResetBtn = Descendants<Button>(broadcasterView).Single(b => Equals(b.Content, "Восстановить стандартные права стримера"));

        var tempDir = Path.Combine(Path.GetTempPath(), "PoproshaykaBotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var twitchAdapter = new TwitchAccountAuthAdapter(
                PlatformAuthRole.Bot,
                new StubTwitchOAuthService(),
                new AccountsStore(filePath: Path.Combine(tempDir, "accounts.json")),
                () => new OAuthCredentialsSnapshot("cid", "sec", "http://localhost:8080/callback", "chan"),
                new StubEmbeddedTwitchAuthDialog());
            using var twitchVm = new PlatformOAuthAccountViewModel(
                twitchAdapter,
                new StubDialogService(),
                new StubShellLauncher(),
                new ManualUiDispatcher(),
                TimeProvider.System,
                NullLogger<PlatformOAuthAccountViewModel>.Instance);
            var twitchView = new PlatformOAuthAccountView { DataContext = twitchVm };
            twitchView.Measure(new Size(680, 900));
            twitchView.Arrange(new Rect(0, 0, 680, 900));
            twitchView.UpdateLayout();

            var twitchExpander = Descendants<Expander>(twitchView).Single();
            twitchExpander.IsExpanded = true;
            twitchView.UpdateLayout();

            var scopesBox = Descendants<TextBox>(twitchView).Single(e => AutomationProperties.GetName(e) == "Scopes");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(botResetBtn.IsEnabled, Is.False);
                Assert.That(broadcasterResetBtn.IsEnabled, Is.False);
                Assert.That(AutomationProperties.GetName(accessBox.FindName("Pwd") as DependencyObject ?? throw new InvalidOperationException()), Is.EqualTo("Access Token"));
                Assert.That(AutomationProperties.GetName(accessBox.FindName("Plain") as DependencyObject ?? throw new InvalidOperationException()), Is.EqualTo("Access Token"));
                Assert.That(AutomationProperties.GetName(refreshBox.FindName("Pwd") as DependencyObject ?? throw new InvalidOperationException()), Is.EqualTo("Refresh Token"));
                Assert.That(AutomationProperties.GetName(refreshBox.FindName("Plain") as DependencyObject ?? throw new InvalidOperationException()), Is.EqualTo("Refresh Token"));
                Assert.That(scopesBox, Is.Not.Null);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Test]
    public void HttpServerCheckPageView_имеет_имя_порта_и_одну_кнопку_проверки_при_ошибке_ввода()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PoproshaykaBotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(tempDir, "settings.json"));
            var server = new KestrelHttpServer(null!, settingsManager, [], NullLogger<KestrelHttpServer>.Instance, NullLoggerFactory.Instance);
            var vm = new HttpServerCheckPageViewModel(settingsManager, server, NullLogger<HttpServerCheckPageViewModel>.Instance);
            var context = new OnboardingContext(
                new AppSettings { Twitch = new TwitchSettings { Enabled = false, HttpServerPort = 8080 } },
                new(),
                new(),
                new YouTubeSettings { Enabled = true });
            vm.OnEnter(context);
            vm.PortText = "99999";

            var view = new HttpServerCheckPageView { DataContext = vm };
            view.Measure(new Size(688, 400));
            view.Arrange(new Rect(0, 0, 688, 400));
            view.UpdateLayout();

            var portBox = Descendants<TextBox>(view).Single(e => AutomationProperties.GetName(e) == "Порт HTTP-сервера");
            var checkButtons = Descendants<Button>(view).Where(b => Equals(b.Content, "Проверить порт") && b.Visibility == Visibility.Visible).ToArray();
            var retryButtons = Descendants<Button>(view).Where(b => Equals(b.Content, "Повторить проверку") && b.Visibility == Visibility.Visible).ToArray();
            var errorBlock = Descendants<TextBlock>(view).Single(tb => tb.Text == vm.PortErrorText);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(portBox, Is.Not.Null);
                Assert.That(checkButtons, Has.Length.EqualTo(1));
                Assert.That(retryButtons, Is.Empty);
                Assert.That(errorBlock.Visibility, Is.EqualTo(Visibility.Visible));
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private static YouTubeSettingsSectionViewModel CreateSectionViewModel()
    {
        return new(
            new StubOAuthService(),
            new StubShellLauncher(),
            new StubDialogService(),
            new ManualUiDispatcher(),
            TimeProvider.System,
            NullLoggerFactory.Instance);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class StubQuotaGovernor(YouTubeQuotaStatus status) : IYouTubeQuotaGovernor
    {
        public bool CanAfford(YouTubeQuotaPriority priority, int cost) => true;

        public bool TryReserve(
            PlatformAuthRole role,
            YouTubeQuotaPriority priority,
            int cost,
            out YouTubeQuotaLease lease,
            YouTubeQuotaCategory category = YouTubeQuotaCategory.Other)
        {
            lease = default!;
            return false;
        }

        public void MarkExhaustedFromServer(PlatformAuthRole role)
        {
        }

        public YouTubeQuotaStatus CaptureDiagnostics() => status;
    }

    private sealed class StubChatDiagnostics(YouTubeChatStatus status) : IYouTubeChatDiagnostics
    {
        public YouTubeChatStatus CaptureDiagnostics() => status;
    }

    private sealed class StubMonitorDiagnostics(YouTubeMonitoringStatus status) : IYouTubeMonitorDiagnostics
    {
        public YouTubeMonitoringStatus CaptureDiagnostics() => status;
    }

    private sealed class StubShellLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;

        public bool Reveal(string path) => true;

        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class StubDialogService : IDialogService
    {
        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public bool Confirm(string title, string message, bool defaultYes = false) => true;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;

        public void Info(string title, string message)
        {
        }

        public void Warning(string title, string message)
        {
        }

        public void Error(string title, string message)
        {
        }
    }

    private sealed class StubOAuthService : IYouTubeOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string[]? scopes,
            Action<string> onAuthUrlReady,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string[]? scopes,
            Action<string> onAuthUrlReady,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => false;

        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false)
        {
        }
    }

    private sealed class StubEmbeddedTwitchAuthDialog : Infrastructure.IEmbeddedTwitchAuthDialog
    {
        public Task<Infrastructure.EmbeddedTwitchAuthResult> AuthorizeAsync(Infrastructure.EmbeddedTwitchAuthRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Infrastructure.EmbeddedTwitchAuthResult.Canceled("Отменено"));
    }

    private sealed class StubTwitchOAuthService : ITwitchOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged
        {
            add { }
            remove { }
        }

        public Task<string> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default)
            => throw new NotSupportedException();

        public void SetAuthResult(string code, string? state) { }

        public void SetAuthError(Exception exception) { }

        public Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default) => Task.FromResult(true);

        public Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default) => Task.FromResult<TokenValidationInfo?>(null);

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) => Task.FromResult(string.Empty);

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>(null);

        public void UpdateSettings(PlatformAuthRole role, string accessToken, string refreshToken, IEnumerable<string>? newScopes, string login, string userId, int expiresInSeconds = 0, bool publishAuthorizationRefreshed = false) { }

        public void ClearTokens(PlatformAuthRole role) { }
    }
}
