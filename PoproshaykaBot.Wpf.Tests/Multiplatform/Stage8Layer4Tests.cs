using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Broadcast;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Chat;
using PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Obs;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Server;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.Users;
using PoproshaykaBot.Core.YouTube;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Mcp;
using PoproshaykaBot.Wpf.Tests.Support;
using PoproshaykaBot.Wpf.ViewModels.Onboarding;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Multiplatform;

[TestFixture]
public sealed class Stage8Layer4Tests
{
    [Test]
    public async Task YouTubeOnboardingPage_IsApplicableOnlyWhenEnabledAndRequiresBotAuth()
    {
        using var editor = new YouTubeSettingsSectionViewModel(
            new StubYouTubeOAuthService(),
            new StubLauncher(),
            new StubDialogService(),
            new ManualUiDispatcher(),
            TimeProvider.System,
            NullLoggerFactory.Instance);
        using var page = new YouTubeOnboardingPageViewModel(editor);

        var disabledContext = new OnboardingContext(new AppSettings(), new(), new());
        Assert.Multiple(() =>
        {
            Assert.That(editor.ShowQuotaDiagnostics, Is.False);
            Assert.That(editor.ShowEnableToggle, Is.False);
            Assert.That(page.IsApplicable(disabledContext), Is.False);
        });

        var enabledContext = new OnboardingContext(
            new AppSettings(),
            new(),
            new(),
            new YouTubeSettings { Enabled = true });
        Assert.That(page.IsApplicable(enabledContext), Is.True);

        page.OnEnter(enabledContext);
        Assert.That(page.CanAdvance, Is.False);

        editor.ClientId = "yt-client";
        editor.ClientSecret = "yt-secret";
        editor.Channel = "@ytchan";
        Assert.That(page.CanAdvance, Is.False, "Без авторизации бота YouTube мастер не должен пускать дальше.");

        editor.Bot.Load(new YouTubeAccountSettings
        {
            ChannelId = "UC_BOT",
            ChannelTitle = "Bot Channel",
            AccessToken = "yt-bot-token",
            RefreshToken = "yt-bot-refresh",
            Scopes = [.. YouTubeScopes.BotRequired],
        });
        Assert.That(page.CanAdvance, Is.True);

        var left = await page.OnLeavingAsync(enabledContext);
        Assert.Multiple(() =>
        {
            Assert.That(left, Is.True);
            Assert.That(enabledContext.YouTubeSettings.Enabled, Is.True);
            Assert.That(enabledContext.YouTubeSettings.ClientId, Is.EqualTo("yt-client"));
            Assert.That(enabledContext.YouTubeSettings.Channel, Is.EqualTo("@ytchan"));
            Assert.That(enabledContext.YouTubeClientSecret, Is.EqualTo("yt-secret"));
            Assert.That(enabledContext.YouTubeBotAccount.ChannelId, Is.EqualTo("UC_BOT"));
        });
    }

    [Test]
    public async Task HealthCheckPage_SelectsYouTubeWhenEnabledAndDetectsMultiplatformEcho()
    {
        var messenger = new StubChatMessenger();
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        using var page = new HealthCheckPageViewModel(
            messenger,
            bus,
            NullLogger<HealthCheckPageViewModel>.Instance,
            new StubLauncher());

        var context = new OnboardingContext(
            new AppSettings(),
            new(),
            new(),
            new YouTubeSettings { Enabled = true, ClientId = "cid", Channel = "@yt" });

        page.OnEnter(context);

        Assert.Multiple(() =>
        {
            Assert.That(page.HasYouTubeEnabled, Is.True);
            Assert.That(page.SelectedPlatform, Is.EqualTo(StreamingPlatform.Twitch));
        });

        page.IsYouTubeSelected = true;
        page.ChatTestMessage = "привет ютуб";
        page.SendChatTestCommand.Execute(null);

        Assert.That(messenger.Calls, Is.EqualTo(new[] { (StreamingPlatform.YouTube, "привет ютуб") }));

        var entry = new ChatMessageData
        {
            Platform = StreamingPlatform.YouTube,
            Channel = "yt",
            MessageId = "m-yt",
            UserId = "u-bot",
            DisplayName = "bot",
            Message = "hi",
            IsBot = true,
        };
        await bus.PublishAsync(new ChatMessageReceived(
            StreamingPlatform.YouTube,
            "yt",
            "m-yt",
            "u-bot",
            "bot",
            "bot",
            "hi",
            UserStatus.Moderator,
            false,
            entry,
            IsBot: true));

        Assert.Multiple(() =>
        {
            Assert.That(page.BotStatusKnown, Is.True);
            Assert.That(page.BotStatusText, Contains.Substring("YouTube"));
        });
    }

