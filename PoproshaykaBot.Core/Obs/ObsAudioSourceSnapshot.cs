namespace PoproshaykaBot.Core.Obs;

public sealed record ObsAudioSourceSnapshot(
    string Name,
    string Kind,
    bool IsMuted,
    double? VolumeDecibels,
    double? VolumeMultiplier);
