using PoproshaykaBot.Core.Obs;

namespace PoproshaykaBot.Wpf.Bootstrap;

public static class GalleryObs
{
    private const long StreamTotalFrames = 139_000;

    public static ObsDashboardSnapshot? Find(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (string.Equals(key, SectionKeys.OverviewObs, StringComparison.OrdinalIgnoreCase))
        {
            return Create(skippedFrames: 417,
            [
                new("Звук раб. стола", "wasapi_output_capture", IsMuted: true, VolumeDecibels: 0D, VolumeMultiplier: 1D),
            ]);
        }

        if (string.Equals(key, SectionKeys.OverviewObsFull, StringComparison.OrdinalIgnoreCase))
        {
            return Create(skippedFrames: 4_170,
            [
                new("Микрофон", "wasapi_input_capture", IsMuted: false, VolumeDecibels: -3.5D, VolumeMultiplier: 0.72D),
                new("Звук рабочего стола", "wasapi_output_capture", IsMuted: false, VolumeDecibels: -12D, VolumeMultiplier: 0.41D),
                new("Музыка на фоне – плейлист стрима", "ffmpeg_source", IsMuted: false, VolumeDecibels: -24D, VolumeMultiplier: 0.18D),
                new("Оповещения", "browser_source", IsMuted: true, VolumeDecibels: 0D, VolumeMultiplier: 1D),
                new("Discord", "wasapi_process_output_capture", IsMuted: false, VolumeDecibels: -6D, VolumeMultiplier: 0.93D),
            ]);
        }

        if (string.Equals(key, SectionKeys.OverviewObsMicActive, StringComparison.OrdinalIgnoreCase))
        {
            return Create(skippedFrames: 0,
            [
                new("Микрофон", "wasapi_input_capture", IsMuted: false, VolumeDecibels: -4.2D, VolumeMultiplier: 0.68D),
                new("Звук рабочего стола", "wasapi_output_capture", IsMuted: false, VolumeDecibels: -14D, VolumeMultiplier: 0.35D),
            ]);
        }

        if (string.Equals(key, SectionKeys.OverviewObsMicMuted, StringComparison.OrdinalIgnoreCase))
        {
            return Create(skippedFrames: 0,
            [
                new("Микрофон", "wasapi_input_capture", IsMuted: true, VolumeDecibels: 0D, VolumeMultiplier: 0D),
                new("Звук рабочего стола", "wasapi_output_capture", IsMuted: false, VolumeDecibels: -14D, VolumeMultiplier: 0.35D),
            ]);
        }

        if (string.Equals(key, SectionKeys.OverviewObsReconnecting, StringComparison.OrdinalIgnoreCase))
        {
            return ObsDashboardSnapshot.Unavailable(
                new(false, null, null, "Соединение с OBS прервано"));
        }

        return null;
    }

    public static ObsDashboardSnapshot? FindSeed(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!string.Equals(key, SectionKeys.OverviewObsReconnecting, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Create(skippedFrames: 0,
        [
            new("Микрофон", "wasapi_input_capture", IsMuted: false, VolumeDecibels: -5D, VolumeMultiplier: 0.5D),
            new("Звук рабочего стола", "wasapi_output_capture", IsMuted: false, VolumeDecibels: -14D, VolumeMultiplier: 0.35D),
        ]);
    }

    private static ObsDashboardSnapshot Create(long skippedFrames, IReadOnlyList<ObsAudioSourceSnapshot> sources)
    {
        return new(new(true, "31.1.2", "5.6.2", null),
            "Сцена",
            IsStreaming: true,
            StreamTimecode: "00:38:44.120",
            StreamCongestion: 0D,
            StreamSkippedFrames: skippedFrames,
            StreamTotalFrames: StreamTotalFrames,
            IsRecording: false,
            IsRecordingPaused: false,
            RecordTimecode: "00:00:00.000",
            RecordBytes: 0,
            sources,
            DateTimeOffset.Now);
    }
}
