using System.Collections.Concurrent;
using System.Reflection;

namespace PoproshaykaBot.WinForms.Infrastructure.Di;

public static class ServiceProviderControlHydration
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    public static void HydrateDescendants(this IServiceProvider services, Control root)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(root);

        foreach (var control in root.DescendantsAndSelf())
        {
            HydrateOne(services, control);
        }
    }

    private static void HydrateOne(IServiceProvider services, Control control)
    {
        var properties = PropertyCache.GetOrAdd(control.GetType(), GetInjectableProperties);

        foreach (var property in properties)
        {
            if (property.GetValue(control) is not null)
            {
                continue;
            }

            var value = services.GetService(property.PropertyType)
                        ?? throw new InvalidOperationException($"Failed to inject {property.PropertyType.Name} into {control.GetType().Name}.{property.Name}. "
                                                               + $"Register the dependency via services.AddSingleton/AddScoped/AddTransient.");

            property.SetValue(control, value);
        }
    }

    private static PropertyInfo[] GetInjectableProperties(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite
                        && p.GetIndexParameters().Length == 0
                        && p.GetCustomAttribute<InjectAttribute>(inherit: true) is not null)
            .ToArray();
    }
}
