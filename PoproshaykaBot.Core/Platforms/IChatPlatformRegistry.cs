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
}
