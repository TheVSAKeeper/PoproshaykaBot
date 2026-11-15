using System.Diagnostics.CodeAnalysis;

namespace PoproshaykaBot.Core.Platforms.Auth;

public interface IPlatformAuthRegistry
{
    IPlatformAuthService GetRequired(StreamingPlatform platform);

    bool TryGet(StreamingPlatform platform, [NotNullWhen(true)] out IPlatformAuthService? service);
}
