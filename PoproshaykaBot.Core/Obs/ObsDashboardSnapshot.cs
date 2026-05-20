namespace PoproshaykaBot.Core.Obs;

public sealed record ObsDashboardSnapshot(
    ObsConnectionSnapshot Connection,
    string? CurrentSceneName,
    bool? IsStreaming,
    string? StreamTimecode,
    bool? IsRecording,
    bool? IsRecordingPaused,
    string? RecordTimecode,
    IReadOnlyList<ObsAudioSourceSnapshot> AudioSources,
    DateTimeOffset UpdatedAt)
{
    public bool IsConnected => Connection.IsConnected;

    public static ObsDashboardSnapshot Disabled()
    {
        return Unavailable(ObsConnectionSnapshot.Disconnected("OBS интеграция отключена."));
    }

    public static ObsDashboardSnapshot Unavailable(ObsConnectionSnapshot connection)
    {
        return new(connection, null, null, null, null, null, null, [], DateTimeOffset.Now);
    }
}
