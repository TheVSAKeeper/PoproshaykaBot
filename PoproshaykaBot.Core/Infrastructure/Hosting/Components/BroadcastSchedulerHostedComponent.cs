using PoproshaykaBot.Core.Broadcast;

namespace PoproshaykaBot.Core.Infrastructure.Hosting.Components;

internal sealed class BroadcastSchedulerHostedComponent(BroadcastScheduler scheduler) : IHostedComponent
{
    public string Name => "Планировщик автотрансляции";

    public int StartOrder => 400;

    public Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        if (scheduler.IsActive)
        {
            scheduler.Stop();
        }

        return Task.CompletedTask;
    }
}
