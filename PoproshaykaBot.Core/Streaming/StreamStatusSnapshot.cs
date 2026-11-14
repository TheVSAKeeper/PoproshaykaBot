namespace PoproshaykaBot.Core.Streaming;

public readonly record struct StreamStatusSnapshot(
    StreamStatus Status,
    StreamInfo? Stream,
    DateTimeOffset? ViewerSampledAtUtc)
{
    public static readonly TimeSpan DefaultViewerFreshnessWindow = TimeSpan.FromMinutes(5);

    public ViewerSampleState EvaluateViewerSample(DateTimeOffset nowUtc, TimeSpan maxAge)
    {
        if (Status != StreamStatus.Online || Stream == null || ViewerSampledAtUtc == null)
        {
            return ViewerSampleState.None;
        }

        return nowUtc - ViewerSampledAtUtc.Value <= maxAge
            ? ViewerSampleState.Fresh
            : ViewerSampleState.Stale;
    }
}
