using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Dialogs;
using System.Net;

namespace PoproshaykaBot.Wpf.Tests.Shell;

[TestFixture]
public class EmbeddedYouTubeAuthDialogTests
{
    private const string LeakedSecret = "yt-secret-0123456789";

    private static readonly EmbeddedYouTubeAuthRequest Request = new(
        PlatformAuthRole.Bot,
        "yt-client-id",
        "yt-client-secret",
        [YouTubeScopes.ForceSsl]);

    [Test]
    public async Task Successful_flow_binds_browser_profile_and_requests_close()
    {
        var oauth = new FakeYouTubeOAuthService();
        using var viewModel = Create(oauth);
        viewModel.Configure(Request);

        var closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        var flow = viewModel.RunFlowAsync();
        Assert.That(oauth.IsStarted, Is.True);

        var expected = new YouTubeOAuthFlowResult("yt-access", "yt-refresh", [YouTubeScopes.ForceSsl], "UC1", "MyYT", 3600);
        oauth.Completion.SetResult(expected);
        await flow;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.Result.Outcome, Is.EqualTo(EmbeddedYouTubeAuthOutcome.Completed));
            Assert.That(viewModel.ShouldKeepBrowserProfile, Is.True);
            Assert.That(viewModel.UserDataFolder, Is.EqualTo(WebView2UserDataFolders.ResolveYouTubeAccounts()));
            Assert.That(viewModel.Result.Flow, Is.EqualTo(expected with { BrowserProfileName = viewModel.BrowserProfileName }));
            Assert.That(Guid.TryParseExact(viewModel.BrowserProfileName, "N", out _), Is.True);
            Assert.That(closeRequests, Is.EqualTo(1));
            Assert.That(oauth.HasStatusSubscribers, Is.False);
        }
    }

    [Test]
    public async Task Cancel_aborts_flow_marks_profile_for_cleanup_and_unsubscribes()
    {
        var oauth = new FakeYouTubeOAuthService();
        using var viewModel = Create(oauth);
        viewModel.Configure(Request);

        var flow = viewModel.RunFlowAsync();
        Assert.That(oauth.HasStatusSubscribers, Is.True);

        viewModel.Cancel();
        await flow;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.Result.Outcome, Is.EqualTo(EmbeddedYouTubeAuthOutcome.Canceled));
            Assert.That(viewModel.Result.Flow, Is.Null);
            Assert.That(viewModel.ShouldKeepBrowserProfile, Is.False);
            Assert.That(oauth.HasStatusSubscribers, Is.False);
        }
    }

    [Test]
    public async Task Failure_never_leaks_secret_and_keeps_window_open_for_user()
    {
        var oauth = new FakeYouTubeOAuthService();
        using var viewModel = Create(oauth);
        viewModel.Configure(Request);

        var closeRequests = 0;
        viewModel.CloseRequested += (_, _) => closeRequests++;

        var flow = viewModel.RunFlowAsync();
        oauth.Completion.SetException(new YouTubeOAuthException(
            $"client_secret={LeakedSecret}",
            errorCode: "invalid_client",
            statusCode: HttpStatusCode.Unauthorized));
        await flow;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.Result.Outcome, Is.EqualTo(EmbeddedYouTubeAuthOutcome.Failed));
            Assert.That(viewModel.ShouldKeepBrowserProfile, Is.False);
            Assert.That(viewModel.Result.Message, Does.Not.Contain(LeakedSecret));
            Assert.That(viewModel.StatusMessage, Does.Not.Contain(LeakedSecret));
            Assert.That(viewModel.ErrorDescription, Does.Not.Contain(LeakedSecret));
            Assert.That(closeRequests, Is.Zero);
            Assert.That(oauth.HasStatusSubscribers, Is.False);
        }
    }

    [Test]
    public void Runtime_missing_switches_to_unavailable_with_download_option()
    {
        var oauth = new FakeYouTubeOAuthService();
        using var viewModel = Create(oauth);
        viewModel.Configure(Request with { IsCombined = true });

        viewModel.ShowRuntimeMissing();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.Title, Is.EqualTo("Авторизация в YouTube"));
            Assert.That(viewModel.Result.Outcome, Is.EqualTo(EmbeddedYouTubeAuthOutcome.Unavailable));
            Assert.That(viewModel.HasError, Is.True);
            Assert.That(viewModel.CanDownloadRuntime, Is.True);
            Assert.That(viewModel.Result.Message, Does.Contain("системный браузер"));
        }
    }

    [Test]
    public async Task Status_updates_filter_by_role()
    {
        var oauth = new FakeYouTubeOAuthService();
        using var viewModel = Create(oauth);
        viewModel.Configure(Request);

        var flow = viewModel.RunFlowAsync();
        oauth.RaiseStatus(PlatformAuthRole.Broadcaster, "чужая роль");
        oauth.RaiseStatus(PlatformAuthRole.Bot, "Ожидание подтверждения входа в Google...");

        Assert.That(viewModel.StatusMessage, Is.EqualTo("Ожидание подтверждения входа в Google..."));

        viewModel.Cancel();
        await flow;
    }

    private static EmbeddedYouTubeAuthDialogViewModel Create(FakeYouTubeOAuthService oauth) =>
        new(oauth, new FakeShellLauncher(), new InlineUiDispatcher(), NullLogger<EmbeddedYouTubeAuthDialogViewModel>.Instance);

    private sealed class InlineUiDispatcher : IUiDispatcher
    {
        public bool HasAccess => true;
        public void Invoke(Action action) => action();
        public IUiTimer CreateTimer(TimeSpan interval, Action tick) => throw new NotSupportedException();
    }

    private sealed class FakeShellLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;
        public bool Reveal(string path) => true;
        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class FakeYouTubeOAuthService : IYouTubeOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged;
        public bool IsStarted { get; private set; }
        public bool HasStatusSubscribers => StatusChanged is not null;
        public TaskCompletionSource<YouTubeOAuthFlowResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void RaiseStatus(PlatformAuthRole role, string message) => StatusChanged?.Invoke(role, message);

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string[]? scopes,
            Action<string> onAuthUrlReady,
            CancellationToken ct = default)
        {
            onAuthUrlReady("https://accounts.google.com/o/oauth2/v2/auth?client_id=yt-client-id&prompt=select_account%20consent");
            IsStarted = true;
            return Completion.Task.WaitAsync(ct);
        }

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(
            PlatformAuthRole role,
            string clientId,
            string clientSecret,
            string[]? scopes,
            Action<string> onAuthUrlReady,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => throw new NotSupportedException();

        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false) => throw new NotSupportedException();
    }
}
