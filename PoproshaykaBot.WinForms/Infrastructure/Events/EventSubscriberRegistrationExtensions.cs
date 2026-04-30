using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace PoproshaykaBot.WinForms.Infrastructure.Events;

public static class EventSubscriberRegistrationExtensions
{
    public static IServiceCollection AddEventSubscribers(this IServiceCollection services, Assembly assembly)
    {
        foreach (var type in DiscoverSubscribers(assembly))
        {
            services.AddSingleton(type);
        }

        return services;
    }

    public static void ActivateEventSubscribers(this IServiceProvider provider, Assembly assembly)
    {
        foreach (var type in DiscoverSubscribers(assembly))
        {
            provider.GetRequiredService(type);
        }
    }

    private static IEnumerable<Type> DiscoverSubscribers(Assembly assembly)
    {
        return assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false }
                        && typeof(IEventSubscriber).IsAssignableFrom(t));
    }
}
