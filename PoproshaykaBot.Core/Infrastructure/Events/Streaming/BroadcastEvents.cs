using PoproshaykaBot.Core.Platforms;
using PoproshaykaBot.Core.Streaming;

namespace PoproshaykaBot.Core.Infrastructure.Events.Streaming;

public sealed record BroadcastStarted(
    Guid BroadcastId,
    StreamingPlatform FirstPlatform,
    string Channel,
    StreamInfo? Stream,
    DateTimeOffset StartedAt,
    bool IsCatchUp = false) : EventBase;

public sealed record BroadcastPlatformJoined(
    Guid BroadcastId,
    StreamingPlatform Platform,
    string Channel,
    StreamInfo? Stream,
    DateTimeOffset JoinedAt,
    bool IsCatchUp = false) : EventBase;

public sealed record BroadcastPlatformLeft(
    Guid BroadcastId,
    StreamingPlatform Platform,
    string Channel,
    DateTimeOffset LeftAt) : EventBase;

public sealed record BroadcastEnded(
    Guid BroadcastId,
    StreamingPlatform LastPlatform,
    string LastChannel,
    DateTimeOffset EndedAt) : EventBase;

public sealed record ViewerSampleObserved(
    StreamingPlatform Platform,
    string Channel,
    int ViewerCount,
    DateTimeOffset SampledAtUtc,
    string? StreamId = null) : EventBase, IPlatformEvent;
