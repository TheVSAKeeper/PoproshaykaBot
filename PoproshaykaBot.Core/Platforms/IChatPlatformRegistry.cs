using PoproshaykaBot.Core.Streaming;
using System.Diagnostics.CodeAnalysis;

namespace PoproshaykaBot.Core.Platforms;

public interface IChatPlatformRegistry
{
    IReadOnlyList<IChatPlatform> All { get; }

    bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IChatPlatform? chatPlatform);

    IChatPlatform? Find(StreamingPlatform platform);

    IChatPlatform GetRequired(StreamingPlatform platform);

    IReadOnlyList<IChatPlatform> GetEnabled();

    IReadOnlyList<IChatPlatform> GetConnected();

    IStreamStatus GetStreamStatus(StreamingPlatform platform) => GetRequired(platform).StreamStatus;

    string GetDisplayName(StreamingPlatform platform) =>
        TryGet(platform, out var found) ? found.DisplayName : platform.ToString();

    string? GetConfiguredChannel(StreamingPlatform platform) =>
        TryGet(platform, out var found) ? found.ConfiguredChannel : null;

    string? BuildStreamUrl(StreamingPlatform platform, StreamInfo? stream = null) =>
        TryGet(platform, out var found) ? found.BuildStreamUrl(stream) : null;
}
