using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Platforms;

namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public sealed class PlatformHost(
    StreamingPlatform platform,
    IEnumerable<IHostedComponent> components,
    ILogger<PlatformHost> logger)
    : AppHost(components, logger)
{
    public StreamingPlatform Platform { get; } = platform;
}
