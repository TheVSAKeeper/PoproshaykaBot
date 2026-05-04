using PoproshaykaBot.Core.Twitch.Helix;

namespace PoproshaykaBot.Core.Twitch.Chat;

public interface IBotUserIdProvider
{
    Task<string?> GetAsync(CancellationToken cancellationToken);

    Task<UserInfo?> GetUserAsync(CancellationToken cancellationToken);
}
