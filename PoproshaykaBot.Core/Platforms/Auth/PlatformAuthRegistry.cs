using System.Diagnostics.CodeAnalysis;

namespace PoproshaykaBot.Core.Platforms.Auth;

public sealed class PlatformAuthRegistry : IPlatformAuthRegistry
{
    private readonly Dictionary<StreamingPlatform, IPlatformAuthService> _services;

    public PlatformAuthRegistry(IEnumerable<IPlatformAuthService> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        _services = new Dictionary<StreamingPlatform, IPlatformAuthService>();
        foreach (var service in services)
        {
            if (!_services.TryAdd(service.Platform, service))
            {
                throw new InvalidOperationException($"Дублирующая регистрация IPlatformAuthService для площадки {service.Platform}.");
            }
        }
    }

    public IPlatformAuthService GetRequired(StreamingPlatform platform)
    {
        return _services.TryGetValue(platform, out var service)
            ? service
            : throw new KeyNotFoundException($"Сервис авторизации для площадки {platform} не зарегистрирован.");
    }

    public bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IPlatformAuthService? service)
    {
        return _services.TryGetValue(platform, out service);
    }
}
