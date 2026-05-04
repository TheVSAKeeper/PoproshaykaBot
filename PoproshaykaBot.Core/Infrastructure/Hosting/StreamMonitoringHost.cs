using Microsoft.Extensions.Logging;

namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public sealed class StreamMonitoringHost(
    IEnumerable<IStreamHostedComponent> components,
    ILogger<StreamMonitoringHost> logger)
    : AppHost(components, logger);