    [Test]
    public void BotAutomation_GetStatePopulatesPlatformsAndBroadcast()
    {
        var broadcastId = Guid.NewGuid();
        var twitch = new StubPlatform(StreamingPlatform.Twitch, "Twitch", true, true, "tw_chan", StreamStatus.Online, "Stream");
        var youtube = new StubPlatform(StreamingPlatform.YouTube, "YouTube", true, false, "UC_yt", StreamStatus.Offline, null);
        var automation = new BotAutomation(
            new StubConnectionController(),
            new StubTargetProvider(),
            twitch.StreamStatus,
            new StubHttpServer(),
            new StubObs(),
            new StubSettingsManager(),
            NullLogger<BotAutomation>.Instance,
            new StubRegistry(twitch, youtube),
            new StubBroadcastState(true, broadcastId, new HashSet<StreamingPlatform> { StreamingPlatform.Twitch }));

        var state = automation.GetState();

        Assert.Multiple(() =>
        {
            Assert.That(state.TargetChannel.Login, Is.EqualTo("tw_chan"));
            Assert.That(state.Platforms, Has.Count.EqualTo(2));
            Assert.That(state.Platforms![0].Platform, Is.EqualTo(StreamingPlatform.Twitch));
            Assert.That(state.Platforms[1].Platform, Is.EqualTo(StreamingPlatform.YouTube));
            Assert.That(state.Broadcast, Is.Not.Null);
            Assert.That(state.Broadcast!.IsAnyLive, Is.True);
            Assert.That(state.Broadcast.CurrentBroadcastId, Is.EqualTo(broadcastId));
            Assert.That(state.Broadcast.LivePlatforms, Is.EqualTo(new[] { StreamingPlatform.Twitch }));
        });
    }

