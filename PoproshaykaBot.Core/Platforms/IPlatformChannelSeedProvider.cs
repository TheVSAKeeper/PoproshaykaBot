using PoproshaykaBot.Core.Settings;

namespace PoproshaykaBot.Core.Platforms;

public sealed record PlatformChannelSeed(
    StreamingPlatform Platform,
    string ChannelId,
    string DisplayName,
    string Handle,
    string Url,
    bool MonitoringEnabled,
    string? BroadcasterSubjectId,
    string? BotSubjectId);

public interface IPlatformChannelSeedProvider
{
    StreamingPlatform Platform { get; }

    PlatformChannelSeed? TryCreateSeed();

    void MirrorActiveChannel(ChannelCatalogEntry entry);
}
