using Microsoft.Extensions.DependencyInjection;
using PoproshaykaBot.Core.Statistics;
using Serilog;

namespace PoproshaykaBot.Wpf.Bootstrap;

public static class StatisticsBootstrap
{
    public static async Task LoadUserStatisticsAsync(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.GetService<StatisticsPreload>() is { } preload)
        {
            await preload.EnsureLoadedAsync().ConfigureAwait(false);
            return;
        }

        await LoadCoreAsync(services.GetRequiredService<UserStatisticsLoader>(), CancellationToken.None).ConfigureAwait(false);
    }

    internal static async Task LoadCoreAsync(UserStatisticsLoader loader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loader);

        try
        {
            await loader.EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Log.ForContext(typeof(StatisticsBootstrap))
                .Warning(exception, "Статистика пользователей не прочитана – страница «Пользователи» останется пустой");
        }
    }
}