    [Test]
    public void BotConnectionPage_StartsMissingYouTubeWhenTwitchAlreadyConnected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pb-stage8-l4-conn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var settingsManager = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(tempDir, "settings.json"));
            var accountsStore = new Core.Settings.Stores.AccountsStore(null, Path.Combine(tempDir, "accounts.json"));
            var ytSettingsStore = new Core.Settings.Stores.YouTubeSettingsStore(null, Path.Combine(tempDir, "youtube.json"));
            var ytAccountsStore = new Core.Settings.Stores.YouTubeAccountsStore(null, Path.Combine(tempDir, "youtube-accounts.json"));
            var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
            var controller = new StubConnectionController();
            var registry = new StubRegistry(
                new StubPlatform(StreamingPlatform.Twitch, "Twitch", true, true, "tw", StreamStatus.Online, "Live"),
                new StubPlatform(StreamingPlatform.YouTube, "YouTube", true, false, "yt", StreamStatus.Offline, null));

            using var page = new BotConnectionPageViewModel(
                controller,
                bus,
                settingsManager,
                accountsStore,
                NullLogger<BotConnectionPageViewModel>.Instance,
                ytSettingsStore,
                ytAccountsStore,
                registry);

            var context = new OnboardingContext(
                new AppSettings(),
                new(),
                new(),
                new YouTubeSettings { Enabled = true, ClientId = "cid", Channel = "@yt" });

            page.OnEnter(context);

            Assert.That(controller.StartedPlatforms, Is.EqualTo(new[] { StreamingPlatform.YouTube }));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    private sealed class StubChatMessenger : IBroadcastMessenger
    {
        public List<(StreamingPlatform Platform, string Text)> Calls { get; } = [];
        public ChatEnqueueResult Send(StreamingPlatform platform, string text, ChatSendCategory category = ChatSendCategory.None)
        {
            Calls.Add((platform, text));
            return ChatEnqueueResult.Queued;
        }
        public BroadcastSendResult Send(string text, IReadOnlySet<StreamingPlatform> platforms, ChatSendCategory category = ChatSendCategory.None)
            => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());
        public BroadcastSendResult SendToConnected(string text, ChatSendCategory category = ChatSendCategory.None)
            => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());
        public BroadcastSendResult Send(Func<IChatPlatform, string?> messageFactory, IReadOnlySet<StreamingPlatform>? platforms = null, ChatSendCategory category = ChatSendCategory.None)
            => new(new Dictionary<StreamingPlatform, ChatEnqueueResult>());
    }

    private sealed class StubLauncher : IShellLauncher
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
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }

    private sealed class StubYouTubeOAuthService : IYouTubeOAuthService
    {
        public event Action<PlatformAuthRole, string>? StatusChanged { add { } remove { } }
        public Task<YouTubeOAuthFlowResult> StartOAuthFlowAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<YouTubeOAuthFlowResult> StartOAuthFlowToDraftAsync(PlatformAuthRole role, string clientId, string clientSecret, string[]? scopes, Action<string> onAuthUrlReady, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<string> RefreshTokenAsync(PlatformAuthRole role, string clientId, string clientSecret, string refreshToken, CancellationToken ct = default)
            => Task.FromResult(string.Empty);
        public Task<string?> GetAccessTokenAsync(PlatformAuthRole role, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
        public bool TryClearAccessToken(PlatformAuthRole role, string expectedToken) => false;
        public void ClearAccount(PlatformAuthRole role, bool publishAuthorizationRefreshed = false) { }
    }

    private sealed class StubPlatform(
        StreamingPlatform id,
        string displayName,
        bool isEnabled,
        bool isConnected,
        string configuredChannel,
        StreamStatus status,
        string? title) : IChatPlatform
    {
        public StreamingPlatform Id => id;
        public string DisplayName => displayName;
        public PlatformCapabilities Capabilities => PlatformCapabilities.None;
        public bool IsEnabled => isEnabled;
        public bool IsConnected => isConnected;
        public IStreamStatus StreamStatus { get; } = new StubStreamStatus(status, title is null ? null : new StreamInfo { Title = title });
        public IPlatformMessenger Messenger => null!;
        public string? ConfiguredChannel => configuredChannel;
        public string? BuildStreamUrl(StreamInfo? stream = null) => $"https://example.invalid/{configuredChannel}";
    }

    private sealed class StubRegistry(params IChatPlatform[] platforms) : IChatPlatformRegistry
    {
        public IReadOnlyList<IChatPlatform> All => platforms;
        public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform)
        {
            chatPlatform = Find(platform);
            return chatPlatform is not null;
        }
        public IChatPlatform? Find(StreamingPlatform platform) => platforms.FirstOrDefault(p => p.Id == platform);
        public IChatPlatform GetRequired(StreamingPlatform platform) => Find(platform) ?? throw new KeyNotFoundException();
        public IReadOnlyList<IChatPlatform> GetEnabled() => platforms.Where(p => p.IsEnabled).ToArray();
        public IReadOnlyList<IChatPlatform> GetConnected() => platforms.Where(p => p.IsConnected).ToArray();
    }

    private sealed class StubBroadcastState(bool isAnyLive, Guid? broadcastId, IReadOnlySet<StreamingPlatform> live) : IBroadcastState
    {
        public bool IsAnyLive => isAnyLive;
        public Guid? CurrentBroadcastId => broadcastId;
        public IReadOnlySet<StreamingPlatform> LivePlatforms => live;
        public bool IsPlatformLive(StreamingPlatform platform) => live.Contains(platform);
    }

    private sealed class StubStreamStatus(StreamStatus status, StreamInfo? info) : IStreamStatus
    {
        public StreamStatus CurrentStatus => status;
        public StreamInfo? CurrentStream => info;
        public StreamStatusSnapshot GetSnapshot() => new(status, info, null);
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class StubConnectionController : IBotConnectionController
    {
        public List<StreamingPlatform> StartedPlatforms { get; } = [];
        public bool IsBusy => false;
        public BotLifecyclePhase CurrentPhase => BotLifecyclePhase.Connected;
        public bool IsPlatformBusy(StreamingPlatform platform) => false;
        public BotLifecyclePhase GetPhase(StreamingPlatform platform) => platform == StreamingPlatform.Twitch ? BotLifecyclePhase.Connected : BotLifecyclePhase.Disconnected;
        public void StartConnection() { }
        public void StartConnection(StreamingPlatform platform) => StartedPlatforms.Add(platform);
        public void CancelConnection() { }
        public void CancelConnection(StreamingPlatform platform) { }
        public Task WaitForConnectionAsync() => Task.CompletedTask;
        public Task WaitForConnectionAsync(StreamingPlatform platform) => Task.CompletedTask;
        public Task StopAsync(BotStopMode mode = BotStopMode.Graceful) => Task.CompletedTask;
        public Task StopAsync(StreamingPlatform platform, BotStopMode mode = BotStopMode.Graceful) => Task.CompletedTask;
    }

    private sealed class StubTargetProvider : ITargetChannelProvider
    {
        public TargetChannelState Current => new("tw_chan", "tw_chan", false, true);
        public void BeginSession() { }
        public void EndSession() { }
    }

    private sealed class StubHttpServer : IHttpServerStatus
    {
        public bool IsRunning => true;
        public int? Port => 8080;
    }

    private sealed class StubObs : IObsSceneController
    {
        public bool IsConnected => false;
        public ObsConnectionSnapshot CurrentStatus => ObsConnectionSnapshot.Disconnected();
        public Task<string?> GetCurrentSceneAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task SetCurrentSceneAsync(string sceneName, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubSettingsManager() : SettingsManager(
        NullLogger<SettingsManager>.Instance,
        Path.Combine(Path.GetTempPath(), "stage8-l4-mcp", "settings.json"))
    {
        private readonly AppSettings _settings = new();
        public override AppSettings Current => _settings;
    }
}
