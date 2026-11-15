using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class YouTubeSettingsSectionTests
{
    private string _root = null!;
    private FakeYouTubeOAuthService _oauthService = null!;
    private FakeShellLauncher _shellLauncher = null!;
    private FakeDialogService _dialogs = null!;
    private ManualUiDispatcher _dispatcher = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "yt-settings-ui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _oauthService = new FakeYouTubeOAuthService();
        _shellLauncher = new FakeShellLauncher();
        _dialogs = new FakeDialogService();
        _dispatcher = new ManualUiDispatcher();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Test]
    public void LoadAndSaveTo_RoundTripsProjectAndAccountFields()
    {
        using var vm = CreateSection();

        vm.Load(
            new YouTubeSettings
            {
                Enabled = true,
                ProjectId = "proj-1",
                ClientId = "client-1",
                Channel = "@streamer",
                DailyQuotaBudget = 12_000,
            },
            "secret-1",
            new YouTubeAccountSettings { AccessToken = "b-at", RefreshToken = "b-rt", ChannelId = "UCb", ChannelTitle = "Bot" },
            new YouTubeAccountSettings { AccessToken = "c-at", RefreshToken = "c-rt", ChannelId = "UCc", ChannelTitle = "Caster" });

        var dirtied = 0;
        vm.SettingChanged += (_, _) => dirtied++;

        vm.ProjectId = "  proj-2  ";
        vm.ClientId = "  client-2  ";
        vm.ClientSecret = "  secret-2  ";
        vm.Channel = "  @updated  ";
        vm.DailyQuotaBudgetText = "15000";

        var savedSettings = new YouTubeSettings();
        var botOut = new YouTubeAccountSettings();
        var broadcasterOut = new YouTubeAccountSettings();
        vm.SaveTo(savedSettings, out var savedSecret, botOut, broadcasterOut);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dirtied, Is.GreaterThanOrEqualTo(5));
            Assert.That(savedSettings.Enabled, Is.True);
            Assert.That(savedSettings.ProjectId, Is.EqualTo("proj-2"));
            Assert.That(savedSettings.ClientId, Is.EqualTo("client-2"));
            Assert.That(savedSettings.Channel, Is.EqualTo("@updated"));
            Assert.That(savedSettings.DailyQuotaBudget, Is.EqualTo(15_000));
            Assert.That(savedSecret, Is.EqualTo("secret-2"));
            Assert.That(botOut.AccessToken, Is.EqualTo("b-at"));
            Assert.That(broadcasterOut.ChannelId, Is.EqualTo("UCc"));
        }
    }

    [Test]
    public async Task OpenBrowserCommand_WithoutClientId_WarnsAndDoesNotStartFlow()
    {
        using var vm = CreateSection();
        vm.Load(new(), string.Empty, new(), new());

        await vm.Bot.OpenBrowserCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_dialogs.Warnings, Has.Count.EqualTo(1));
            Assert.That(_shellLauncher.OpenedUrls, Is.Empty);
            Assert.That(_oauthService.DraftFlowCalls, Is.Zero);
        }
    }

    [Test]
    public async Task OpenBrowserCommand_ExecutesDraftFlow_AndPopulatesTokensAndChannel()
    {
        using var vm = CreateSection();
        vm.Load(
            new() { ClientId = "client-1" },
            "secret-1",
            new(),
            new());

        var changed = 0;
        vm.SettingChanged += (_, _) => changed++;

        _oauthService.OnDraftFlow = (role, clientId, clientSecret, openUrl) =>
        {
            openUrl("https://accounts.google.com/o/oauth2/v2/auth?state=s1");
            return Task.FromResult(new YouTubeOAuthFlowResult(
                "new-access",
                "new-refresh",
                [YouTubeScopes.ForceSsl],
                "UCbot",
                "MyYouTubeBot",
                3600));
        };

        await vm.Bot.OpenBrowserCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_shellLauncher.OpenedUrls, Is.EqualTo(new[] { "https://accounts.google.com/o/oauth2/v2/auth?state=s1" }));
            Assert.That(vm.Bot.HasToken, Is.True);
            Assert.That(vm.Bot.AccessTokenValue, Is.EqualTo("new-access"));
            Assert.That(vm.Bot.RefreshTokenValue, Is.EqualTo("new-refresh"));
            Assert.That(vm.Bot.ChannelId, Is.EqualTo("UCbot"));
            Assert.That(vm.Bot.ChannelTitle, Is.EqualTo("MyYouTubeBot"));
            Assert.That(changed, Is.GreaterThanOrEqualTo(1));
        }
    }

    [Test]
    public void ClearCommand_ClearsDraftTokensAndChannel()
    {
        using var vm = CreateSection();
        vm.Load(
            new() { ClientId = "client-1" },
            "secret-1",
            new() { AccessToken = "at", RefreshToken = "rt", ChannelId = "UC1", ChannelTitle = "Bot" },
            new());

        var changed = 0;
        vm.SettingChanged += (_, _) => changed++;

        vm.Bot.ClearCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.Bot.HasAnyCredential, Is.False);
            Assert.That(vm.Bot.HasChannelIdentity, Is.False);
            Assert.That(vm.Bot.ChannelDisplay, Is.Empty);
            Assert.That(changed, Is.EqualTo(1));
        }
    }

    [TestCase("abc", true)]
    [TestCase("0", true)]
    [TestCase("-5", true)]
    [TestCase("", true)]
    [TestCase("15000", false)]
    public void DailyQuotaBudgetText_ReportsValidationErrorOnlyForInvalidValues(string input, bool expectedError)
    {
        using var vm = CreateSection();
        vm.Load(new(), string.Empty, new(), new());

        vm.DailyQuotaBudgetText = input;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.HasQuotaBudgetError, Is.EqualTo(expectedError));
            Assert.That(string.IsNullOrEmpty(vm.QuotaBudgetError), Is.EqualTo(!expectedError));
        }
    }

    private YouTubeSettingsSectionViewModel CreateSection(TimeProvider? timeProvider = null)
    {
        return new(
            _oauthService,
            _shellLauncher,
            _dialogs,
            _dispatcher,
            timeProvider ?? TimeProvider.System,
            NullLoggerFactory.Instance);
    }

    private sealed class FakeShellLauncher : IShellLauncher
    {
        public List<string> OpenedUrls { get; } = [];

        public bool Open(string pathOrUrl)
        {
            OpenedUrls.Add(pathOrUrl);
            return true;
        }

        public bool Reveal(string path) => true;

        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class FakeYouTubeOAuthService : IYouTubeOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged
        {
            add { }
            remove { }
        }

        public int DraftFlowCalls { get; private set; }

        public Func<PlatformAuthRole, string, string, Action<string>, Task<YouTubeOAuthFlowResult>>? OnDraftFlow { get; set; }

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string[]? scopes,
            Action<string> onAuthUrlReady,
            CancellationToken ct = default)
        {
            throw new NotSupportedException();
        }

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string[]? scopes,
            Action<string> onAuthUrlReady,
            CancellationToken ct = default)
        {
            DraftFlowCalls++;
            return OnDraftFlow is not null
                ? OnDraftFlow(role, clientId, clientSecret, onAuthUrlReady)
                : Task.FromException<YouTubeOAuthFlowResult>(new InvalidOperationException("No flow handler configured"));
        }

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
            => Task.FromResult(string.Empty);

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => false;

        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false)
        {
        }
    }

    private sealed class FakeDialogService : IDialogService
    {
        public List<string> Warnings { get; } = [];

        public List<string> Infos { get; } = [];

        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public bool Confirm(string title, string message, bool defaultYes = false) => true;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => true;

        public void Info(string title, string message) => Infos.Add(message);

        public void Warning(string title, string message) => Warnings.Add(message);

        public void Error(string title, string message) => throw new InvalidOperationException(message);
    }
}
