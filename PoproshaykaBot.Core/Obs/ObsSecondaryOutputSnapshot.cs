namespace PoproshaykaBot.Core.Obs;

public enum ObsMultistreamVendor
{
    None = 0,
    AitumStreamSuite = 1,
    AitumMultistream = 2,
    ObsOutput = 3,
}

public enum ObsSecondaryOutputEventKind
{
    None = 0,
    Started = 1,
    Stopped = 2,
}

public sealed record ObsSecondaryOutputSnapshot(
    string Name,
    string OutputId,
    string Kind,
    ObsMultistreamVendor Vendor,
    bool IsActive,
    bool IsReconnecting,
    string? Timecode,
    long? DurationMs,
    long? Bytes,
    double? Congestion,
    long? SkippedFrames,
    long? TotalFrames,
    bool CanControl,
    bool IsVertical);

public sealed record ObsSecondaryOutputEvent(
    ObsMultistreamVendor Vendor,
    ObsSecondaryOutputEventKind Kind,
    string OutputName,
    int? ErrorCode,
    string? LastError);
