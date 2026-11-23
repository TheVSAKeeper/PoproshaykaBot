using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Chat.Display;
using PoproshaykaBot.Core.Chat.History;
using PoproshaykaBot.Core.Debugging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Settings.Chat;
using PoproshaykaBot.Core.Settings.Stores;
using PoproshaykaBot.Core.Streaming;
using PoproshaykaBot.Core.Twitch.Chat;
using PoproshaykaBot.Core.YouTube.Auth;
using PoproshaykaBot.Wpf.Infrastructure;
using PoproshaykaBot.Wpf.ViewModels.Settings;
using PoproshaykaBot.Wpf.ViewModels.Tiles;
using PoproshaykaBot.Wpf.ViewModels.Tiles.Chat;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Tiles;

[TestFixture]
internal sealed class ChatImprovementsWpfTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pb-wpf-chat-imp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
        }
    }

    [Test]
    public async Task YouTubePane_SwitchesBetweenApiFallbackAndEmbeddedWebProfile()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings.json"));
        var displayStore = new ChatDisplayStore(NullLogger<ChatDisplayStore>.Instance, _tempDir);
        var ytAccounts = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_tempDir, "yt-accounts.json"));
        ytAccounts.UpsertContexts(
        [
            (PlatformAuthRole.Bot, "yt-bot-UC1", "TheVSAKeeper", new YouTubeAccountSettings
            {
                AccessToken = "tok",
                ChannelId = "UC1",
                ChannelTitle = "TheVSAKeeper",
            }),
        ]);

        var ytPlatform = new StubYouTubePlatform(new StubStreamStatus(), new StubAccountMessenger());
        var registry = new ChatPlatformRegistry([ytPlatform]);

        var profileId = Guid.NewGuid().ToString("N");
        var loginDialog = new StubYouTubeLoginDialog(EmbeddedYouTubeChatLoginResult.Completed(profileId));

        using var tile = new ChatDisplayTileViewModel(
            settings,
            new StubTargetChannelProvider(),
            NullLogger<ChatDisplayTileViewModel>.Instance,
            new StubShellLauncher(),
            new StubDialogService(),
            bus,
            displayStore,
            platformRegistry: registry,
            accountDirectories: [ytAccounts],
            youtubeChatLogin: loginDialog,
            youtubeAccountsStore: ytAccounts);

        tile.PlatformChat.SelectedAccount = tile.PlatformChat.AccountChoices.Single(o => o.Id == "UC1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tile.PlatformChat.CanSignInBrowser, Is.True);
            Assert.That(tile.PlatformChat.CanResetBrowserSession, Is.False);
            Assert.That(tile.PlatformChat.ShowMessageInput, Is.True);
        }

        await tile.PlatformChat.SignInBrowserCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tile.PlatformChat.SelectedAccount?.BrowserProfile?.ProfileName, Is.EqualTo(profileId));
            Assert.That(tile.PlatformChat.CanSignInBrowser, Is.False);
            Assert.That(tile.PlatformChat.CanResetBrowserSession, Is.True);
            Assert.That(tile.PlatformChat.ShowMessageInput, Is.False);
        }

        tile.CompletePlatformSessionReset("UC1");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tile.PlatformChat.SelectedAccount?.BrowserProfile, Is.Null);
            Assert.That(tile.PlatformChat.CanSignInBrowser, Is.True);
            Assert.That(tile.PlatformChat.CanResetBrowserSession, Is.False);
            Assert.That(tile.PlatformChat.ShowMessageInput, Is.True);
        }
    }

    [Test]
    public void MultiplatformChatTile_InsertsStreamAndDateDividersAcrossStreams()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings-div.json"));
        var historySettings = new StaticHistorySettings(new ChatSettings { ShowHistoryInApp = true });
        using var history = new ChatHistoryManager(settings, bus, historySettings);

        var stream1 = Guid.NewGuid();
        var stream2 = Guid.NewGuid();
        var t1 = new DateTime(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 11, 23, 15, 0, 0, DateTimeKind.Utc);

        history.AddMessage(new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            Channel = "bobito217",
            ChannelId = "177128531",
            BroadcastId = stream1,
            StreamId = "s-1",
            StreamStartedAtUtc = t1,
            StreamTitle = "Первый эфир",
            MessageId = "m1",
            Timestamp = t1.AddMinutes(5),
            UserId = "u1",
            DisplayName = "User1",
            Message = "Привет 1",
        });

        history.AddMessage(new ChatMessageData
        {
            Platform = StreamingPlatform.Twitch,
            Channel = "bobito217",
            ChannelId = "177128531",
            BroadcastId = stream2,
            StreamId = "s-2",
            StreamStartedAtUtc = t2,
            StreamTitle = "Второй эфир",
            MessageId = "m2",
            Timestamp = t2.AddMinutes(5),
            UserId = "u2",
            DisplayName = "User2",
            Message = "Привет 2",
        });

        using var tile = new MultiplatformChatTileViewModel(history, bus, historySettings);
        var dividers = tile.Items.OfType<ChatFeedStreamDividerItemViewModel>().ToList();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dividers, Has.Count.EqualTo(2));
            Assert.That(dividers[0].Label, Does.Contain("Первый эфир"));
            Assert.That(dividers[1].Label, Does.Contain("Второй эфир"));
        }
    }

    [Test]
    public void BasicSettingsSectionViewModel_LoadsAndSavesTwitchFetchRecentMessages()
    {
        var vm = new BasicSettingsSectionViewModel();
        var loaded = new TwitchSettings
        {
            Channel = "bobito217",
            FetchRecentMessages = true,
        };

        vm.LoadSettings(loaded);
        Assert.That(vm.FetchRecentMessages, Is.True);

        vm.FetchRecentMessages = false;
        var saved = new TwitchSettings();
        vm.SaveSettings(saved);
        Assert.That(saved.FetchRecentMessages, Is.False);
    }

    [Test]
    public void YouTubePane_IncludesBroadcasterOnlyAccount_ForFirstBrowserSignIn()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings-bc.json"));
        var displayStore = new ChatDisplayStore(NullLogger<ChatDisplayStore>.Instance, _tempDir);
        var ytAccounts = new YouTubeAccountsStore(NullLogger<YouTubeAccountsStore>.Instance, Path.Combine(_tempDir, "yt-bc.json"));
        ytAccounts.UpsertContexts(
        [
            (PlatformAuthRole.Broadcaster, "yt-broadcaster-UCB", "BroadcasterOnly", new YouTubeAccountSettings
            {
                AccessToken = "tok",
                ChannelId = "UCB",
                ChannelTitle = "BroadcasterOnly",
            }),
        ]);

        var ytPlatform = new StubYouTubePlatform(new StubStreamStatus(), new DenyAccountMessenger());
        var registry = new ChatPlatformRegistry([ytPlatform]);

        using var tile = new ChatDisplayTileViewModel(
            settings,
            new StubTargetChannelProvider(),
            NullLogger<ChatDisplayTileViewModel>.Instance,
            new StubShellLauncher(),
            new StubDialogService(),
            bus,
            displayStore,
            platformRegistry: registry,
            accountDirectories: [ytAccounts],
            youtubeChatLogin: new StubYouTubeLoginDialog(EmbeddedYouTubeChatLoginResult.Canceled()),
            youtubeAccountsStore: ytAccounts);

        var option = tile.PlatformChat.AccountChoices.SingleOrDefault(o => o.Id == "UCB");
        Assert.That(option, Is.Not.Null);
        tile.PlatformChat.SelectedAccount = option;
        Assert.That(tile.PlatformChat.CanSignInBrowser, Is.True);
    }

    [Test]
    public void EmbeddedYouTubeChatLoginDialogViewModel_BlocksConfirmUntilBrowserReadyAndSignedIn()
    {
        var vm = new ViewModels.Dialogs.EmbeddedYouTubeChatLoginDialogViewModel(
            new StubShellLauncher(),
            NullLogger<ViewModels.Dialogs.EmbeddedYouTubeChatLoginDialogViewModel>.Instance);
        vm.Configure(new("UC1", "TheVSAKeeper"));

        Assert.That(vm.ConfirmCommand.CanExecute(null), Is.False);

        vm.SetBrowserReady();
        Assert.That(vm.ConfirmCommand.CanExecute(null), Is.False);

        vm.SetSignedIn(true);
        Assert.That(vm.ConfirmCommand.CanExecute(null), Is.True);

        vm.SetSignedIn(false);
        Assert.That(vm.ConfirmCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task MultiplatformChatTile_PreservesTopDividerAtCap_ScopesModeration_AndLoadsPastBroadcastEvents()
    {
        var bus = new InMemoryEventBus(NullLogger<InMemoryEventBus>.Instance);
        var settings = new SettingsManager(NullLogger<SettingsManager>.Instance, Path.Combine(_tempDir, "settings-cap.json"));
        var historySettings = new StaticHistorySettings(new ChatSettings { ShowHistoryInApp = true });
        using var history = new ChatHistoryManager(settings, bus, historySettings);

        var streamId = Guid.NewGuid();
        var start = new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < MultiplatformChatTileViewModel.MaxFeedItems; i++)
        {
            history.AddMessage(new ChatMessageData
            {
                Platform = StreamingPlatform.Twitch,
                Channel = "bobito217",
                ChannelId = "177128531",
                BroadcastId = streamId,
                StreamId = "s-cap",
                StreamStartedAtUtc = start,
                StreamTitle = "Длинный стрим",
                MessageId = $"m-{i}",
                Timestamp = start.AddSeconds(i),
                UserId = "u1",
                DisplayName = "User1",
                Message = $"msg {i}",
            });
        }

        using var tile = new MultiplatformChatTileViewModel(history, bus, historySettings);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(tile.Items.Count, Is.LessThanOrEqualTo(MultiplatformChatTileViewModel.MaxFeedItems));
            Assert.That(tile.Items[0], Is.TypeOf<ChatFeedStreamDividerItemViewModel>());
        }

        // Verify moderation in one YouTube session does not delete another YouTube session of same user
        using var ytHistory = new ChatHistoryManager(settings, bus, historySettings);
        var scope1 = ChatHistoryScope.ForYouTube("UC1", "live-1", "YT");
        var scope2 = ChatHistoryScope.ForYouTube("UC1", "live-2", "YT");
        ytHistory.ImportHistory(scope1,
        [
            new()
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "YT",
                ChannelId = "UC1",
                ChatSessionId = "live-1",
                MessageId = "yt-m1",
                Timestamp = start,
                UserId = "u-bad",
                DisplayName = "User",
                Message = "old stream msg",
                IsHistorical = true,
            },
        ]);
        ytHistory.ImportHistory(scope2,
        [
            new()
            {
                Platform = StreamingPlatform.YouTube,
                Channel = "YT",
                ChannelId = "UC1",
                ChatSessionId = "live-2",
                MessageId = "yt-m2",
                Timestamp = start.AddHours(2),
                UserId = "u-bad",
                DisplayName = "User",
                Message = "new stream msg",
            },
        ]);

        using var ytTile = new MultiplatformChatTileViewModel(ytHistory, bus, historySettings);
        await bus.PublishAsync(new Core.Infrastructure.Events.Chat.ChatMessagesDeleted(scope2, [], "u-bad"));

        var remainingIds = ytTile.Items.OfType<ChatFeedMessageItemViewModel>().Select(static m => m.Key.MessageId).ToList();
        Assert.That(remainingIds, Is.EqualTo(new[] { "yt-m1" }));
    }

    private sealed class DenyAccountMessenger : IChatAccountMessenger
    {
        public bool CanSendAs(ChatSenderAccount account) => false;
        public Task<ChatAccountSendResult> SendAsAsync(ChatSenderAccount account, string streamId, string text) =>
            Task.FromResult(new ChatAccountSendResult(ChatEnqueueResult.NotConnected, ChatDeliveryStatus.Rejected, 0));
    }

    private sealed class StaticHistorySettings(ChatSettings current) : IChatHistorySettingsProvider
    {
        public event Action<ChatSettings>? Changed
        {
            add { }
            remove { }
        }

        public ChatSettings GetCurrent() => current;
    }

    private sealed class StubYouTubeLoginDialog(EmbeddedYouTubeChatLoginResult result) : IEmbeddedYouTubeChatLoginDialog
    {
        public Task<EmbeddedYouTubeChatLoginResult> SignInAsync(EmbeddedYouTubeChatLoginRequest request) => Task.FromResult(result);
    }

    private sealed class StubTargetChannelProvider : ITargetChannelProvider
    {
        public TargetChannelState Current { get; } = new("chan", "chan", false, true);
        public void BeginSession() { }
        public void EndSession() { }
    }

    private sealed class StubStreamStatus : IStreamStatus
    {
        public StreamStatus CurrentStatus => StreamStatus.Online;
        public StreamInfo CurrentStream { get; } = new() { Id = "video-1", Title = "Live" };
        public StreamStatusSnapshot GetSnapshot() => throw new NotSupportedException();
        public Task RefreshLiveSnapshotAsync() => Task.CompletedTask;
    }

    private sealed class StubYouTubePlatform(IStreamStatus status, IChatAccountMessenger messenger) : IChatPlatform
    {
        public StreamingPlatform Id => StreamingPlatform.YouTube;
        public string DisplayName => "YouTube";
        public PlatformCapabilities Capabilities => PlatformCapabilities.ServerChatHistory;
        public bool IsEnabled => true;
        public bool IsConnected => true;
        public IStreamStatus StreamStatus => status;
        public IPlatformMessenger Messenger => throw new NotSupportedException();
        public IChatAccountMessenger? AccountMessenger => messenger;
        public string BuildNativeChatUrl(StreamInfo stream) => $"https://www.youtube.com/live_chat?v={stream.Id}&is_popout=1";
        public string BuildStreamUrl(StreamInfo? stream = null) => $"https://www.youtube.com/watch?v={stream?.Id}";
    }

    private sealed class StubAccountMessenger : IChatAccountMessenger
    {
        public bool CanSendAs(ChatSenderAccount account) => true;
        public Task<ChatAccountSendResult> SendAsAsync(ChatSenderAccount account, string streamId, string text) =>
            Task.FromResult(new ChatAccountSendResult(ChatEnqueueResult.Queued, ChatDeliveryStatus.Sent, 1));
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
        public bool Confirm(string title, string message, bool defaultYes = false) => false;
        public bool ConfirmWarning(string title, string message, bool defaultYes = false) => false;
        public void Info(string title, string message) { }
        public void Warning(string title, string message) { }
        public void Error(string title, string message) { }
    }
}
