using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Broadcast;

public interface IBroadcastPlatformTargetsProvider
{
    IReadOnlySet<StreamingPlatform> GetTargetPlatforms();
}

public sealed class DefaultBroadcastPlatformTargetsProvider : IBroadcastPlatformTargetsProvider
{
    private static readonly IReadOnlySet<StreamingPlatform> DefaultTargets =
        new HashSet<StreamingPlatform> { StreamingPlatform.Twitch };

    public IReadOnlySet<StreamingPlatform> GetTargetPlatforms() => DefaultTargets;
}
