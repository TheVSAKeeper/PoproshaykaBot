namespace PoproshaykaBot.Core.Platforms;

[Flags]
public enum PlatformCapabilities
{
    None = 0,
    Reply = 1,
    Whisper = 2,
    NativePolls = 4,
    ChannelMetadataEdit = 8,
    EmotePositions = 16,
    ServerChatHistory = 32,
    BackgroundStreamMonitoring = 64,
    MultiChannelMonitoring = 128,
    ViewerCountSampling = 256,
}
