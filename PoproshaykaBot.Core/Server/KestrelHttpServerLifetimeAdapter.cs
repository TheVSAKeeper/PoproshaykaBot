using PoproshaykaBot.Core.Infrastructure.Hosting;

namespace PoproshaykaBot.Core.Server;

internal sealed class KestrelHttpServerLifetimeAdapter(KestrelHttpServer server) : IAppLifetimeComponent
{
    public string Name => "HTTP сервер (Kestrel)";

    public int StartOrder => 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return server.StartAsync();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return server.IsRunning ? server.StopAsync() : Task.CompletedTask;
    }
}
