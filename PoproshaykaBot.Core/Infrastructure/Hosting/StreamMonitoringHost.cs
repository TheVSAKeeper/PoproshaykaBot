using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public sealed class StreamMonitoringHost(
    StreamingPlatform platform,
    IEnumerable<IStreamHostedComponent> components,
    ILogger<StreamMonitoringHost> logger)
    : AppHost(components, logger)
{
    public StreamMonitoringHost(
        IEnumerable<IStreamHostedComponent> components,
        ILogger<StreamMonitoringHost> logger)
        : this(StreamingPlatform.Twitch, components, logger)
    {
    }

    public StreamingPlatform Platform { get; } = platform;
}
