namespace PoproshaykaBot.Core.Infrastructure.Hosting;

public interface IHostedComponent
{
    string Name { get; }

    int StartOrder { get; }

    Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken);

    Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken);
}
