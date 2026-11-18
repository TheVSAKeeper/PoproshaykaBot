using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Streaming.Switching;

namespace PoproshaykaBot.Core.Platforms;

internal sealed class ChannelCatalogStartupLifetimeComponent(
    ChannelCatalogReconciler reconciler,
    ChannelSwitchCoordinator switchCoordinator) : IAppLifetimeComponent
{
    public string Name => "Каталог каналов и переключение";

    public int StartOrder => 50;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        reconciler.ReconcileStartup();
        _ = switchCoordinator;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
