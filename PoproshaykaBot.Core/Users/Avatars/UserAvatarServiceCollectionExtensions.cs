using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PoproshaykaBot.Core.Infrastructure;
using PoproshaykaBot.Core.Twitch;
using PoproshaykaBot.Core.Twitch.GameArt;

namespace PoproshaykaBot.Core.Users.Avatars;

public static class UserAvatarServiceCollectionExtensions
{
    public const string AvatarsDirectorySegment = "avatars";

    public static IServiceCollection AddUserAvatars(this IServiceCollection services, bool cacheOnly = false)
    {
        services.AddHttpClient(TwitchEndpoints.AvatarClient);

        services.RemoveAll<UserAvatarCacheOptions>();
        services.AddSingleton(_ => new UserAvatarCacheOptions
        {
            Directory = AppPaths.Combine(GameArtServiceCollectionExtensions.CacheDirectorySegment, AvatarsDirectorySegment),
            CacheOnly = cacheOnly,
        });

        services.TryAddSingleton<IUserAvatarCache, UserAvatarCache>();

        return services;
    }
}
