using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Twitch.Auth;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.IO;
using System.Net;

namespace PoproshaykaBot.Wpf.Tests.Settings;

[TestFixture]
public sealed class PlatformOAuthAccountViewModelTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PoproshaykaBotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Test]
    public void Capabilities_differ_between_twitch_and_youtube_without_branching_in_viewmodel()
    {
        using var twitchVm = CreateTwitchVm(confirmResult: true);
        using var youtubeVm = CreateYouTubeVm(confirmResult: true);

        Assert.That(twitchVm.Capabilities.SupportsEmbeddedAuth, Is.True);
        Assert.That(twitchVm.Capabilities.SupportsTokenValidation, Is.True);
        Assert.That(twitchVm.Capabilities.SupportsManualTokenRefresh, Is.True);
        Assert.That(twitchVm.Capabilities.SupportsEditableScopes, Is.True);
        Assert.That(twitchVm.Capabilities.ChannelIdentityLabel, Is.Empty);
        Assert.That(twitchVm.AuthorizeInAppCommand.CanExecute(null), Is.True);

        Assert.That(youtubeVm.Capabilities.SupportsEmbeddedAuth, Is.False);
        Assert.That(youtubeVm.Capabilities.SupportsTokenValidation, Is.False);
        Assert.That(youtubeVm.Capabilities.SupportsManualTokenRefresh, Is.False);
        Assert.That(youtubeVm.Capabilities.SupportsEditableScopes, Is.False);
        Assert.That(youtubeVm.Capabilities.ShowsChannelIdentity, Is.True);
        Assert.That(youtubeVm.Capabilities.ChannelIdentityLabel, Is.EqualTo("Канал Google-аккаунта: "));
        Assert.That(youtubeVm.AuthorizeInAppCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void Clear_cancels_cleanly_when_confirmation_is_declined()
    {
        using var youtubeVm = CreateYouTubeVm(confirmResult: false);
        var settingsChanged = 0;
        youtubeVm.SettingChanged += (_, _) => settingsChanged++;

        youtubeVm.Load(new YouTubeAccountSettings
        {
            AccessToken = "yt-access",
            RefreshToken = "yt-refresh",
            ChannelId = "UC123",
            ChannelTitle = "Test Channel",
            StoredScopes = [YouTubeScopes.ForceSsl],
            AccessTokenExpiresAt = new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero),
        });

        youtubeVm.ClearCommand.Execute(null);

        Assert.That(youtubeVm.AccessTokenValue, Is.EqualTo("yt-access"));
        Assert.That(youtubeVm.RefreshTokenValue, Is.EqualTo("yt-refresh"));
        Assert.That(youtubeVm.ChannelId, Is.EqualTo("UC123"));
        Assert.That(youtubeVm.ChannelTitle, Is.EqualTo("Test Channel"));
        Assert.That(youtubeVm.HasToken, Is.True);
        Assert.That(youtubeVm.HasChannelIdentity, Is.True);
        Assert.That(settingsChanged, Is.EqualTo(0));
    }

    [Test]
    public void Manual_token_edit_clears_expiry_and_stored_scopes_and_refreshes_summary()
    {
        var fixedNow = new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(fixedNow);
        using var youtubeVm = CreateYouTubeVm(confirmResult: true, timeProvider: timeProvider);

        var draft = new YouTubeAccountSettings
        {
            AccessToken = "old-access",
            RefreshToken = "old-refresh",
            ChannelId = "UC123",
            ChannelTitle = "Test Channel",
            Scopes = [YouTubeScopes.ForceSsl],
            StoredScopes = [YouTubeScopes.ReadOnly],
            AccessTokenExpiresAt = fixedNow.AddHours(1),
        };
        youtubeVm.Load(draft);

        Assert.That(youtubeVm.HasScopeMismatch, Is.True);
        Assert.That(youtubeVm.GrantedScopesSummary, Does.Contain("youtube.readonly"));
        Assert.That(youtubeVm.TokenExpirySummary, Does.StartWith("Действует до "));

        var changedCount = 0;
        youtubeVm.SettingChanged += (_, _) => changedCount++;

        youtubeVm.AccessTokenValue = "manual-access";

        var savedTarget = new YouTubeAccountSettings();
        youtubeVm.SaveTo(savedTarget);

        Assert.That(changedCount, Is.EqualTo(1));
        Assert.That(savedTarget.AccessTokenExpiresAt, Is.Null);
        Assert.That(savedTarget.StoredScopes, Is.Empty);
        Assert.That(savedTarget.ChannelId, Is.Empty);
        Assert.That(savedTarget.ChannelTitle, Is.Empty);
        Assert.That(youtubeVm.HasChannelIdentity, Is.False);
        Assert.That(youtubeVm.GrantedScopesSummary, Is.EqualTo("–"));
        Assert.That(youtubeVm.HasScopeMismatch, Is.False);
        Assert.That(youtubeVm.TokenExpirySummary, Is.EqualTo("Срок жизни access-токена не указан"));
    }

    [Test]
    public void Status_callback_enqueued_before_dispose_is_ignored_after_dispose()
    {
        var dispatcher = new QueuedUiDispatcher();
        var ytService = new FakeYouTubeService();
        using var vm = CreateYouTubeVm(confirmResult: true, dispatcher: dispatcher, ytService: ytService);

        ytService.RaiseStatus(PlatformAuthRole.Bot, "Обновление статуса...");
        Assert.That(dispatcher.PendingCount, Is.EqualTo(1));
        Assert.That(vm.StatusMessage, Is.EqualTo(string.Empty));

        vm.Dispose();
        dispatcher.Drain();

        Assert.That(vm.StatusMessage, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task Browser_error_never_leaks_raw_exception_message_and_logs_telemetry()
    {
        var dialogs = new RecordingDialogService { ConfirmResult = true };
        var logger = new RecordingLogger();
        var ytService = new FakeYouTubeService
        {
            NextException = new YouTubeOAuthException(
                "https://oauth2.googleapis.com/token?client_secret=TOP_SECRET_LEAK",
                errorCode: "invalid_client",
                statusCode: HttpStatusCode.Unauthorized),
        };

        using var ytVm = CreateYouTubeVm(dialogs: dialogs, ytService: ytService, logger: logger);
        await ytVm.OpenBrowserCommand.ExecuteAsync(null);

        Assert.That(ytVm.StatusMessage, Does.Not.Contain("TOP_SECRET_LEAK"));
        Assert.That(ytVm.StatusMessage, Does.Contain("неверный Client ID или Client Secret Google-проекта"));
        Assert.That(dialogs.Errors, Has.Count.EqualTo(1));
        Assert.That(dialogs.Errors[0], Does.Not.Contain("TOP_SECRET_LEAK"));
        Assert.That(logger.EventIds, Does.Contain(1138));
        Assert.That(logger.EventIds, Does.Contain(1140));

        var twitchService = new FakeTwitchService
        {
            NextException = new InvalidOperationException("RAW_TWITCH_SECRET_TOKEN_XYZ"),
        };

        using var twitchVm = CreateTwitchVm(dialogs: dialogs, twitchService: twitchService, logger: logger);
        await twitchVm.OpenBrowserCommand.ExecuteAsync(null);

        Assert.That(twitchVm.StatusMessage, Does.Not.Contain("RAW_TWITCH_SECRET_TOKEN_XYZ"));
        Assert.That(dialogs.Errors, Has.Count.EqualTo(2));
        Assert.That(dialogs.Errors[1], Does.Not.Contain("RAW_TWITCH_SECRET_TOKEN_XYZ"));
    }

    [Test]
    public async Task Browser_start_and_clear_log_information_events_through_app_log()
    {
        var logger = new RecordingLogger();
        var dialogs = new RecordingDialogService { ConfirmResult = true };
        var ytService = new FakeYouTubeService();

        using var vm = CreateYouTubeVm(dialogs: dialogs, ytService: ytService, logger: logger);
        await vm.OpenBrowserCommand.ExecuteAsync(null);
        vm.ClearCommand.Execute(null);

        Assert.That(logger.EventIds, Does.Contain(1138));
        Assert.That(logger.EventIds, Does.Contain(1141));
    }

    [Test]
    public async Task Cancelled_browser_flow_does_not_reset_active_second_flow_or_overwrite_status()
    {
        var firstAbandonedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishAbandonedWithFailure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = 0;

        var ytService = new FakeYouTubeService
        {
            FlowOverride = async ct =>
            {
                var attempt = Interlocked.Increment(ref call);
                if (attempt == 1)
                {
                    firstAbandonedStarted.TrySetResult();
                    await finishAbandonedWithFailure.Task;
                    throw new InvalidOperationException("abandoned flow failed");
                }

                await secondCompleted.Task;
                return new YouTubeOAuthFlowResult("yt-at-2", "yt-rt-2", [YouTubeScopes.ForceSsl], "UC2", "SecondChan", 3600);
            },
        };

        using var vm = CreateYouTubeVm(ytService: ytService);
        var firstTask = vm.OpenBrowserCommand.ExecuteAsync(null);
        await firstAbandonedStarted.Task;

        await vm.CancelAuthorizationCommand.ExecuteAsync(null);
        Assert.That(vm.IsAuthorizing, Is.False);

        var secondTask = vm.OpenBrowserCommand.ExecuteAsync(null);
        Assert.That(vm.IsAuthorizing, Is.True);

        finishAbandonedWithFailure.TrySetResult();
        await firstTask;

        Assert.That(vm.IsAuthorizing, Is.True);
        Assert.That(vm.StatusMessage, Does.Not.StartWith("Ошибка:"));

        secondCompleted.TrySetResult();
        await secondTask;

        Assert.That(vm.IsAuthorizing, Is.False);
        Assert.That(vm.AccessTokenValue, Is.EqualTo("yt-at-2"));
        Assert.That(vm.ChannelId, Is.EqualTo("UC2"));
    }

    [Test]
    public async Task YouTube_adapter_supports_embedded_auth_when_dialog_is_provided_and_saves_browser_profile()
    {
        var profile = Guid.NewGuid().ToString("N");
        var embedded = new StubEmbeddedYouTubeAuthDialog(
            new YouTubeOAuthFlowResult("yt-emb-at", "yt-emb-rt", [YouTubeScopes.ForceSsl], "UC_EMB", "Embedded Chan", 3600)
                { BrowserProfileName = profile });
        var adapter = new YouTubeAccountAuthAdapter(
            PlatformAuthRole.Bot,
            new FakeYouTubeService(),
            () => "yt-client",
            () => "yt-secret",
            embedded);

        using var vm = new PlatformOAuthAccountViewModel(
            adapter,
            new RecordingDialogService { ConfirmResult = true },
            new StubShellLauncher(),
            new ManualUiDispatcher(),
            TimeProvider.System,
            new RecordingLogger());

        Assert.That(vm.Capabilities.SupportsEmbeddedAuth, Is.True);
        Assert.That(vm.AuthorizeInAppCommand.CanExecute(null), Is.True);

        await vm.AuthorizeInAppCommand.ExecuteAsync(null);

        var saved = new YouTubeAccountSettings();
        vm.SaveTo(saved);

        Assert.Multiple(() =>
        {
            Assert.That(saved.AccessToken, Is.EqualTo("yt-emb-at"));
            Assert.That(saved.ChannelId, Is.EqualTo("UC_EMB"));
            Assert.That(saved.BrowserProfileName, Is.EqualTo(profile));
        });
    }

    private PlatformOAuthAccountViewModel CreateTwitchVm(
        bool confirmResult = true,
        RecordingDialogService? dialogs = null,
        FakeTwitchService? twitchService = null,
        RecordingLogger? logger = null,
        TimeProvider? timeProvider = null)
    {
        var accountsStore = new AccountsStore(filePath: Path.Combine(_directory, "accounts.json"));
        var credentials = new OAuthCredentialsSnapshot("tw-client", "tw-secret", "http://localhost:8080/callback", "channel");
        var adapter = new TwitchAccountAuthAdapter(
            PlatformAuthRole.Bot,
            twitchService ?? new FakeTwitchService(),
            accountsStore,
            () => credentials,
            new StubEmbeddedTwitchAuthDialog());

        return new(
            adapter,
            dialogs ?? new RecordingDialogService { ConfirmResult = confirmResult },
            new StubShellLauncher(),
            new ManualUiDispatcher(),
            timeProvider ?? TimeProvider.System,
            logger ?? new RecordingLogger());
    }

    private static PlatformOAuthAccountViewModel CreateYouTubeVm(
        bool confirmResult = true,
        RecordingDialogService? dialogs = null,
        FakeYouTubeService? ytService = null,
        IUiDispatcher? dispatcher = null,
        TimeProvider? timeProvider = null,
        RecordingLogger? logger = null)
    {
        var adapter = new YouTubeAccountAuthAdapter(
            PlatformAuthRole.Bot,
            ytService ?? new FakeYouTubeService(),
            () => "yt-client",
            () => "yt-secret");

        return new(
            adapter,
            dialogs ?? new RecordingDialogService { ConfirmResult = confirmResult },
            new StubShellLauncher(),
            dispatcher ?? new ManualUiDispatcher(),
            timeProvider ?? TimeProvider.System,
            logger ?? new RecordingLogger());
    }

    private sealed class FakeTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class QueuedUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _queue = new();

        public bool HasAccess => false;

        public int PendingCount => _queue.Count;

        public void Invoke(Action action)
        {
            _queue.Enqueue(action);
        }

        public IUiTimer CreateTimer(TimeSpan interval, Action tick)
        {
            return new ManualUiDispatcher.ManualTimer(interval, tick);
        }

        public void Drain()
        {
            while (_queue.TryDequeue(out var action))
            {
                action();
            }
        }
    }

    private sealed class RecordingDialogService : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;

        public List<string> Errors { get; } = [];

        public Task<bool> ShowAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel) => Task.FromResult(false);

        public bool Confirm(string title, string message, bool defaultYes = false) => ConfirmResult;

        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => ConfirmResult;

        public void Info(string title, string message) { }

        public void Warning(string title, string message) { }

        public void Error(string title, string message) => Errors.Add(message);
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<int> EventIds { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            EventIds.Add(eventId.Id);
        }
    }

    private sealed class StubShellLauncher : IShellLauncher
    {
        public bool Open(string pathOrUrl) => true;

        public bool Reveal(string path) => true;

        public bool Start(string executable, params string[] arguments) => true;
    }

    private sealed class StubEmbeddedTwitchAuthDialog : IEmbeddedTwitchAuthDialog
    {
        public Task<EmbeddedTwitchAuthResult> AuthorizeAsync(EmbeddedTwitchAuthRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(EmbeddedTwitchAuthResult.Canceled("Отменено"));
        }
    }

    private sealed class StubEmbeddedYouTubeAuthDialog(YouTubeOAuthFlowResult flow) : IEmbeddedYouTubeAuthDialog
    {
        public Task<EmbeddedYouTubeAuthResult> AuthorizeAsync(EmbeddedYouTubeAuthRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(EmbeddedYouTubeAuthResult.Completed(flow));
        }
    }

    private sealed class FakeTwitchService : ITwitchOAuthService
    {
        public Exception? NextException { get; set; }

        public event Action<PlatformAuthRole, string>? StatusChanged;

        public Task<string> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default)
        {
            if (NextException is not null)
            {
                throw NextException;
            }

            return Task.FromResult("tw-token");
        }

        public Task<OAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, string? redirectUri, Action<string> onAuthUrlReady, bool checkBroadcasterChannel = true, CancellationToken ct = default)
        {
            if (NextException is not null)
            {
                throw NextException;
            }

            StatusChanged?.Invoke(role, "ok");
            return Task.FromResult(new OAuthFlowResult("tw-at", "tw-rt", scopes ?? [], "bot", "1", 3600));
        }

        public void SetAuthResult(string code, string? state) { }

        public void SetAuthError(Exception exception) { }

        public Task<bool> IsTokenValidAsync(string token, CancellationToken ct = default) => Task.FromResult(true);

        public Task<TokenValidationInfo?> ValidateAsync(string token, CancellationToken ct = default) => Task.FromResult<TokenValidationInfo?>(null);

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) => Task.FromResult("tw-at-new");

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>("tw-at");

        public void UpdateSettings(PlatformAuthRole role, string accessToken, string refreshToken, IEnumerable<string>? newScopes, string login, string userId, int expiresInSeconds = 0, bool publishAuthorizationRefreshed = false) { }

        public void ClearTokens(PlatformAuthRole role) { }
    }

    private sealed class FakeYouTubeService : IYouTubeOAuthService
    {
        public Exception? NextException { get; set; }

        public Func<CancellationToken, Task<YouTubeOAuthFlowResult>>? FlowOverride { get; set; }

        public event Action<PlatformAuthRole, string>? StatusChanged;

        public void RaiseStatus(PlatformAuthRole role, string message) => StatusChanged?.Invoke(role, message);

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default)
        {
            return StartOAuthFlowToDraftAsync(role, clientId, clientSecret, scopes, onAuthUrlReady, ct);
        }

        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default)
        {
            if (FlowOverride is not null)
            {
                return FlowOverride(ct);
            }

            if (NextException is not null)
            {
                throw NextException;
            }

            return Task.FromResult(new YouTubeOAuthFlowResult("yt-at", "yt-rt", scopes ?? [YouTubeScopes.ForceSsl], "UC1", "Chan", 3600));
        }

        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default) => Task.FromResult("yt-at-new");

        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default) => Task.FromResult<string?>("yt-at");

        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => true;

        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false) { }
    }
}
