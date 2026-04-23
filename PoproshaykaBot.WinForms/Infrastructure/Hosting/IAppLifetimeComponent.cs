namespace PoproshaykaBot.WinForms.Infrastructure.Hosting;

public interface IAppLifetimeComponent
{
    string Name { get; }

    int StartOrder { get; }

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);
}
