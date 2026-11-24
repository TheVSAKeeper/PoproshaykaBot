namespace PoproshaykaBot.Core.Obs;

public sealed record ObsDashboardSnapshot(
    ObsConnectionSnapshot Connection,
    string? CurrentSceneName,
    bool? IsStreaming,
    string? StreamTimecode,
    double? StreamCongestion,
    long? StreamSkippedFrames,
    long? StreamTotalFrames,
    bool? IsRecording,
    bool? IsRecordingPaused,
    string? RecordTimecode,
    long? RecordBytes,
    IReadOnlyList<ObsAudioSourceSnapshot> AudioSources,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ObsSecondaryOutputSnapshot> SecondaryOutputs)
{
    public ObsDashboardSnapshot(
        ObsConnectionSnapshot Connection,
        string? CurrentSceneName,
        bool? IsStreaming,
        string? StreamTimecode,
        double? StreamCongestion,
        long? StreamSkippedFrames,
        long? StreamTotalFrames,
        bool? IsRecording,
        bool? IsRecordingPaused,
        string? RecordTimecode,
        long? RecordBytes,
        IReadOnlyList<ObsAudioSourceSnapshot> AudioSources,
        DateTimeOffset UpdatedAt)
        : this(
            Connection,
            CurrentSceneName,
            IsStreaming,
            StreamTimecode,
            StreamCongestion,
            StreamSkippedFrames,
            StreamTotalFrames,
            IsRecording,
            IsRecordingPaused,
            RecordTimecode,
            RecordBytes,
            AudioSources,
            UpdatedAt,
            [])
    {
    }

    public bool IsConnected => Connection.IsConnected;

    public static ObsDashboardSnapshot Disabled()
    {
        return Unavailable(ObsConnectionSnapshot.Disconnected("OBS интеграция отключена."));
    }

    public static ObsDashboardSnapshot Unavailable(ObsConnectionSnapshot connection)
    {
        return new(connection, null, null, null, null, null, null, null,
            null, null, null, [], DateTimeOffset.Now, []);
    }
}
