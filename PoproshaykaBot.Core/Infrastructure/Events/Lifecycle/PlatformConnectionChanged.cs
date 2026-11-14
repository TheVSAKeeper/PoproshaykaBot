using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events.Lifecycle;

public sealed record PlatformConnectionChanged(
    StreamingPlatform Platform,
    BotLifecyclePhase Phase,
    Exception? Exception = null)
    : EventBase, IPlatformEvent;
