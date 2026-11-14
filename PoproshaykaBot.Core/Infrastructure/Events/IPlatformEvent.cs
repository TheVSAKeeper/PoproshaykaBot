using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Events;

public interface IPlatformEvent : IEvent
{
    StreamingPlatform Platform { get; }
}
