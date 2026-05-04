using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoproshaykaBot.Core.Infrastructure.Events;
using PoproshaykaBot.Core.Infrastructure.Hosting;
using PoproshaykaBot.Core.Infrastructure.Hosting.Components;
using PoproshaykaBot.Core.Infrastructure.Logging;
using PoproshaykaBot.Core.Statistics;
using Serilog;

namespace PoproshaykaBot.Core.Infrastructure.Di;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddCoreInfrastructure(this IServiceCollection services, UiLogSink uiLogSink)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });

        services.AddSingleton(uiLogSink);
        services.AddHttpClient();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<InMemoryEventBus>();
        services.AddSingleton<IEventBus>(sp => sp.GetRequiredService<InMemoryEventBus>());
        services.AddSingleton<UiEventDispatcher>();
        services.AddSingleton<AppHost>();

        services.AddSingleton<StatisticsCollector>();
        services.AddSingleton<IHostedComponent, StatisticsHostedComponent>();
        services.AddSingleton<IHostedComponent, ChatDecorationsHostedComponent>();
        services.AddSingleton<IHostedComponent, BroadcastSchedulerHostedComponent>();

        services.AddEventSubscribers(typeof(InfrastructureServiceCollectionExtensions).Assembly);

        return services;
    }
}
