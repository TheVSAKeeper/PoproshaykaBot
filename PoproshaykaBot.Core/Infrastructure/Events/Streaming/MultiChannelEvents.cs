using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record ChannelStreamObserved(
    ChannelMonitoringSnapshot Previous,
    ChannelMonitoringSnapshot Current,
    bool IsCatchUp = false) : EventBase;

public sealed record ChannelMonitoringStateChanged(
    StreamingPlatform Platform,
    ChannelKey Channel,
    ChannelMonitoringSnapshot Snapshot) : EventBase;

public sealed record AutoDiscoverCandidate(
    ChannelKey Channel,
    string ChannelDisplayName,
    string StreamKey,
    string? StreamTitle,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset DetectedAtUtc);

public sealed record AutoDiscoverPrompt(
    StreamingPlatform Platform,
    ChannelKey? CurrentActiveChannel,
    string? CurrentActiveDisplayName,
    IReadOnlyList<AutoDiscoverCandidate> Candidates);

public sealed record AutoDiscoverPromptChanged(
    StreamingPlatform Platform,
    AutoDiscoverPrompt? Prompt) : EventBase;

public sealed record ChannelSwitchFailed(
    StreamingPlatform Platform,
    ChannelKey? FromChannel,
    ChannelKey TargetChannel,
    bool KeptPreviousChat,
    string Reason) : EventBase;

public sealed record ActiveChannelChanged(
    StreamingPlatform Platform,
    ChannelKey? PreviousChannel,
    ChannelKey NewChannel,
    string NewDisplayName,
    ChannelSwitchMode Mode) : EventBase;
