using PoproshaykaBot.WinForms.Statistics;

namespace PoproshaykaBot.WinForms.Infrastructure.Hosting.Components;

internal sealed class StatisticsHostedComponent(StatisticsCollector collector) : IHostedComponent
{
    public string Name => "Инициализация статистики...";

    public int StartOrder => 100;

    public async Task StartAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        await collector.StartAsync().ConfigureAwait(false);
        collector.ResetBotStartTime();
    }

    public Task StopAsync(IProgress<string> progress, CancellationToken cancellationToken)
    {
        return collector.StopAsync();
    }
}
