using PoproshaykaBot.Core.Infrastructure.Hosting;

namespace PoproshaykaBot.Core.YouTube.Quota;

internal sealed class YouTubeQuotaLifetimeComponent(YouTubeQuotaStore store) : IAppLifetimeComponent
{
    public string Name => "Квота YouTube";

    public int StartOrder => 15;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        store.EnsureLoaded();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        store.TryFlush();
        return Task.CompletedTask;
    }
}
