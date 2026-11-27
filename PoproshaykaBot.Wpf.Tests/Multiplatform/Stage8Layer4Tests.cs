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
using PoproshaykaBot.Wpf.ViewModels.Settings;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace PoproshaykaBot.Wpf.Tests.Multiplatform;

[TestFixture]
public sealed class Stage8Layer4Tests
{
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
