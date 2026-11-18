using PoproshaykaBot.Core.Chat;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Events.Streaming;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Platforms.Auth;
using PoproshaykaBot.Core.Settings;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Tests.Fakes;

internal sealed class ThirdPlatformTestAdapter :
    IPlatformChannelAdapter,
    IPlatformChannelMonitor,
    IPlatformConnectionAdapter
{
    private readonly IEventBus _eventBus;
    private readonly PlatformChannelRegistry _channelRegistry;
    private readonly Dictionary<ChannelKey, ChannelMonitoringSnapshot> _snapshots = [];

    public ThirdPlatformTestAdapter(
        StreamingPlatform platform,
        PlatformCapabilities capabilities,
        IEventBus eventBus,
        PlatformChannelRegistry channelRegistry)
    {
        Platform = platform;
        Capabilities = capabilities;
        _eventBus = eventBus;
        _channelRegistry = channelRegistry;
        Host = new(platform, [], Microsoft.Extensions.Logging.Abstractions.NullLogger<PlatformHost>.Instance);
    }

    public StreamingPlatform Platform { get; }

    public PlatformCapabilities Capabilities { get; set; }

    public string Name => "VkLiveTest";

    public int StartOrder => 10;

    public bool IsEnabled => true;

    public PlatformHost Host { get; }

    public Dictionary<ChannelKey, ChannelLiveProbeResult> LiveProbes { get; } = [];

    public List<ChannelKey> RetargetHistory { get; } = [];

    public Task<ResolvedChannelIdentity?> ResolveChannelIdentityAsync(
        string rawInput,
        string? preferredContextId,
        CancellationToken cancellationToken)
    {
        var clean = rawInput.Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return Task.FromResult<ResolvedChannelIdentity?>(null);
        }

        var id = $"vk-{clean.ToLowerInvariant()}";
        return Task.FromResult<ResolvedChannelIdentity?>(new(new(Platform, id), clean, clean, $"https://vk.test/{clean}"));
    }

    public Task<ChannelAccessVerification> VerifyChannelAccessAsync(
        ChannelKey channel,
        PlatformAuthRole role,
        string contextId,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new ChannelAccessVerification(true, $"sub-{channel.ChannelId}"));
    }

    public Task<ChannelLiveProbeResult> ProbeChannelLiveAsync(
        ChannelCatalogEntry channel,
        CancellationToken cancellationToken)
    {
        if (LiveProbes.TryGetValue(channel.Key, out var probe))
        {
            return Task.FromResult(probe);
        }

        return Task.FromResult(new ChannelLiveProbeResult(false, true, null));
    }

    public void PublishUnsupportedMonitoring(ChannelKey channel, string displayName)
    {
        var snap = new ChannelMonitoringSnapshot(
            channel,
            displayName,
            ChannelMonitoringAvailability.Unavailable,
            MonitoringUnavailableReason.NotSupportedByPlatform,
            "Площадка не поддерживает фоновый мониторинг.",
            StreamStatus.Unknown,
            null,
            DateTimeOffset.UtcNow,
            false);
        _snapshots[channel] = snap;
        _ = _eventBus.PublishAsync(new ChannelMonitoringStateChanged(Platform, channel, snap));
    }

    public void PublishObserved(ChannelKey channel, string displayName, StreamStatus curr, string streamId, DateTimeOffset at)
    {
        var current = new ChannelMonitoringSnapshot(
            channel,
            displayName,
            ChannelMonitoringAvailability.Active,
            MonitoringUnavailableReason.None,
            null,
            curr,
            curr == StreamStatus.Online ? new StreamInfo { Id = streamId, StartedAt = at.UtcDateTime, ViewerCount = 12 } : null,
            at,
            false);
        _snapshots[channel] = current;
    }

    public ChannelMonitoringSnapshot GetSnapshot(ChannelKey channel) =>
        _snapshots.TryGetValue(channel, out var snap)
            ? snap
            : new(channel, channel.ChannelId, ChannelMonitoringAvailability.Active, MonitoringUnavailableReason.None, null, StreamStatus.Unknown, null, null, false);

    public IReadOnlyList<ChannelMonitoringSnapshot> GetSnapshots() => [.. _snapshots.Values];

    public Task RefreshAsync(ChannelKey? channel = null, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<string> BeginSessionAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        await Host.StartAsync(progress, cancellationToken);
        return _channelRegistry.GetChannel(Platform) ?? "VkMain";
    }

    public void EndSession() { }

    public Task<string> RetargetSessionAsync(PlatformChatSessionTarget target, IProgress<string> progress, CancellationToken cancellationToken)
    {
        RetargetHistory.Add(target.Channel.Key);
        _channelRegistry.SetBinding(new(target.Channel.Key, target.Channel.DisplayName, AuthContextIds.Default, AuthContextIds.Default, target.SwitchGeneration));
        return Task.FromResult(target.Channel.DisplayName);
    }
}
